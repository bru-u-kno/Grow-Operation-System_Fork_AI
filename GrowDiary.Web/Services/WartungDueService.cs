using System.Globalization;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>Etwas, das gewartet, getauscht oder gesichert gehört.</summary>
public sealed record WartungsPunkt(
    string Bereich,
    string Titel,
    string Stufe,
    string Meldung,
    string Herkunft);

/// <summary>Woher eine Wartungsfrist kommt.</summary>
public enum WartungsFristQuelle
{
    /// <summary>Ein geplanter Eintrag trägt seine Frist selbst.</summary>
    Geplant,
    /// <summary>Der Folgetermin, den der letzte Abschluss genannt hat (<c>NextDueAtUtc</c>).</summary>
    Folgetermin,
    /// <summary>Letzter Abschluss plus Intervall am Gerät.</summary>
    LetzterAbschluss,
    /// <summary>Einbaudatum plus Intervall — es gibt noch keinen Abschluss.</summary>
    Einbau,
    /// <summary>Weder Frist am Eintrag noch Abschluss noch Einbaudatum.</summary>
    Keine,
}

/// <summary>Was wann fällig ist — je Gerät und Art (Prüfen, Kalibrieren).</summary>
/// <param name="Schluessel">Stabil je Zeile: <c>wartung-{id}</c> für einen geplanten Eintrag, <c>wartung-t-{geraet}</c> für eine gerechnete Frist.</param>
/// <param name="Art"><c>wartung</c> oder <c>kalibrierung</c>.</param>
/// <param name="Titel">Der Titel des geplanten Eintrags, sonst „Prüfen" bzw. „Kalibrieren".</param>
/// <param name="HardwareItemId">Das Gerät.</param>
/// <param name="Geraet">Sein Name aus dem Geräte-Eintrag.</param>
/// <param name="FaelligUtc">Die Frist; <c>null</c>, wenn es keine gibt.</param>
/// <param name="Quelle">Woher die Frist kommt.</param>
/// <param name="BezugUtc">Wovon aus gerechnet wurde — letzter Abschluss oder Einbau.</param>
/// <param name="IntervallTage">Das Intervall am Gerät, falls eins eingetragen ist.</param>
public sealed record WartungsFrist(
    string Schluessel,
    string Art,
    string Titel,
    int HardwareItemId,
    string Geraet,
    DateTime? FaelligUtc,
    WartungsFristQuelle Quelle,
    DateTime? BezugUtc,
    int? IntervallTage)
{
    /// <summary>Ohne Frist steht der Eintrag nur zur Information da.</summary>
    public bool OhneFrist => FaelligUtc is null;
}

/// <summary>
/// Liest die Termine, die bisher nur herumlagen: Lebensdauer, Prüfintervall, Sicherung.
/// </summary>
/// <remarks>
/// <para><b>Dasselbe Muster wie beim Wasserwechsel.</b> An jedem Gerät stehen
/// <c>ExpectedLifespanDays</c> und <c>InspectionIntervalDays</c> — sie werden aus
/// der Verschleiss-Vorlage vorbefüllt, gespeichert, angezeigt und von keinem
/// Dienst je ausgewertet. Wer keinen Wartungstermin von Hand anlegt, wird nie
/// erinnert; der Luftstein sitzt dann drei Jahre im Eimer.</para>
///
/// <para><b>Die Zahlen sind seine eigenen.</b> Hier wird nichts über
/// Lebensdauern behauptet — gerechnet wird mit dem, was am Gerät steht (aus der
/// Vorlage oder von Hand). Nur die Sicherungs-Frist ist eine Setzung, und die
/// sagt der Text auch.</para>
/// </remarks>
public sealed class WartungDueService
{
    /// <summary>Ab wann eine Sicherung als alt gilt — Faustregel, keine Wissenschaft.</summary>
    public const int SicherungAlterTage = 30;

    /// <summary>Ab welchem Anteil der Lebensdauer vorgewarnt wird.</summary>
    /// <remarks>Bei 90 % bleibt Zeit zum Bestellen, bevor das Teil wirklich fällig ist.</remarks>
    private const double VorwarnAnteil = 0.9;

    private readonly HardwareRepository _hardware;
    private readonly AppPaths _paths;
    private readonly SopDueService _stufe;

    public WartungDueService(HardwareRepository hardware, AppPaths paths, SopDueService stufe)
    {
        _hardware = hardware;
        _paths = paths;
        _stufe = stufe;
    }

