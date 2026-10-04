using System.Text.Json;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Models;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;

namespace GrowDiary.Web.Infrastructure.KiZugriff;

/// <summary>Was Schritt 2 zu einer Anfrage über einen Schlüssel sagt.</summary>
/// <param name="SchaltbefehlZaehlen">Die Aktion schaltet ein Gerät — zählt ins Stundenfenster.</param>
/// <param name="SicherungVorher">Vorher eine Sicherung anlegen.</param>
public sealed record KiEntscheidung(
    bool Durch,
    int Status = StatusCodes.Status200OK,
    string? Code = null,
    string? Meldung = null,
    bool SchaltbefehlZaehlen = false,
    bool SicherungVorher = false)
{
    public static KiEntscheidung Weiter { get; } = new(true);

    public static KiEntscheidung Verboten(string code, string meldung) => new(false, StatusCodes.Status403Forbidden, code, meldung);
}

/// <summary>
/// Fork AI (A-003, 03.10.2026): Der Weg einer Anfrage mit Schlüssel.
/// </summary>
/// <remarks>
/// <para>Drei Schritte, so im Bauplan (<c>docs/ki-zugriff.md</c>, „Weg einer Anfrage"):</para>
/// <list type="number">
///   <item><see cref="SchluesselWegAsync"/> — in der bestehenden Sperre vor dem
///   Routing: Schlüssel prüfen, Kontext ablegen. Ruft den Rest der Kette und
///   schreibt danach Schritt 3 (Prüfprotokoll, zuletzt genutzt).</item>
///   <item><see cref="NachDemRoutingAsync"/> — direkt nach
///   <c>UseRouting</c>: erst dort ist bekannt, welche Aktion antworten wird,
///   und damit ihre Einstufung.</item>
/// </list>
/// <para>Schritt 3 hängt an Schritt 1 und nicht an Schritt 2, damit auch eine
/// Abweisung in Schritt 2 ins Protokoll kommt — und eine Ausnahme mittendrin.</para>
/// </remarks>
public static class KiZugriffSperre
{
    private static readonly JsonSerializerOptions JsonOptionen = new(JsonSerializerDefaults.Web);

    // ------------------------------------------------------------ Schritt 1

