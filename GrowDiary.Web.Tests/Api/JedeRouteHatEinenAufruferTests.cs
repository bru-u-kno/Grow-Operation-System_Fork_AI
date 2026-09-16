using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Jeder Endpunkt wird von jemandem gerufen — oder hat einen ausgeschriebenen Grund.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (02.09.2026).</b> Beim Durchgehen der ungeprüften Klassen
/// kamen zwei Controller heraus, die <b>erreichbar</b> sind und die
/// <b>niemand</b> ruft:</para>
///
/// <list type="bullet">
///   <item><c>GET /api/system/network</c> gibt die privaten LAN-Adressen der
///   Maschine heraus — nachgeprüft an der laufenden App, HTTP 200 mit der
///   echten Adresse darin. Sein Nachfolger <c>GET /api/system/mobile-access</c>
///   wird von <c>MobilePage.tsx</c> benutzt; dieser hier von nichts.</item>
///   <item><c>GET /api/live/home</c> rechnet in einer Schleife über alle Zelte
///   und fragt dabei <b>Home Assistant an der echten Anlage</b> ab. Die
///   Live-Seite holt in Wahrheit je Zelt einzeln.</item>
/// </list>
///
/// <para><b>Warum eine Zählung und nicht zwei Löschungen.</b> Beide sind nicht
/// aus Nachlässigkeit entstanden, sondern beim Umbau von MVC auf React liegen
/// geblieben — und beim nächsten Umbau bleibt wieder etwas liegen. Ein
/// Endpunkt ohne Aufrufer ist nicht harmlos: er ist Angriffsfläche, er hält
/// eine zweite Wahrheit am Leben, und er kostet beim Lesen Zeit.</para>
///
/// <para><b>Als Aufrufer zählt</b>, wer die Route wirklich anspricht: die
/// React-Oberfläche, die Playwright-Mappe, das MCP-Add-on und
/// <c>GrowOsAccess</c>. Ein Vorkommen in einem <i>Kommentar</i> oder in der
/// Doku zählt nicht — eine Erwähnung ist keine Verwendung.</para>
/// </remarks>
public sealed class JedeRouteHatEinenAufruferTests
{
    /// <summary>
    /// Endpunkte ohne Aufrufer — jeder mit ausgeschriebenem Grund.
    /// </summary>
    /// <remarks>
    /// Der Schlüssel ist <c>METHODE /pfad/mit/{platzhaltern}</c>, genau wie ihn
    /// die Meldung unten ausgibt.
    /// </remarks>
    /// <remarks>
    /// <para>Zwei Gruppen, beide gewollt. Der Fehlerbehandler
    /// <c>/api/error</c> steht <b>nicht</b> hier: er hat einen echten Aufrufer
    /// (<c>app.UseExceptionHandler("/api/error")</c> in <c>Program.cs</c>) —
    /// genau dafür zählt <c>GrowDiary.Web</c> selbst als Aufrufer-Ort.</para>
    /// </remarks>
    private static readonly Dictionary<string, string> OHNE_AUFRUFER = new(StringComparer.OrdinalIgnoreCase)
    {
        // --- Betriebszugang: fuer den Entwickler, per curl, ohne Knopf -------
        //
        // Diese sieben sind kein Versehen, sondern eine eigene Flaeche: sie
        // stehen ausdruecklich in AdminAccessPolicy.ProtectedPrefixes, sind
        // also nur ueber Ingress oder Loopback erreichbar, und sie stehen im
        // API-Verzeichnis, ueber das man sie findet. Ein Knopf dafuer waere
        // falsch — sie beantworten Fragen, die vor einem Release aufkommen,
        // nicht beim Anbauen.
        ["GET /api/system/api-manifest"] =
            "Das Verzeichnis, ueber das man die uebrigen Betriebs-Endpunkte findet. Wer es "
            + "aufruft, sucht sie gerade — ein Knopf in der Oberflaeche waere hier sinnlos.",
        ["GET /api/system/database-status"] =
            "Zustand der Datenbank vor einem Release: Groesse, Tabellen, offene Wanderungen.",
        ["GET /api/system/error-contract"] =
            "Die Fehlerformate, gegen die EinFehlerformatFuerAlleTests zaehlt. Zum Nachsehen, "
            + "wenn ein Endpunkt anders antwortet als erwartet.",
        ["GET /api/system/migration-status"] =
            "Welche Datenbank-Wanderungen gelaufen sind. Die Frage stellt sich nach einem "
            + "Update, nicht im Betrieb.",
        ["GET /api/system/release-readiness"] =
            "Die Vorpruefung vor einem Release. Sie gehoert in die Freigabe, nicht auf eine Seite.",
        ["GET /api/system/security-status"] =
            "Welche Schutzmassnahmen greifen. Zum Nachsehen, nicht zum Anzeigen.",
        ["POST /api/system/backup/{fileName}/restore"] =
            "Das Gegenstueck zum Sicherungs-Download, den ReleasePage.tsx anbietet. Wer eine "
            + "Sicherung zurueckspielt, ueberschreibt Daten — dafuer gibt es bewusst KEINEN "
            + "Knopf, sondern erst den Plan (restore-plan) und dann diesen Aufruf von Hand.",
        ["GET /api/system/backup/{fileName}/validate"] =
            "Prueft eine Sicherungsdatei, BEVOR jemand sie zurueckspielt. Gehoert zum selben "
            + "Weg von Hand wie restore-plan und restore.",
        ["GET /api/system/migration-plan"] =
            "Was eine Datenbank-Wanderung tun WUERDE. Die Frage stellt sich vor einem Update, "
            + "nicht im Betrieb.",
        ["POST /api/exports/grows/validate"] =
            "Prueft eine Export-Datei, bevor ReleasePage.tsx sie ueber import-plan und import "
            + "einspielt. Der Weg dorthin geht ueber die beiden, nicht ueber diesen.",
        ["GET /api/grows/{growId:int}/chronik"] =
            "Die Chronik eines Grows: was wann geaendert wurde. Bis zum 02.09.2026 sammelte die "
            + "App diese Zeilen SCHREIB-ONLY — vier Controller schrieben hinein, niemand kam "
            + "heran. Man liest sie nicht taeglich, sondern wenn etwas passiert ist; ob und wie "
            + "sie auf einer Seite erscheint, ist eine Gestaltungsfrage.",
        ["GET /api/system/audit-events"] =
            "Das Protokoll kritischer Backend-Vorgaenge (SystemAuditEvents): Lichtflanken, "
            + "Nachtabsenkung, Sicherungen. Zum Nachsehen, wenn etwas passiert ist — dafuer "
            + "gibt es keinen Knopf, weil man es nicht taeglich liest.",

        // --- Alte Lesezeichen bekommen eine Antwort statt eines 404 ---------
        //
        // Vier Stummel aus der Zeit vor der deutschen Oberflaeche. Jeder ist
        // eine Zeile, jeder hat einen Test, und jeder beantwortet genau eine
        // Frage: was passiert, wenn jemand die alte Adresse noch gespeichert
        // hat. Loeschen hiesse, ihm ein 404 zu zeigen, wo eine Weiterleitung
        // steht.
        ["GET /tents"] = "Weiterleitung auf /zelte — alte Lesezeichen.",
        ["GET /tents/{id:int}"] = "Weiterleitung auf /zelte/{id} — alte Lesezeichen.",
        ["GET /grows/{id:int}/export"] =
            "Weiterleitung auf /api/exports/grows/{id}. Festgehalten von "
            + "LegacyMvcEndpointContainmentTests.GrowsLegacyExport_RedirectsToVersionedApiExport.",
        ["GET /settings/backup"] =
            "Antwortet mit 410 Gone und der Kennung legacy_backup_disabled, statt eine rohe "
            + "SQLite-Datei herauszugeben. Festgehalten von "
            + "LegacyMvcEndpointContainmentTests.SettingsBackupDatabase_DoesNotReturnRawSqliteDatabase.",
    };