    public IReadOnlyList<WartungsPunkt> Offen(DateTime nowUtc)
    {
        // Wartung ist eine Erinnerung, keine Gefahrenmeldung — anders als der
        // Pumpen-Waechter richtet sie sich deshalb nach der Begleitungsstufe.
        var stufe = _stufe.Stufe;
        if (stufe == "expert") return [];

        // Ausgemustert ist dieselbe Grenze wie in FristenRechnen.
        var geraete = _hardware.GetHardwareItems()
            .Where(g => g.Status != HardwareItemStatus.Retired)
            .ToList();

        var punkte = Beurteilen(geraete, Fristen(), LetzteSicherung(), nowUtc);
        return stufe == "important"
            ? punkte.Where(p => p.Stufe == "kritisch").ToList()
            : punkte;
    }

    /// <summary>Die Fristen aller Geräte aus der Datenbank — gerechnet von <see cref="FristenRechnen"/>.</summary>
    public IReadOnlyList<WartungsFrist> Fristen()
        => FristenRechnen(_hardware.GetHardwareItems(), _hardware.GetMaintenanceEvents(), _hardware.GetCalibrationEvents());

    /// <summary>
    /// Wann ist an welchem Gerät was fällig? Die eine Rechnung dafür — der
    /// Wartungs-Reiter zeigt sie (<c>api/maintenance-due/fristen</c>),
    /// <see cref="Offen"/> erinnert daraus.
    /// </summary>
    /// <remarks>
    /// <para><b>Woher die Frist kommt.</b> Schließt jemand eine Wartung oder
    /// Kalibrierung ab, rechnet <c>HardwareRepository.CompleteMaintenanceEvent</c>
    /// bzw. <c>CompleteCalibrationEvent</c> den Folgetermin (<c>NextDueAtUtc</c>)
    /// und legt dafür einen geplanten Eintrag an. Diese Daten sind die Wahrheit:</para>
    /// <list type="number">
    /// <item>Ein geplanter Eintrag trägt seine Frist selbst — auch ohne Intervall am Gerät.</item>
    /// <item>Sonst gilt der Folgetermin des letzten Abschlusses dieser Art,</item>
    /// <item>sonst letzter Abschluss plus Intervall,</item>
    /// <item>und erst ohne jeden Abschluss das Einbaudatum plus Intervall.</item>
    /// </list>
    /// <para>Abgesagte und übersprungene Termine zählen nicht; ausgemusterte
    /// Geräte auch nicht — weder ihre Intervalle noch liegengebliebene Termine.
    /// Eine gescheiterte Kalibrierung hat trotzdem stattgefunden; auch nach ihr
    /// plant das Backend den Folgetermin.</para>
    /// <para><b>Fork AI (02.10.2026).</b> Vorher stand diese Rechnung zweimal:
    /// im Wartungs-Reiter (<c>wartung-zeilen.ts</c>, aus allen Einträgen) und hier
    /// (nur letzte Prüfung oder Einbau, ohne Kalibrierung und ohne geplante
    /// Termine). Ein Gerät konnte im Reiter „in 11 T" stehen und hier schon
    /// überfällig sein.</para>
    /// </remarks>
    public static IReadOnlyList<WartungsFrist> FristenRechnen(
        IEnumerable<HardwareItem> geraete,
        IEnumerable<MaintenanceEvent> wartungen,
        IEnumerable<CalibrationEvent> kalibrierungen)
    {
        var aktiv = new Dictionary<int, HardwareItem>();
        foreach (var geraet in geraete)
        {
            if (geraet.Status != HardwareItemStatus.Retired) aktiv.TryAdd(geraet.Id, geraet);
        }

        var arten = new (string Art, string Titel, List<FristEintrag> Eintraege, Func<HardwareItem, int?> Intervall)[]
        {
            ("wartung", "Prüfen",
                wartungen.Select(e => new FristEintrag(e.Id, e.HardwareItemId, e.Title,
                    e.Status == MaintenanceEventStatus.Planned,
                    e.Status == MaintenanceEventStatus.Completed,
                    e.DueAtUtc, e.PerformedAtUtc, e.NextDueAtUtc)).ToList(),
                g => g.InspectionIntervalDays),
            ("kalibrierung", "Kalibrieren",
                kalibrierungen.Select(e => new FristEintrag(e.Id, e.HardwareItemId, e.Title,
                    e.Status == CalibrationEventStatus.Planned,
                    e.Status is CalibrationEventStatus.Completed or CalibrationEventStatus.Failed,
                    e.DueAtUtc, e.PerformedAtUtc, e.NextDueAtUtc)).ToList(),
                g => g.CalibrationIntervalDays),
        };

        var fristen = new List<WartungsFrist>();
        foreach (var (art, titel, alle, intervall) in arten)
        {
            var eigene = alle.Where(e => aktiv.ContainsKey(e.HardwareItemId)).ToList();

            // 1. Geplante Einträge tragen ihre Frist selbst.
            var geplant = eigene.Where(e => e.Geplant).ToList();
            foreach (var e in geplant)
            {
                var geraet = aktiv[e.HardwareItemId];
                fristen.Add(new WartungsFrist(
                    $"{art}-{e.Id}", art, e.Titel, geraet.Id, geraet.Name,
                    e.DueAtUtc, e.DueAtUtc is null ? WartungsFristQuelle.Keine : WartungsFristQuelle.Geplant,
                    null, intervall(geraet) is > 0 and { } t ? t : null));
            }

            // 2.–4. Geräte mit Intervall, aber ohne geplanten Eintrag dieser Art.
            var mitTermin = geplant.Select(e => e.HardwareItemId).ToHashSet();
            foreach (var geraet in aktiv.Values)
            {
                if (intervall(geraet) is not (> 0 and { } tage) || mitTermin.Contains(geraet.Id)) continue;
                var zuletzt = eigene
                    .Where(e => e.HardwareItemId == geraet.Id && e.Abgeschlossen && e.PerformedAtUtc is not null)
                    .MaxBy(e => e.PerformedAtUtc);

                DateTime? faellig = null;
                DateTime? bezug = null;
                var quelle = WartungsFristQuelle.Keine;
                if (zuletzt?.NextDueAtUtc is { } naechster)
                {
                    (faellig, quelle, bezug) = (naechster, WartungsFristQuelle.Folgetermin, zuletzt.PerformedAtUtc);
                }
                else if (zuletzt?.PerformedAtUtc is { } wann)
                {
                    (faellig, quelle, bezug) = (wann.AddDays(tage), WartungsFristQuelle.LetzterAbschluss, wann);
                }
                else if (geraet.InstalledAtUtc is { } eingebaut)
                {
                    (faellig, quelle, bezug) = (eingebaut.AddDays(tage), WartungsFristQuelle.Einbau, eingebaut);
                }

                fristen.Add(new WartungsFrist(
                    $"{art}-t-{geraet.Id}", art, titel, geraet.Id, geraet.Name, faellig, quelle, bezug, tage));
            }
        }

        // Nach Frist; ohne Frist ans Ende, dort nach Gerät. OrderBy ist stabil —
        // gleiche Fristen behalten die Reihenfolge von oben.
        var deutsch = StringComparer.Create(CultureInfo.GetCultureInfo("de-DE"), ignoreCase: false);
        return fristen
            .OrderBy(f => f.FaelligUtc is null)
            .ThenBy(f => f.FaelligUtc ?? DateTime.MaxValue)
            .ThenBy(f => f.FaelligUtc is null ? f.Geraet : string.Empty, deutsch)
            .ToList();
    }

