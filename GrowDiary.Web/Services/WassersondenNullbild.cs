using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Erkennt das „Nullbild" einer Wassersonde: pH und EC melden im selben
/// Augenblick beide genau 0 — ein Platzhalter der Integration, kein Messwert.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (03.10.2026, Anlage des Nutzers).</b> In der Nacht vom
/// 02. auf den 03.10. stand im Rohwert-Verlauf für pH, EC und Wassertemperatur
/// an derselben Stelle der Wert 0, ringsum pH 6,2, EC 1,72 und 17,8 °C. Die
/// Vermutung war, ein nicht verfügbarer Sensor sei als 0 gespeichert worden.
/// Das stimmt nicht: <c>unavailable</c>, <c>unknown</c> und ein leerer Zustand
/// lassen sich nicht als Zahl lesen und werden schon heute nicht geschrieben
/// (<see cref="HomeAssistantService"/>, <c>double.TryParse</c>; der
/// Snapshot-Takt überspringt Zustände ohne Zahl). Home Assistant selbst hat
/// <b>„0"</b> aufgezeichnet — alle drei Entitäten der Bluelab-Sonde in
/// derselben Millisekunde (23:18:10,507), 71 Sekunden lang, danach wieder
/// normale Werte. Die Langzeitstatistik von Home Assistant zeigt ein
/// pH-Tagesminimum von 0 an 13 der letzten 31 Tage; Grow OS erwischt so einen
/// Ausfall, wenn sein 5-Minuten-Takt in das Fenster fällt.</para>
///
/// <para><b>Warum nicht „jede 0 verwerfen".</b> EC 0 ist ein echter Wert
/// (Osmosewasser, trockene Sonde), 0 °C Wasser ist möglich, PPFD 0 ist jede
/// Nacht, CO₂-Dosierung 0 jeden Tag. Eine Pauschalregel würde echte Werte
/// verschlucken.</para>
///
/// <para><b>Warum pH 0 zusammen mit EC 0 eindeutig ist.</b> pH 0 heisst eine
/// Wasserstoffionen-Aktivität von 1 mol/l — so eine Lösung leitet extrem gut
/// (1-molare Salzsäure liegt bei rund 330 mS/cm). EC 0 heisst: nahezu keine
/// Ionen. Beides zugleich kann keine Flüssigkeit sein; einer der beiden Werte
/// ist sicher falsch, und weil beide auf exakt 0 stehen, sind es beide. Die
/// Regel gilt unabhängig davon, ob pH und EC aus einem Gerät kommen — im
/// selben Tank widersprechen sie sich so oder so.</para>
///
/// <para><b>Was mitverworfen wird.</b> Eine Wassertemperatur, die im selben
/// Augenblick ebenfalls auf genau 0 steht. Allein wäre 0 °C möglich; neben
/// dem Nullbild ist sie Teil desselben Ausfalls (genau so beobachtet). Andere
/// Werte desselben Augenblicks bleiben unangetastet.</para>
///
/// <para><b>Was die Regel NICHT fängt.</b> Ein Zelt ohne EC-Sonde (pH 0 allein)
/// und ein Zelt ohne pH-Sonde (EC 0 und 0 °C): dort gibt es keinen Widerspruch,
/// der eine echte Messung ausschliesst. Diese Werte bleiben stehen.</para>
/// </remarks>
public static class WassersondenNullbild
{
    /// <summary>Kennung der pH-Messgröße (aus <see cref="TentSensorMetricKeyMap"/>).</summary>
    public static readonly string Ph = TentSensorMetricKeyMap.Resolve(SensorMetricType.ReservoirPh);

    /// <summary>Kennung der EC-Messgröße.</summary>
    public static readonly string Ec = TentSensorMetricKeyMap.Resolve(SensorMetricType.ReservoirEc);

    /// <summary>Kennung der Wassertemperatur.</summary>
    public static readonly string Wassertemperatur = TentSensorMetricKeyMap.Resolve(SensorMetricType.ReservoirWaterTemp);