    /// <summary>Wo ein Aufruf stehen darf.</summary>
    /// <remarks>
    /// <para><b>Warum <c>GrowDiary.Web</c> mit dabei ist.</b> Manche Adressen
    /// baut das Backend selbst und reicht sie als Link weiter — die Sicherung
    /// etwa: <c>$"/api/system/backup/{Uri.EscapeDataString(fileName)}"</c> geht
    /// als <c>safetyBackupDownloadUrl</c> an <c>ReleasePage.tsx</c>, die daraus
    /// ein <c>&lt;a href&gt;</c> macht. Ohne diese Quelle meldete die Zählung
    /// den Download als tot, obwohl der Knopf da ist.</para>
    ///
    /// <para><b>Aber die Route belegt sich nicht selbst.</b> Attribut-Zeilen
    /// (<c>[HttpGet("…")]</c>, <c>[Route("…")]</c>) werden vorher entfernt.
    /// Genau diese Falle ist <c>routes-reachable</c> schon einmal
    /// zugeschnappt: eine erfundene Route belegte sich durch ihre eigene
    /// Deklaration.</para>
    /// </remarks>
    private static readonly string[] AUFRUFER_ORTE =
    [
        Path.Combine("GrowDiary.React", "src"),
        Path.Combine("GrowDiary.React", "e2e"),
        "GrowOsAccess",
        "GrowMcp",
        "GrowDiary.Web",
    ];