    /// <summary>Wartung und Kalibrierung auf einen Nenner gebracht.</summary>
    private sealed record FristEintrag(
        int Id, int HardwareItemId, string Titel, bool Geplant, bool Abgeschlossen,
        DateTime? DueAtUtc, DateTime? PerformedAtUtc, DateTime? NextDueAtUtc);

    /// <summary>Wann zuletzt gesichert wurde — die Datei selbst ist der Beleg.</summary>
    /// <remarks>
    /// Kein eigener Zeitstempel in den Einstellungen: der wäre eine zweite
    /// Wahrheit, die auseinanderlaufen kann. Was zählt, ist, ob eine Sicherung
    /// wirklich daliegt.
    /// </remarks>
    public DateTime? LetzteSicherung()
    {
        var ordner = _paths.BackupsPath;
        if (!Directory.Exists(ordner)) return null;

        DateTime? neueste = null;
        foreach (var datei in Directory.EnumerateFiles(ordner, "*.zip"))
        {
            var wann = File.GetLastWriteTimeUtc(datei);
            if (neueste is null || wann > neueste) neueste = wann;
        }
        return neueste;
    }

    /// <summary>
    /// Die reine Rechnung — ohne Datenbank, ohne Dateisystem.
    /// </summary>
    /// <param name="geraete">Für die Lebensdauer; gefiltert wird vom Aufrufer.</param>
    /// <param name="fristen">Aus <see cref="FristenRechnen"/>. Erinnert wird nur an
    /// gerechnete Fristen — geplante Einträge stehen auf der Aktionsseite schon
    /// als Termine und kämen sonst doppelt.</param>
    /// <param name="letzteSicherung">Wann zuletzt gesichert wurde.</param>
    /// <param name="nowUtc">Jetzt.</param>
    public static IReadOnlyList<WartungsPunkt> Beurteilen(
        IReadOnlyList<HardwareItem> geraete,
        IReadOnlyList<WartungsFrist> fristen,
        DateTime? letzteSicherung,
        DateTime nowUtc)
    {
        var punkte = new List<WartungsPunkt>();

        foreach (var geraet in geraete)
        {
            if (geraet.InstalledAtUtc is not { } eingebaut) continue;
            var tageSeitEinbau = (int)(nowUtc.Date - eingebaut.Date).TotalDays;

            if (geraet.ExpectedLifespanDays is > 0 and { } lebensdauer)
            {
                if (tageSeitEinbau >= lebensdauer)
                {
                    punkte.Add(new WartungsPunkt(
                        "Verschleiß", geraet.Name, "kritisch",
                        $"{geraet.Name}: seit {tageSeitEinbau} Tagen im Einsatz, vorgesehen sind {lebensdauer}. Tausch fällig.",
                        "Lebensdauer aus deinem Geräte-Eintrag, gerechnet ab Einbaudatum."));
                }
                else if (tageSeitEinbau >= lebensdauer * VorwarnAnteil)
                {
                    var rest = lebensdauer - tageSeitEinbau;
                    punkte.Add(new WartungsPunkt(
                        "Verschleiß", geraet.Name, "warnung",
                        $"{geraet.Name}: noch {rest} von {lebensdauer} Tagen. Ersatz jetzt bestellen, dann liegt er da, wenn er gebraucht wird.",
                        "Lebensdauer aus deinem Geräte-Eintrag; Vorwarnung bei 90 %."));
                }
            }
        }

        foreach (var frist in fristen)
        {
            if (frist.Quelle is WartungsFristQuelle.Geplant or WartungsFristQuelle.Keine) continue;
            if (frist.FaelligUtc is not { } faellig || frist.IntervallTage is not { } intervall) continue;
            if (nowUtc.Date < faellig.Date) continue;

            var ueberfaellig = (int)(nowUtc.Date - faellig.Date).TotalDays;
            var seit = frist.BezugUtc is { } bezug ? (int)(nowUtc.Date - bezug.Date).TotalDays : 0;
            var pruefen = frist.Art == "wartung";
            var meldung = (frist.Quelle, pruefen) switch
            {
                (WartungsFristQuelle.Einbau, true) => $"{frist.Geraet}: seit dem Einbau vor {seit} Tagen nie geprüft (Plan: alle {intervall}).",
                (WartungsFristQuelle.Einbau, false) => $"{frist.Geraet}: seit dem Einbau vor {seit} Tagen nie kalibriert (Plan: alle {intervall}).",
                (_, true) => $"{frist.Geraet}: zuletzt vor {seit} Tagen geprüft (Plan: alle {intervall}).",
                (_, false) => $"{frist.Geraet}: zuletzt vor {seit} Tagen kalibriert (Plan: alle {intervall}).",
            };
            var herkunft = (frist.Quelle, pruefen) switch
            {
                (WartungsFristQuelle.Einbau, true) => "Prüfintervall aus deinem Geräte-Eintrag; ohne Prüfeintrag zählt das Einbaudatum.",
                (WartungsFristQuelle.Einbau, false) => "Kalibrierintervall aus deinem Geräte-Eintrag; ohne Kalibrierung zählt das Einbaudatum.",
                (WartungsFristQuelle.Folgetermin, true) => "Folgetermin aus der letzten abgeschlossenen Wartung.",
                (WartungsFristQuelle.Folgetermin, false) => "Folgetermin aus der letzten Kalibrierung.",
                (_, true) => "Prüfintervall aus deinem Geräte-Eintrag, gerechnet ab der letzten abgeschlossenen Wartung.",
                (_, false) => "Kalibrierintervall aus deinem Geräte-Eintrag, gerechnet ab der letzten Kalibrierung.",
            };
            punkte.Add(new WartungsPunkt(
                pruefen ? "Prüfung" : "Kalibrierung", frist.Geraet,
                ueberfaellig >= intervall ? "kritisch" : "warnung",
                meldung, herkunft));
        }

        // Die Sicherung zum Schluss: sie betrifft kein Geraet, sondern alles.
        if (letzteSicherung is { } sicherung)
        {
            var alter = (int)(nowUtc.Date - sicherung.Date).TotalDays;
            if (alter >= SicherungAlterTage)
            {
                punkte.Add(new WartungsPunkt(
                    "Sicherung", "Datensicherung", alter >= SicherungAlterTage * 3 ? "kritisch" : "warnung",
                    $"Letzte Sicherung vor {alter} Tagen. Alles seither — Messungen, Journal, Ernten — hängt an einer SD-Karte.",
                    $"Faustregel: nach {SicherungAlterTage} Tagen erinnern. Unter Einstellungen sicherst du in einem Klick."));
            }
        }
        else
        {
            punkte.Add(new WartungsPunkt(
                "Sicherung", "Datensicherung", "kritisch",
                "Es liegt noch keine Sicherung vor. Geht die Karte kaputt, ist alles weg — jede Messung, jedes Journal, jede Ernte.",
                "Keine Datei unter App_Data/backups gefunden."));
        }

        return punkte
            .OrderByDescending(p => p.Stufe == "kritisch")
            .ThenBy(p => p.Bereich)
            .ToList();
    }
}