    /// <summary>
    /// Schritt 1: Den Schlüssel prüfen. Nur aufrufen, wenn
    /// <see cref="AdminAccessPolicy.IsKiSchluesselWeg"/> zutrifft.
    /// </summary>
    public static async Task SchluesselWegAsync(HttpContext context, Func<Task> weiter)
    {
        var dienst = context.RequestServices.GetRequiredService<KiZugriffDienst>();
        var klartext = KiZugriffDienst.SchluesselAusKopf(context.Request);
        var ergebnis = dienst.Pruefen(klartext, context.Connection.RemoteIpAddress);

        switch (ergebnis.Ergebnis)
        {
            case KiPruefung.ZuVieleVersuche:
                context.Response.Headers.RetryAfter = ((int)KiZugriffDienst.SperrDauer.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                // Fork AI (A-005): ein gültiger Schlüssel kommt hier nie an — Begründung an KiZugriffDienst.Pruefen.
                await FehlerSchreiben(context, StatusCodes.Status429TooManyRequests, "ki_zu_viele_versuche",
                    "Zu viele ungültige Schlüssel von dieser Adresse. Grow OS weist von hier "
                    + $"{KiZugriffDienst.SperrDauer.TotalMinutes:0} Minuten lang jeden ungültigen Schlüssel ab; ein gültiger kommt weiter durch.");
                return;

            case KiPruefung.ZugriffAus:
                // Ohne Schlüssel-Id: bei ausgeschaltetem Zugriff wird der Schlüssel gar nicht erst geprüft.
                Protokollieren(context, KiProtokollArt.ZugriffAus, $"Schlüssel abgewiesen, Zugriff für KI-Assistenten ist aus: {context.Request.Method} {context.Request.Path}", "warning", false,
                    anfrage: new KiAnfrage(null, StatusCodes.Status403Forbidden, "ki_zugriff_aus"));
                await FehlerSchreiben(context, StatusCodes.Status403Forbidden, "ki_zugriff_aus",
                    "Der Zugriff für KI-Assistenten ist in Grow OS ausgeschaltet "
                    + "(Einstellungen → Zugriff für KI-Assistenten).");
                return;

            case KiPruefung.Ungueltig:
            case KiPruefung.Gesperrt:
                Protokollieren(context, KiProtokollArt.SchluesselAbgewiesen,
                    $"{(ergebnis.Ergebnis == KiPruefung.Gesperrt ? "Gesperrter" : "Ungültiger")} Schlüssel: {context.Request.Method} {context.Request.Path}",
                    "warning", false,
                    anfrage: new KiAnfrage(ergebnis.GesperrterSchluesselId, StatusCodes.Status401Unauthorized, "ki_schluessel_ungueltig"));
                if (ergebnis.SperreBegonnen)
                {
                    Protokollieren(context, KiProtokollArt.AdresseGesperrt,
                        $"{KiZugriffDienst.FehlversucheBisSperre} ungültige Schlüssel in {KiZugriffDienst.FehlversuchFenster.TotalMinutes:0} Minuten — "
                        + $"Adresse für {KiZugriffDienst.SperrDauer.TotalMinutes:0} Minuten gesperrt.",
                        "critical", false,
                        // Was ab jetzt von dieser Adresse kommt: 429 ki_zu_viele_versuche.
                        anfrage: new KiAnfrage(ergebnis.GesperrterSchluesselId, null, "ki_zu_viele_versuche"));
                }
                context.Response.Headers.WWWAuthenticate = "Bearer";
                await FehlerSchreiben(context, StatusCodes.Status401Unauthorized, "ki_schluessel_ungueltig",
                    ergebnis.Ergebnis == KiPruefung.Gesperrt
                        ? "Dieser Schlüssel ist gesperrt. Der Betreiber kann in Grow OS einen neuen anlegen."
                        : "Der Schlüssel ist ungültig.");
                return;
        }

        var kontext = ergebnis.Kontext!;
        context.Items[KiZugriffKontext.ItemKey] = kontext;

        var ausnahme = false;
        try
        {
            await weiter();
        }
        catch
        {
            ausnahme = true;
            throw;
        }
        finally
        {
            Nachher(context, dienst, kontext, ausnahme);
        }
    }

    // ------------------------------------------------------------ Schritt 2

    /// <summary>
    /// Schritt 2: Darf dieser Schlüssel diese Aktion? Direkt nach <c>UseRouting</c>.
    /// </summary>
    public static async Task NachDemRoutingAsync(HttpContext context, Func<Task> weiter)
    {
        if (KiZugriffKontext.Aus(context) is not { } kontext)
        {
            await weiter();
            return;
        }

        // Die Fehlerseite: UseExceptionHandler führt die Anfrage nach einer
        // Ausnahme noch einmal nach /api/error aus — mit denselben Items, also
        // mit Kontext. Ohne diese Ausnahme würde aus der 500 eine 403
        // (ApiErrorController ist nicht für Schlüssel eingestuft), und der
        // Assistent erführe nie, dass seine Aktion gescheitert ist.
        if (context.Features.Get<IExceptionHandlerFeature>() is not null)
        {
            await weiter();
            return;
        }

        var entscheidung = Entscheiden(kontext, context.GetEndpoint(), context.Request.Method, context.Request.Path);
        if (!entscheidung.Durch)
        {
            await FehlerSchreiben(context, entscheidung.Status, entscheidung.Code!, entscheidung.Meldung!);
            return;
        }

        var dienst = context.RequestServices.GetRequiredService<KiZugriffDienst>();
        if (entscheidung.SchaltbefehlZaehlen
            && !dienst.SchaltbefehlZulassen(kontext.Hoechstwerte.MaxSchaltbefehleJeStunde))
        {
            await FehlerSchreiben(context, StatusCodes.Status429TooManyRequests, "ki_hoechstwert",
                $"Höchstwert erreicht: über KI-Assistenten sind höchstens {kontext.Hoechstwerte.MaxSchaltbefehleJeStunde} "
                + "Schalt- und Dosierbefehle je Stunde erlaubt. Der Betreiber kann den Wert in Grow OS ändern.");
            return;
        }

        if (entscheidung.SicherungVorher)
        {
            var sicherung = context.RequestServices.GetRequiredService<IKiSicherung>();
            var datei = sicherung.Anlegen(context.RequestServices);
            if (datei is null)
            {
                await FehlerSchreiben(context, StatusCodes.Status503ServiceUnavailable, "ki_sicherung_fehlgeschlagen",
                    "Vor dieser Aktion legt Grow OS eine Sicherung an, und das ist gerade nicht gelungen. "
                    + "Es wurde nichts ausgeführt.");
                return;
            }
            Protokollieren(context, KiProtokollArt.SicherungVorher,
                $"Sicherung {datei} vor {context.Request.Method} {context.Request.Path} über KI-Assistent ‚{kontext.SchluesselName}‘.",
                "info", true, datei,
                new KiAnfrage(kontext.SchluesselId, null, null));
        }

        await weiter();
    }

    /// <summary>
    /// Die Regel aus dem Bauplan — ohne Seiteneffekte, damit sie sich einzeln prüfen lässt.
    /// </summary>
    /// <remarks>
    /// <para><b>Aktion gewinnt vor Controller.</b> Die Endpunkt-Metadaten führen
    /// die Attribute des Controllers zuerst, die der Aktion danach. Das letzte
    /// von <see cref="KiStufeAttribute"/> oder <see cref="KeinKiZugriffAttribute"/>
    /// ist also das der Aktion, wenn sie eines trägt.</para>
    ///
    /// <para><b>Verwaltungswege</b> (<see cref="AdminAccessPolicy.IsAdminPath"/>)
    /// brauchen immer Verwaltung — lesend wie schreibend, zusätzlich zur Stufe der
    /// Aktion. Sie sind schon heute für ein Nachbar-Add-on auch lesend zu.</para>
    /// </remarks>
    public static KiEntscheidung Entscheiden(KiZugriffKontext kontext, Endpoint? endpunkt, string methode, PathString pfad)
    {
        // Kein Endpunkt: die App antwortet ohnehin 404.
        if (endpunkt is null || IstFallback(endpunkt)) return KiEntscheidung.Weiter;

        var lesend = HttpMethods.IsGet(methode);
        var verwaltungsweg = AdminAccessPolicy.IsAdminPath(pfad);
        if (lesend && !verwaltungsweg) return KiEntscheidung.Weiter;

        var einstufung = endpunkt.Metadata.LastOrDefault(m => m is KiStufeAttribute or KeinKiZugriffAttribute);

        if (einstufung is KeinKiZugriffAttribute kein)
        {
            return KiEntscheidung.Verboten("ki_kein_zugriff",
                $"Das ist über einen KI-Assistenten nie erreichbar: {kein.Grund}");
        }

        if (verwaltungsweg && !kontext.Darf(KiStufe.Verwaltung))
        {
            return StufeFehlt(KiStufe.Verwaltung);
        }

        if (lesend) return KiEntscheidung.Weiter;

        if (einstufung is not KiStufeAttribute { Stufe: not KiStufe.Keine } stufe)
        {
            return KiEntscheidung.Verboten("ki_nicht_eingestuft",
                "Diese Aktion ist für KI-Assistenten nicht freigegeben: sie ist keiner Stufe zugeordnet.");
        }

        if (!kontext.Darf(stufe.Stufe)) return StufeFehlt(stufe.Stufe);

        return new KiEntscheidung(
            Durch: true,
            // Per Bit: eine Aktion kann mehrere Stufen verlangen (etwa Verwaltung | GeraeteSchalten).
            // Ein Sicherheitsbefehl wie der Pumpen-Stopp zählt nie (KiOhneHoechstwertAttribute).
            SchaltbefehlZaehlen: (stufe.Stufe & KiStufe.GeraeteSchalten) != 0
                                 && endpunkt.Metadata.GetMetadata<KiOhneHoechstwertAttribute>() is null,
            SicherungVorher: endpunkt.Metadata.GetMetadata<KiSicherungVorherAttribute>() is not null);
    }

    /// <remarks>Öffentlich seit A-003 Etappe B (03.10.2026): <c>KiHomeAssistantApiController</c>
    /// verlangt je Domain zusätzlich Verwaltung und antwortet mit genau dieser Meldung.</remarks>
    public static KiEntscheidung StufeFehlt(KiStufe stufe)
        => KiEntscheidung.Verboten("ki_stufe_fehlt",
            $"Dafür fehlt die Freigabe für Stufe „{KiZugriffDienst.Anzeigename(stufe)}“. "
            + "Der Betreiber kann sie in Grow OS für diesen Schlüssel anhaken.");

    /// <summary>
    /// Der SPA-Fallback aus <c>Program.cs</c> (<c>MapFallback</c>): er beantwortet
    /// einen unbekannten /api-Weg mit 404 und schreibt nichts.
    /// </summary>
    /// <remarks>
    /// <c>MapFallback</c> setzt die Reihenfolge auf <see cref="int.MaxValue"/> — so
    /// erkennt ihn auch ASP.NET selbst. Jeder andere Endpunkt ohne Controller
    /// läuft durch die normale Regel und ist ohne Einstufung gesperrt.
    /// </remarks>
    private static bool IstFallback(Endpoint endpunkt)
        => endpunkt is RouteEndpoint { Order: int.MaxValue }
           && endpunkt.Metadata.GetMetadata<ControllerActionDescriptor>() is null;

    // ------------------------------------------------------------ Schritt 3

    private static void Nachher(HttpContext context, KiZugriffDienst dienst, KiZugriffKontext kontext, bool ausnahme)
    {
        try
        {
            dienst.ZuletztGenutzt(kontext.SchluesselId);
        }
        catch
        {
            // Der Zeitstempel darf die Antwort nie kippen.
        }

        var lesend = HttpMethods.IsGet(context.Request.Method);
        if (lesend && !AdminAccessPolicy.IsAdminPath(context.Request.Path)) return;

        var status = ausnahme ? StatusCodes.Status500InternalServerError : context.Response.StatusCode;
        var erfolg = status < 400;
        var haDienst = GemerkterHaDienst(context);
        Protokollieren(context,
            lesend ? KiProtokollArt.VerwaltungLesend : KiProtokollArt.Schreibend,
            $"über KI-Assistent ‚{kontext.SchluesselName}‘: {context.Request.Method} {context.Request.Path} → {status}"
            + (haDienst is null ? string.Empty : $" (Home Assistant: {haDienst})"),
            erfolg ? "info" : "warning",
            erfolg,
            anfrage: new KiAnfrage(kontext.SchluesselId, status, ausnahme ? null : GemerkterFehlercode(context), haDienst));
    }

    // --------------------------------------------------------------- Hilfe

    /// <summary>Unter diesem Schlüssel in <see cref="HttpContext.Items"/> liegt der Fehlercode der Antwort.</summary>
    private const string FehlercodeItemKey = "GrowOs.KiZugriff.Fehlercode";

    /// <summary>
    /// Fork AI (A-003, 03.10.2026): Den Fehlercode einer Antwort an eine Anfrage über
    /// einen Schlüssel fürs Protokoll vormerken.
    /// </summary>
    /// <remarks>
    /// Schritt 3 sieht nur den Status. Ob eine 403 „Stufe fehlt" oder „nie
    /// erreichbar" hiess, steht im Körper, der da schon unterwegs ist. Die
    /// Abweisungen hier tun das selbst (<see cref="FehlerSchreiben"/>); ein
    /// Controller mit eigener KI-Abweisung ruft das hier (Dosier-Höchstwert).
    /// </remarks>
    public static void FehlercodeMerken(HttpContext? context, string code)
    {
        if (context is not null) context.Items[FehlercodeItemKey] = code;
    }

    /// <summary>Der vorgemerkte Fehlercode dieser Anfrage — oder null.</summary>
    public static string? GemerkterFehlercode(HttpContext context)
        => context.Items.TryGetValue(FehlercodeItemKey, out var code) ? code as string : null;

    /// <summary>Unter diesem Schlüssel in <see cref="HttpContext.Items"/> liegt der gerufene Home-Assistant-Dienst.</summary>
    private const string HaDienstItemKey = "GrowOs.KiZugriff.HaDienst";

    /// <summary>
    /// Fork AI (Prüferbefund 04.10.2026): Den Home-Assistant-Dienst einer Anfrage über
    /// einen Schlüssel fürs Protokoll vormerken — etwa <c>light.turn_on → light.zelt</c>.
    /// </summary>
    /// <remarks>
    /// Schritt 3 sieht nur <c>POST /api/ki-ha/dienst</c>; was dahinter in Home
    /// Assistant geschaltet wurde, weiss nur der Controller. Er ruft das hier, sobald
    /// Domain und Dienst die Form bestanden haben — also auch für eine Abweisung.
    /// <b>Nie die <c>daten</c></b>: darin kann stehen, was nicht ins Protokoll gehört.
    /// </remarks>
    public static void HaDienstMerken(HttpContext? context, string domain, string dienst, string? entityId)
    {
        if (context is null) return;
        context.Items[HaDienstItemKey] = entityId is null ? $"{domain}.{dienst}" : $"{domain}.{dienst} → {entityId}";
    }

    /// <summary>Der vorgemerkte Home-Assistant-Dienst dieser Anfrage — oder null.</summary>
    public static string? GemerkterHaDienst(HttpContext context)
        => context.Items.TryGetValue(HaDienstItemKey, out var dienst) ? dienst as string : null;

    private static void Protokollieren(HttpContext context, string aktion, string zusammenfassung, string schwere, bool erfolg,
        string? datei = null, KiAnfrage? anfrage = null)
    {
        try
        {
            context.RequestServices.GetService<SystemAuditRepository>()?.Add(new SystemAuditEvent
            {
                EventType = "security",
                Action = aktion,
                Summary = zusammenfassung,
                Severity = schwere,
                Source = KiProtokollArt.Quelle,
                RemoteAddress = context.Connection.RemoteIpAddress?.ToString(),
                RelatedFileName = datei,
                Success = erfolg,
                // Fork AI (A-003, 03.10.2026): Schlüssel und Anfrage als eigene Spalten,
                // damit „Was die KI zuletzt getan hat" nach dem Schlüssel filtern kann.
                KiSchluesselId = anfrage?.SchluesselId,
                Methode = context.Request.Method,
                Pfad = context.Request.Path.Value,
                HttpStatus = anfrage?.Status,
                Fehlercode = anfrage?.Fehlercode,
                HaDienst = anfrage?.HaDienst,
            });
        }
        catch
        {
            // Wie TryLogAdminAccess in Program.cs: das Protokoll darf keine Anfrage blockieren.
        }
    }

    /// <summary>Eine Fehlerantwort im Format aller anderen (<see cref="ApiErrorFactory"/>).</summary>
    public static async Task FehlerSchreiben(HttpContext context, int status, string code, string meldung)
    {
        FehlercodeMerken(context, code);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            ApiErrorFactory.Create(code, meldung, status, traceId: context.TraceIdentifier),
            JsonOptionen);
    }
}