    /// <summary>
    /// Dateien, die Routen <b>aufzählen</b> statt sie zu rufen.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Anlass (02.09.2026), gefunden in der eigenen Zählung.</b>
    /// Nach dem ersten Aufräumen war sie grün — und liess trotzdem drei tote
    /// Routen durch (<c>/tents/{id}/camera.jpg</c>, <c>/camera-stream</c>,
    /// <c>/latest-snapshot</c>). „Belegt" waren sie durch das API-Verzeichnis,
    /// das sie auflistet, und durch die Zugriffsregel, die sie schützt.</para>
    ///
    /// <para>Ein Katalog ist kein Aufrufer, und ein Wächter erst recht nicht:
    /// beide beschreiben, was es gibt. Genau dieselbe Falle wie „eine Route
    /// belegt sich nicht selbst" — nur eine Datei weiter.</para>
    /// </remarks>
    private static readonly string[] NUR_VERZEICHNISSE =
    [
        "SystemApiController.StatusEndpoints.cs",
        "AdminAccessPolicy.cs",
    ];

    [Fact]
    public void JedeRouteWirdVonJemandemGerufen()
    {
        var routen = AlleRouten().ToList();

        // Mengenwaechter: ohne Routen prueft der Rest nichts.
        Assert.True(routen.Count >= 100,
            $"Nur {routen.Count} Routen gefunden — die Zaehlung sieht ihre Grundmenge nicht "
            + "und waere auch bei jedem toten Endpunkt gruen.");

        var quelltext = AufruferQuelltext();
        Assert.True(quelltext.Length > 100_000,
            $"Nur {quelltext.Length} Zeichen Aufrufer-Quelltext gelesen — dann findet die "
            + "Zaehlung nichts und meldet ALLES als tot.");

        var verwaist = new List<string>();
        foreach (var (schluessel, muster) in routen)
        {
            if (OHNE_AUFRUFER.ContainsKey(schluessel)) continue;
            if (muster.IsMatch(quelltext)) continue;
            verwaist.Add(schluessel);
        }

        Assert.True(verwaist.Count == 0,
            "Diese Endpunkte ruft niemand:\n  " + string.Join("\n  ", verwaist.Order())
            + "\n\nEin Endpunkt ohne Aufrufer ist nicht harmlos: er ist Angriffsflaeche, er "
            + "haelt eine zweite Wahrheit am Leben, und er kostet beim Lesen Zeit. Entweder "
            + "loeschen oder mit ausgeschriebenem Grund in OHNE_AUFRUFER eintragen.");
    }

