using System.Text.Json;
using GrowDiary.Web.Infrastructure;

namespace GrowDiary.Web.Services;

/// <summary>Ein Gerät am AC-Infinity-Controller, wie der Nutzer es einträgt.</summary>
/// <param name="Name">Frei gewählt — „LED Top", „Abluft".</param>
/// <param name="LeistungEntityId">
/// Das <c>number.</c>-Feld für die Stufe 0–10. Bei der AC-Infinity-Integration
/// heisst es „… Einschaltleistung" (<c>on_power</c>).
/// </param>
/// <param name="ModusEntityId">
/// Optional das <c>select.</c>-Feld „Aktiver Modus". Wird hier nur GELESEN —
/// siehe <see cref="AcTestStand"/>.
/// </param>
/// <param name="EinZeitEntityId">
/// Das <c>time.</c>-Feld der geplanten Ein-Zeit; bei AC Infinity
/// „… Geplante Ein-Zeit". Nur noetig, wenn Grow OS den Zeitplan stellen soll.
/// </param>
/// <param name="AusZeitEntityId">Dasselbe fuer die Aus-Zeit.</param>
public sealed record AcGeraet(
    string Name,
    string LeistungEntityId,
    string? ModusEntityId,
    string? EinZeitEntityId = null,
    string? AusZeitEntityId = null);

/// <summary>Was der Test-Bereich über ein Gerät weiss.</summary>
/// <param name="Stufe">Die eingestellte Stufe 0–10, oder <c>null</c> wenn nicht lesbar.</param>
/// <param name="Modus">Der aktive Modus als Kennung (<see cref="AcModi"/>), oder <c>null</c>.</param>
/// <param name="EinZeit">Die Ein-Zeit, die der Controller MELDET — nicht die gewuenschte.</param>
/// <param name="AusZeit">Dasselbe fuer die Aus-Zeit.</param>
/// <param name="Fehler">Warum nichts gelesen werden konnte.</param>
public sealed record AcGeraetStand(
    AcGeraet Geraet, double? Stufe, string? Modus,
    string? EinZeit, string? AusZeit, string? Fehler)
{
    /// <summary>Der Modus auf Deutsch, oder <c>null</c>, wenn keiner gelesen wurde.</summary>
    public string? ModusName => Modus is null ? null : AcModi.Name(Modus);
}

/// <summary>Was Grow OS ueber die Lichtzeiten dieses Zelts schon weiss.</summary>
/// <remarks>
/// <b>Eine Wahrheit je Zahl.</b> Die Zeiten werden hier NICHT erfunden — kein
/// „18/6 in der Vegetation" aus dem Kopf. Sie kommen aus dem Lichtplan des
/// Zelts, also aus derselben Quelle, aus der auch der Waechter gegen
/// Lichteinbruch und der gelernte Zyklus lesen. Steht dort nichts, gibt es
/// keinen Vorschlag — dann traegt der Nutzer die Zeiten selbst ein.
/// </remarks>
public sealed record AcLichtplan(string Name, string Ein, string Aus);

/// <summary>Der ganze Test-Bereich eines Zelts.</summary>
public sealed record AcTestStand(
    int ZeltId,
    IReadOnlyList<AcGeraetStand> Geraete,
    bool HaVerbunden,
    bool Testbetrieb,
    AcLichtplan? Lichtplan);

/// <summary>
/// Der Versuchsaufbau: Geräte eines AC-Infinity-Controllers direkt aus Grow OS
/// stellen.
/// </summary>
/// <remarks>
/// <para><b>Warum das ausdrücklich ein Test ist.</b> Es geht um die Frage, ob
/// Grow OS die Zentrale sein kann, von der aus der ganze Grow läuft. Die
/// Antwort darauf gibt kein Entwurf, sondern ein Nutzer, der es benutzt. Also
/// steht es als eigener Menüpunkt da, mit einem Streifen obendrüber, der sagt,
/// was es ist — und nicht versteckt in den Einstellungen, wo es niemand
/// ausprobiert und niemand Rückmeldung gibt.</para>
///
/// <para><b>Warum die Einstellungen NICHT im Zelt-Schema liegen.</b> Ein Test
/// bekommt keine Spalten in einer Tabelle, die jede Bestandsinstallation
/// mitschleppt. Sie liegen als kleines JSON in
/// <see cref="AppSettingsRepository"/> unter <see cref="Schluessel"/> — das ist
/// rückstandslos wieder wegzuräumen, wenn der Versuch scheitert, und es hält
/// beliebig viele Geräte aus, ohne dass jemand ein Schema errät.</para>
///
/// <para><b>Was hier NICHT passiert.</b> Nichts regelt sich von selbst. Die
/// Seite schreibt genau dann eine Stufe, wenn jemand sie anklickt. Keine
/// Automatik, kein Zeitplan, kein Umschalten des Modus — der Controller behält
/// sein eigenes Gehirn. Sonst regelten zwei Systeme dasselbe Gerät, und genau
/// das ist die Falle, die beim Kühler eine eigene Regel bekommen hat.</para>
/// </remarks>
public static class AcTest
{
    /// <summary>Unter diesem Schlüssel liegt die Einrichtung je Zelt.</summary>
    public const string Schluessel = "ac-test";