    /// <summary>Die Größen, die ein Nullbild betrifft.</summary>
    public static IReadOnlyList<string> Groessen { get; } = [Ph, Ec, Wassertemperatur];

    /// <summary>
    /// Welche Werte dieses Augenblicks zum Nullbild gehören und verworfen werden.
    /// </summary>
    /// <param name="werte">Die Zahlen EINES Augenblicks, nach Messgrößen-Kennung.</param>
    /// <returns>Leer, wenn kein Nullbild vorliegt.</returns>
    public static IReadOnlyList<string> Verworfene(IReadOnlyDictionary<string, double?> werte)
    {
        if (!IstNull(werte, Ph) || !IstNull(werte, Ec)) return [];
        return Groessen.Where(groesse => IstNull(werte, groesse)).ToList();
    }

    // Genau 0, nicht „nahe 0": der Platzhalter ist exakt „0"; eine echte
    // Messung von EC 0,01 ist kein Nullbild.
    private static bool IstNull(IReadOnlyDictionary<string, double?> werte, string groesse)
        => werte.TryGetValue(groesse, out var wert) && wert == 0;

    /// <summary>
    /// Wendet die Regel auf frisch gelesene Home-Assistant-Zustände an: was zum
    /// Nullbild gehört, gilt danach als nicht verfügbar.
    /// </summary>
    /// <remarks>
    /// An der Quelle, damit Rohwerte, Kacheln, Alarme, Dosierung und Kühler
    /// dasselbe sehen — ein Nullbild ist für alle eine Lücke, nicht 0. Der
    /// Zustandstext wird auf <c>unavailable</c> gesetzt, damit kein Leser den
    /// Text „0" als Wert anzeigt. Die Ausfall-Meldung schlägt davon nicht an:
    /// sie braucht zwei Takte in Folge (<see cref="SensorOfflineTracker"/>).
    /// </remarks>
    /// <returns>Die verworfenen Messgrößen.</returns>
    public static IReadOnlyList<string> AufZustaendeAnwenden(IDictionary<string, HomeAssistantState> zustaende)
    {
        var werte = Groessen
            .Where(zustaende.ContainsKey)
            .ToDictionary(groesse => groesse, groesse => zustaende[groesse].NumericValue);
        var verworfen = Verworfene(werte);
        foreach (var groesse in verworfen)
        {
            zustaende[groesse].NumericValue = null;
            zustaende[groesse].State = "unavailable";
        }

        return verworfen;
    }

    /// <summary>Was die Bereinigung der schon gespeicherten Rohwerte getan hat.</summary>
    /// <param name="Entfernt">Gelöschte Rohwerte.</param>
    /// <param name="TageNeuBerechnet">Tageswerte, die ohne die Nullen neu berechnet wurden.</param>
    /// <param name="TageNichtNeuBerechnet">
    /// Betroffene Tageswerte, die nicht neu berechnet werden konnten, weil die
    /// Rohwerte des Tages nicht mehr vollständig vorliegen.
    /// </param>
    /// <param name="TageZurueckgestellt">
    /// Zelt-Tage, deren Rechnung scheiterte. Ihre Nullbilder bleiben in den
    /// Rohwerten stehen, damit der nächste Start es neu versuchen kann.
    /// </param>
    public sealed record Bereinigung(int Entfernt, int TageNeuBerechnet, int TageNichtNeuBerechnet, int TageZurueckgestellt = 0);