    /// <summary>Jeder eingetragene Grund gilt einer Route, die es wirklich gibt.</summary>
    /// <remarks>
    /// Ein Tippfehler im Schlüssel machte die Ausnahme wirkungslos — und die
    /// Zählung meldete den Endpunkt weiter, bis jemand die Ausnahme „repariert",
    /// indem er den Endpunkt einträgt, den er gerade sieht.
    /// </remarks>
    [Fact]
    public void JedeAusnahmeGiltEinerEchtenRoute()
    {
        var vorhanden = AlleRouten().Select(r => r.Schluessel).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var erfunden = OHNE_AUFRUFER.Keys.Where(k => !vorhanden.Contains(k)).ToList();

        Assert.True(erfunden.Count == 0,
            "Diese Ausnahmen nennen eine Route, die es nicht gibt: " + string.Join(", ", erfunden)
            + ". Eine Ausnahme mit Tippfehler ist wirkungslos.");
    }

    /// <summary>Der Selbsttest: findet das Muster einen echten Aufruf?</summary>
    /// <remarks>
    /// Eine Zählung mit kaputtem Muster meldet <i>alles</i> als tot oder
    /// <i>nichts</i> — beides unbrauchbar. Hier steht ausgeschrieben, welche
    /// Schreibweisen der Oberfläche getroffen werden müssen.
    /// </remarks>
    [Theory]
    // So schreibt die Oberflaeche wirklich (woertlich aus dem Quelltext).
    [InlineData("api/grows/{growId:int}/tasks", "apiFetch(`/api/grows/${grow.id}/tasks`)", true)]
    [InlineData("api/hardware-items", "apiFetch<HardwareItem[]>('/api/hardware-items')", true)]
    [InlineData("api/hardware-items/{id:int}", "api.delete(`/api/hardware-items/${id}`)", true)]
    [InlineData("api/system/mobile-access", "apiFetch('/api/system/mobile-access')", true)]
    // Und was NICHT zaehlen darf: ein anderer Pfad, der zufaellig so anfaengt.
    [InlineData("api/live/home", "apiFetch('/api/live/homepage-alt')", false)]
    [InlineData("api/system/network", "apiFetch('/api/system/network-status')", false)]
    public void DasMusterTrifftEchteAufrufe(string vorlage, string quelle, bool erwartet)
    {
        var muster = MusterFuer(vorlage);

        Assert.True(muster.IsMatch(quelle) == erwartet,
            $"Das Muster fuer „{vorlage}\" sagt zu <{quelle}> das Gegenteil von dem, was es "
            + "soll. Eine Zaehlung mit kaputtem Muster meldet alles als tot oder nichts.");
    }

    /// <summary>
    /// Ein Vorkommen im <b>Kommentar</b> zählt nicht als Aufruf.
    /// </summary>
    /// <remarks>
    /// „Eine Erwähnung ist keine Verwendung" (<c>CLAUDE.md</c>). Ein
    /// auskommentierter Aufruf oder ein Hinweis „früher lag das unter …" hielte
    /// den toten Endpunkt sonst am Leben — und die Zählung wäre grün, während
    /// niemand den Endpunkt ruft.
    /// </remarks>
    [Theory]
    [InlineData("// frueher: apiFetch('/api/system/network')", "")]
    [InlineData("const a = 1 // siehe /api/live/home", "const a = 1 ")]
    [InlineData("/* apiFetch('/api/live/home') */", " ")]
    [InlineData("apiFetch('/api/live/home') // echt", "apiFetch('/api/live/home') ")]
    public void EinKommentarZaehltNichtAlsAufruf(string quelle, string erwartet)
    {
        Assert.True(OhneKommentare(quelle).TrimEnd() == erwartet.TrimEnd(),
            $"Aus <{quelle}> wurde <{OhneKommentare(quelle)}>, erwartet war <{erwartet}>. "
            + "Ein auskommentierter Aufruf haelt sonst einen toten Endpunkt am Leben.");
    }

    // ------------------------------------------------------------------ Hilfe