    /// <summary>Der Ereignistyp im Anlagen-Protokoll.</summary>
    /// <remarks>
    /// Eigener Typ: ein Versuch soll im Protokoll als Versuch erkennbar sein.
    /// </remarks>
    public const string ProtokollTyp = "ac-test";

    /// <summary>Die kleinste und die grösste Stufe, die die Geräte kennen.</summary>
    /// <remarks>
    /// 0 bis 10 nach der AC-Infinity-Integration: <c>on_power</c> ist dort ein
    /// <c>number</c> mit genau diesem Bereich. 0 heisst „aus".
    /// </remarks>
    public const int StufeMin = 0;

    /// <inheritdoc cref="StufeMin"/>
    public const int StufeMax = 10;

    private static readonly JsonSerializerOptions Format = new() { WriteIndented = false };

    /// <summary>Die eingetragenen Geräte eines Zelts.</summary>
    public static IReadOnlyList<AcGeraet> Lesen(AppSettingsRepository einstellungen, int zeltId)
    {
        var roh = einstellungen.GetValue($"{Schluessel}:{zeltId}");
        if (string.IsNullOrWhiteSpace(roh)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<AcGeraet>>(roh, Format) ?? [];
        }
        catch (JsonException)
        {
            // Lieber leer als halb: eine kaputte Zeile darf die Seite nicht
            // sprengen, und der Nutzer trägt seine Geräte neu ein.
            return [];
        }
    }

    /// <summary>Die Geräte eines Zelts speichern — nach Prüfung.</summary>
    /// <returns>Was nicht in Ordnung war; leer heisst gespeichert.</returns>
    public static IReadOnlyList<string> Speichern(
        AppSettingsRepository einstellungen, int zeltId, IReadOnlyList<AcGeraet> geraete)
    {
        var maengel = Pruefen(geraete);
        if (maengel.Count > 0) return maengel;

        einstellungen.SetValue($"{Schluessel}:{zeltId}", JsonSerializer.Serialize(geraete, Format));
        return [];
    }

    /// <summary>
    /// Was an den eingetragenen Geräten nicht stimmt.
    /// </summary>
    /// <remarks>
    /// <b>Die Domäne wird geprüft, nicht nur die Leerheit.</b> Wer versehentlich
    /// eine <c>switch.</c>-Kennung in das Leistungsfeld schreibt, bekommt sonst
    /// beim Klicken einen Aufruf, der ins Leere geht — und die Seite sähe aus,
    /// als hätte sie funktioniert.
    /// </remarks>
    public static IReadOnlyList<string> Pruefen(IReadOnlyList<AcGeraet> geraete)
    {
        var maengel = new List<string>();

        for (var i = 0; i < geraete.Count; i++)
        {
            var g = geraete[i];
            var nummer = i + 1;

            if (string.IsNullOrWhiteSpace(g.Name))
            {
                maengel.Add($"Gerät {nummer}: ohne Namen findet es später niemand wieder.");
            }

            if (string.IsNullOrWhiteSpace(g.LeistungEntityId))
            {
                maengel.Add($"Gerät {nummer}: ohne Entität für die Stufe gibt es nichts zu stellen.");
            }
            else if (!g.LeistungEntityId.StartsWith("number.", StringComparison.OrdinalIgnoreCase)
                     && !g.LeistungEntityId.StartsWith("input_number.", StringComparison.OrdinalIgnoreCase))
            {
                maengel.Add(
                    $"Gerät {nummer}: „{g.LeistungEntityId}\" ist kein number-Feld. Die Stufe 0–10 "
                    + "steht bei AC Infinity unter „… Einschaltleistung\" und beginnt mit number.");
            }

            if (!string.IsNullOrWhiteSpace(g.ModusEntityId)
                && !g.ModusEntityId.StartsWith("select.", StringComparison.OrdinalIgnoreCase))
            {
                maengel.Add($"Gerät {nummer}: der Modus ist ein select-Feld, {g.ModusEntityId} nicht.");
            }

            foreach (var (feld, was) in new[]
                     {
                         (g.EinZeitEntityId, "die Ein-Zeit"),
                         (g.AusZeitEntityId, "die Aus-Zeit"),
                     })
            {
                if (!string.IsNullOrWhiteSpace(feld)
                    && !feld.StartsWith("time.", StringComparison.OrdinalIgnoreCase))
                {
                    maengel.Add($"Gerät {nummer}: {was} ist ein time-Feld, {feld} nicht.");
                }
            }

            // Halb eingetragen ist schlimmer als gar nicht: mit nur einer der
            // beiden Zeiten liesse sich kein Zeitplan stellen, die Oberflaeche
            // haette aber ein Feld ausgefuellt und saehe fertig aus.
            if (string.IsNullOrWhiteSpace(g.EinZeitEntityId)
                != string.IsNullOrWhiteSpace(g.AusZeitEntityId))
            {
                maengel.Add($"Gerät {nummer}: für einen Zeitplan braucht es BEIDE Zeiten, "
                    + "Ein und Aus — mit einer allein lässt sich nichts stellen.");
            }
        }

        return maengel;
    }