    /// <summary>Die Bereinigung beim Start der App, samt Protokoll.</summary>
    /// <remarks>
    /// <para><b>Eigener Gültigkeitsbereich.</b> <see cref="SensorReadingRepository"/>
    /// ist „scoped" registriert. Aus dem Wurzel-Container geholt, warf das in jeder
    /// Development-Umgebung (Scope-Prüfung an) — also im E2E-Backend des Tors und
    /// beim lokalen Start —, und die Nullbilder blieben stehen. Im Add-on
    /// (Production) lief es zufällig. Befund des Prüfers, 03.10.2026.</para>
    /// </remarks>
    /// <param name="dienste">Der Wurzel-Container der App.</param>
    /// <param name="protokoll">Wohin die Zeile geht.</param>
    /// <param name="heuteLokal">Der lokale Kalendertag; abgeschlossene Tage liegen davor.</param>
    public static Bereinigung BeimStart(IServiceProvider dienste, ILogger protokoll, DateOnly heuteLokal)
    {
        using var bereich = dienste.CreateScope();
        var ergebnis = GespeicherteEntfernen(bereich.ServiceProvider.GetRequiredService<SensorReadingRepository>(), heuteLokal);
        if (ergebnis.Entfernt > 0)
        {
            protokoll.LogInformation(
                "Wassersonde: {Entfernt} Rohwerte aus Nullbildern entfernt, {Neu} Tageswerte neu berechnet, {Nicht} nicht neu berechenbar (Rohwerte des Tages nicht mehr vollständig).",
                ergebnis.Entfernt, ergebnis.TageNeuBerechnet, ergebnis.TageNichtNeuBerechnet);
        }
        if (ergebnis.TageZurueckgestellt > 0)
        {
            protokoll.LogWarning(
                "Wassersonde: an {Tage} Zelt-Tagen ließ sich der Tageswert nicht rechnen — deren Nullbilder bleiben stehen, der nächste Start versucht es neu.",
                ergebnis.TageZurueckgestellt);
        }
        return ergebnis;
    }