    /// <summary>
    /// Ein Muster, das die Route in der Schreibweise der Aufrufer trifft.
    /// </summary>
    /// <remarks>
    /// <para><b>Zwei Wege, denn die Oberfläche schreibt auf zwei Arten.</b></para>
    ///
    /// <para>Meistens steht der Pfad am Stück da:
    /// <c>apiFetch(`/api/grows/${id}/tasks`)</c>. Platzhalter werden zu
    /// „alles ausser Trennzeichen".</para>
    ///
    /// <para>Manchmal wird er aber <b>zur Laufzeit zusammengesetzt</b>:
    /// <c>const route = … : 'confirm-finish'</c> und erst danach
    /// <c>`/api/grows/${growId}/actions/${route}`</c>. Ein reiner
    /// Pfadvergleich meldete diese fünf Endpunkte als tot, obwohl der Knopf
    /// dafür auf jeder Grow-Seite steht. Deshalb zählt zusätzlich der
    /// <b>kennzeichnende Namensteil</b> — das letzte feste Stück der Route,
    /// wenn es unverwechselbar genug ist (mit Bindestrich oder länger als
    /// sieben Zeichen). „id" oder „new" zählen nicht.</para>
    /// </remarks>
    private static Regex MusterFuer(string vorlage)
    {
        var teile = vorlage.Trim('/').Split('/');

        // Ein Platzhalter steht fuer alles ausser Trennzeichen: die Oberflaeche
        // schreibt dort `${grow.id}` — kein Schraegstrich, keine Anfuehrung.
        const string platzhalter = @"[^/'""`\s]+";

        var pfad = string.Join("/", teile.Select(teil =>
            teil.StartsWith('{') ? platzhalter : Regex.Escape(teil)));

        /* Vorn POSITIV verankert: vor dem Pfad muss eine Zeichenkette anfangen.

           Erst stand hier gar keine Abgrenzung, dann „kein Wortzeichen davor" —
           beides liess Zufallstreffer durch. „/tents" steckt in
           „/api/live/tents/${id}", und in einer E2E-Datei stand es escapt in
           einem regulaeren Ausdruck (`\/api\/live\/tents\/`), wo vor dem
           Schraegstrich ein Backslash steht und keine Wortgrenze.

           Ein echter Aufruf faengt mit einem Anfuehrungszeichen an. Eine Route,
           die nur zusammengesetzt vorkommt, faellt dadurch als verwaist auf —
           das ist die ungefaehrliche Richtung: ein Mensch sieht hin. */
        var muster = "(?<=['\"`])/" + pfad + @"(?![\w-])";

        var letztesFeste = teile.LastOrDefault(teil => !teil.StartsWith('{'));
        if (letztesFeste is not null
            && (letztesFeste.Contains('-') || letztesFeste.Length > 7))
        {
            /* Auch vorn abgegrenzt: „audit-events" steckt in
               „system-audit-events", und der Endpunkt galt dadurch als
               gerufen — von einer Zeichenkette in seinem eigenen Katalog. */
            muster += @"|(?<![\w-])" + Regex.Escape(letztesFeste) + @"(?![\w-])";
        }

        return new Regex(muster, RegexOptions.Compiled);
    }