    /// <summary>Eine Uhrzeit aus der Datenbank auf HH:MM bringen.</summary>
    /// <remarks>
    /// Im Lichtplan steht mal „08:00", mal „08:00:00" — je nachdem, wer den
    /// Eintrag angelegt hat. Der Zeitplan schickt HH:MM, also wird hier
    /// gekuerzt statt an drei Stellen zu raten.
    /// </remarks>
    public static string? AlsHhMm(string? zeit)
    {
        if (string.IsNullOrWhiteSpace(zeit)) return null;
        var kurz = zeit.Trim();
        if (kurz.Length > 5) kurz = kurz[..5];
        return ZeitErlaubt(kurz) ? kurz : null;
    }

    /// <summary>Ist das eine Uhrzeit im Format HH:MM?</summary>
    /// <remarks>
    /// Rein und ohne Kultur: Home Assistant nimmt <c>time.set_value</c> nur in
    /// dieser Form entgegen. Ein deutsches „18.00" ginge stillschweigend
    /// daneben — und die Nachkontrolle meldete dann einen Fehlschlag, dessen
    /// Ursache in der Eingabe liegt und nicht in der Cloud.
    /// </remarks>
    public static bool ZeitErlaubt(string? zeit)
    {
        if (string.IsNullOrWhiteSpace(zeit)) return false;
        if (!TimeOnly.TryParseExact(zeit, "HH:mm",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _))
        {
            return false;
        }

        return true;
    }

    /// <summary>Ist das ein Zeitplan, den ein Gerät fahren kann?</summary>
    /// <remarks>
    /// <para><b>Der Anlass (02.09.2026).</b> Bis hierher wurde jede Zeit
    /// einzeln geprüft — nicht aber, ob sie zusammen einen Plan ergeben. Ein
    /// Zeitplan von 20:00 bis 20:00 ging durch, und derselbe Aufruf zwang den
    /// Modus danach auf „Schedule". Was das Gerät dann tut, hängt am
    /// Controller: eine Abluft oder ein LED-Treiber fährt ab da einen Plan
    /// ohne Dauer.</para>
    ///
    /// <para><b>Über Mitternacht ist erlaubt</b> — 12/12 in der Blüte heißt oft
    /// 20:00 bis 08:00, und das ist der Normalfall, kein Fehler.</para>
    ///
    /// <para>Wer wirklich durchgehend an will, stellt den Modus auf Dauerlauf
    /// statt einen Plan ohne Dauer einzutragen.</para>
    /// </remarks>
    public static bool ZeitplanErlaubt(string? ein, string? aus)
        => ZeitErlaubt(ein) && ZeitErlaubt(aus)
           && !string.Equals(ein!.Trim(), aus!.Trim(), StringComparison.Ordinal);

    /// <summary>Liegt die Stufe im erlaubten Bereich?</summary>
    /// <remarks>
    /// Rein, damit es prüfbar ist — und ausdrücklich <b>keine</b> Deckelung:
    /// wer 15 schickt, hat sich vertan, und ein stillschweigend auf 10
    /// gesetztes Gerät wäre schlimmer als eine Fehlermeldung.
    /// </remarks>
    public static bool StufeErlaubt(double stufe)
        => stufe >= StufeMin && stufe <= StufeMax && double.IsFinite(stufe);
}