    /// <summary>
    /// Entfernt gespeicherte Nullbilder aus den Rohwerten und berechnet die
    /// betroffenen Tageswerte neu.
    /// </summary>
    /// <remarks>
    /// <para><b>Dieselbe Regel wie beim Lesen</b> (<see cref="Verworfene"/>),
    /// angewandt je Zelt und Zeitpunkt: der Snapshot-Takt schreibt alle Werte
    /// einer Runde mit demselben Zeitstempel. Gelöscht wird nur, was die Regel
    /// eindeutig als Nullbild erkennt — eine einzelne EC 0 bleibt stehen.</para>
    /// <para><b>Tageswerte</b> werden nur für abgeschlossene Tage neu
    /// berechnet, für die es schon einen gibt, und nur, wenn die Rohwerte den
    /// Tag noch ganz abdecken. Nach sieben Tagen räumt der Takt die Rohwerte
    /// weg; ein halber Tag ergäbe ein falsches Tagesbild, und das alte bleibt
    /// dann besser stehen (gezählt in <see cref="Bereinigung.TageNichtNeuBerechnet"/>).</para>
    /// <para><b>Erst rechnen, dann schreiben (seit 03.10.2026).</b> Vorher wurden
    /// die Rohwerte zuerst gelöscht und danach gerechnet. Scheiterte die
    /// Rechnung, war die Grundlage weg, das Minimum 0 blieb im Tageswert, und
    /// ein zweiter Start fand nichts mehr — selten, aber endgültig. Jetzt
    /// entstehen die neuen Tageswerte aus den Rohwerten OHNE das Nullbild, bevor
    /// etwas gelöscht wird; Löschen und Schreiben laufen in einer Transaktion
    /// (<see cref="SensorReadingRepository.Bereinigen"/>). Scheitert die
    /// Rechnung für einen Tag, bleiben die Nullbilder dieses Zelt-Tags stehen
    /// — für ALLE Größen, denn ohne die EC 0 daneben erkennte der nächste Start
    /// die pH 0 nicht mehr als Nullbild.</para>
    /// <para>Läuft bei jedem Start; ohne Nullbild findet sie nichts und tut nichts.</para>
    /// </remarks>
    /// <param name="rohwerte">Die Rohwerte und Tageswerte.</param>
    /// <param name="heuteLokal">Der lokale Kalendertag; abgeschlossene Tage liegen davor.</param>
    /// <param name="rechnen">
    /// Die Tageswert-Rechnung; <c>null</c> heisst <see cref="Tageswert.Berechnen(IReadOnlyList{TentSensorReading}, int, string, DateOnly)"/>.
    /// Nur Prüfungen setzen sie, um eine scheiternde Rechnung zu stellen.
    /// </param>
    public static Bereinigung GespeicherteEntfernen(
        SensorReadingRepository rohwerte,
        DateOnly heuteLokal,
        Func<IReadOnlyList<TentSensorReading>, int, string, DateOnly, TentSensorDailyStat?>? rechnen = null)
    {
        rechnen ??= Tageswert.Berechnen;

        var verworfen = rohwerte.GetReadingsWithValue(Groessen, 0)
            .GroupBy(reading => (reading.TentId, reading.CapturedAtUtc))
            .SelectMany(augenblick =>
            {
                var werte = augenblick
                    .GroupBy(reading => reading.MetricKey)
                    .ToDictionary(gruppe => gruppe.Key, gruppe => (double?)gruppe.First().Value);
                var groessen = Verworfene(werte);
                return augenblick.Where(reading => groessen.Contains(reading.MetricKey));
            })
            .ToList();

        if (verworfen.Count == 0) return new Bereinigung(0, 0, 0);

        var verworfeneIds = verworfen.Select(reading => reading.Id).ToHashSet();
        var tageswerte = new List<TentSensorDailyStat>();
        var zurueckgestellt = new HashSet<(int Zelt, DateOnly Tag)>();
        var nichtBerechenbar = new List<(int Zelt, DateOnly Tag)>();
        var betroffen = verworfen
            .Select(reading => (reading.TentId, reading.MetricKey, Tag: OrtsTag(reading)))
            .Distinct()
            .Where(tag => tag.Tag < heuteLokal);
        foreach (var (zelt, groesse, tag) in betroffen)
        {
            if (rohwerte.GetDailyStats(zelt, groesse, tag, tag).Count == 0) continue;

            try
            {
                var tagesbeginnUtc = tag.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
                var aeltester = rohwerte.GetOldestReadingUtc(zelt, groesse);
                // Eine Takt-Länge Spielraum: der erste Wert nach Mitternacht kommt
                // bis zu fünf Minuten danach.
                if (aeltester is not { } erster || erster > tagesbeginnUtc.AddMinutes(10))
                {
                    nichtBerechenbar.Add((zelt, tag));
                    continue;
                }

                var ohneNullbild = rohwerte.GetReadingsForDay(zelt, groesse, tag)
                    .Where(reading => !verworfeneIds.Contains(reading.Id))
                    .ToList();
                if (rechnen(ohneNullbild, zelt, groesse, tag) is not { } stat)
                {
                    nichtBerechenbar.Add((zelt, tag));
                    continue;
                }

                tageswerte.Add(stat);
            }
            catch (Exception)
            {
                // Ein Tag, der sich nicht rechnen lässt, hält die übrigen nicht
                // auf — und verliert seine Grundlage nicht.
                zurueckgestellt.Add((zelt, tag));
            }
        }

        var loeschen = verworfen.Where(reading => !zurueckgestellt.Contains((reading.TentId, OrtsTag(reading)))).ToList();
        var schreiben = tageswerte.Where(stat => !zurueckgestellt.Contains((stat.TentId, stat.Date))).ToList();
        rohwerte.Bereinigen(loeschen.Select(reading => reading.Id), schreiben);

        // Ein zurückgestellter Zelt-Tag zählt nur dort — er wird beim nächsten
        // Start neu versucht, „nicht berechenbar" wäre für ihn falsch.
        var nicht = nichtBerechenbar.Count(eintrag => !zurueckgestellt.Contains(eintrag));
        return new Bereinigung(loeschen.Count, schreiben.Count, nicht, zurueckgestellt.Count);
    }

    /// <summary>Der lokale Kalendertag eines Rohwerts — die Spalte ist UTC.</summary>
    private static DateOnly OrtsTag(TentSensorReading reading)
        => DateOnly.FromDateTime(reading.CapturedAtUtc.ToLocalTime());
}