    private static IEnumerable<(string Schluessel, Regex Muster)> AlleRouten()
    {
        var assembly = typeof(GrowDiary.Web.Services.Bauzeit).Assembly;

        foreach (var typ in assembly.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract))
        {
            var amTyp = typ.GetCustomAttribute<RouteAttribute>()?.Template?.Trim('/') ?? string.Empty;

            foreach (var methode in typ.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (methode.IsSpecialName) continue;

                var verben = methode.GetCustomAttributes<HttpMethodAttribute>().ToList();

                /* Eine oeffentliche Aktion OHNE Verb-Attribut ist trotzdem eine
                   Route: sie erbt die Route der Klasse und nimmt JEDES Verb an.

                   Das ist der fuenfte blinde Fleck dieser Zaehlung, und ich habe
                   ihn selbst gerissen: beim Loeschen des Kamera-Alias am
                   02.09.2026 blieb `GetTentCameraStatus` ohne Attribut stehen.
                   Sie hing danach unter `/api`, antwortete auf GET, POST und
                   DELETE mit 200 und loeste bei eingerichtetem Home Assistant
                   einen Kamera-Abruf an der ECHTEN Anlage aus. Die Zaehlung war
                   gruen, weil sie nur Verb-Attribute las. */
                if (verben.Count == 0)
                {
                    yield return ($"(ohne Verb) /{amTyp}", new Regex("(?!)"));
                    continue;
                }

                foreach (var attribut in verben)
                {
                    var eigene = attribut.Template?.Trim('/') ?? string.Empty;

                    /* Eine Vorlage mit "/" oder "~/" am Anfang ersetzt die Route
                       am Typ. Ohne den Tilde-Fall stand in der ersten Fassung
                       "GET /api/companion/~/api/grows/{growId:int}/due-sops" in
                       der Meldung — eine Route, die es so nie gab. */
                    var roh = attribut.Template ?? string.Empty;
                    var ersetztTyp = roh.StartsWith('/') || roh.StartsWith("~/");
                    var vorlage = ersetztTyp
                        ? roh.TrimStart('~').Trim('/')
                        : string.Join("/", new[] { amTyp, eigene }.Where(t => t.Length > 0));

                    if (vorlage.Length == 0) continue;

                    var verb = attribut.HttpMethods.FirstOrDefault() ?? "GET";
                    yield return ($"{verb} /{vorlage}", MusterFuer(vorlage));
                }
            }
        }
    }

    /// <summary>Der Quelltext aller Stellen, an denen ein Aufruf stehen darf.</summary>
    private static string AufruferQuelltext()
    {
        var wurzel = ProjektWurzel();
        var teile = new List<string>();

        foreach (var ort in AUFRUFER_ORTE)
        {
            var pfad = Path.Combine(wurzel, ort);
            if (!Directory.Exists(pfad)) continue;

            foreach (var datei in Directory.EnumerateFiles(pfad, "*.*", SearchOption.AllDirectories))
            {
                var endung = Path.GetExtension(datei);
                if (endung is not (".ts" or ".tsx" or ".cs" or ".json")) continue;
                if (datei.Contains("node_modules") || datei.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    || datei.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;

                if (NUR_VERZEICHNISSE.Contains(Path.GetFileName(datei), StringComparer.OrdinalIgnoreCase)) continue;

                var inhalt = OhneKommentare(File.ReadAllText(datei));
                if (endung == ".cs") inhalt = OhneRoutenAttribute(inhalt);
                teile.Add(inhalt);
            }
        }

        return string.Join("\n", teile);
    }

    /// <summary>Quelltext ohne Kommentare — eine Erwähnung ist keine Verwendung.</summary>
    /// <remarks>
    /// <para>Ein auskommentierter Aufruf oder ein Hinweis „früher lag das unter
    /// …" hielte einen toten Endpunkt sonst am Leben, und die Zählung wäre
    /// grün, während ihn niemand ruft.</para>
    ///
    /// <para><b>Absichtlich grob.</b> Ein <c>//</c> in einer Zeichenkette
    /// (<c>"https://…"</c>) schneidet hier zu viel weg. Das ist die
    /// ungefährliche Richtung: es kann einen Aufruf übersehen und einen
    /// Endpunkt fälschlich als tot melden — dann sieht ein Mensch hin. Die
    /// andere Richtung wäre still.</para>
    /// </remarks>
    private static string OhneKommentare(string quelle)
    {
        var ohneBloecke = Regex.Replace(quelle, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return Regex.Replace(ohneBloecke, @"//[^\n]*", string.Empty);
    }

    /// <summary>
    /// Quelltext ohne Routen-Attribute — <b>eine Route belegt sich nicht selbst.</b>
    /// </summary>
    /// <remarks>
    /// Genau diese Falle ist <c>routes-reachable</c> schon einmal zugeschnappt:
    /// die Suche las die Datei mit, in der die Routen stehen, und eine
    /// erfundene Route belegte sich dadurch selbst.
    /// </remarks>
    private static string OhneRoutenAttribute(string quelle)
        => Regex.Replace(quelle, @"\[\s*(Http(Get|Post|Put|Delete|Patch|Head)|Route)\s*\([^\]]*\]",
            string.Empty, RegexOptions.Singleline);

    private static string ProjektWurzel()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "GrowDiary.Web"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Projektwurzel nicht gefunden.");
    }
}
