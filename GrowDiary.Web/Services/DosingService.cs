using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>Was zum Zeitpunkt der Anfrage über das Zelt bekannt ist.</summary>
public sealed record DosingContext(
    /// <summary>Der Messwert, gegen den dosiert wird; null = keiner vorhanden.</summary>
    double? Reading,
    /// <summary>Wie alt dieser Messwert ist.</summary>
    TimeSpan? ReadingAge,
    /// <summary>Wann die zugehörige Sonde zuletzt kalibriert wurde; null = nie.</summary>
    DateTime? ProbeCalibratedAtUtc,
    /// <summary>Ob die Sonde nach ihrem eigenen Plan überfällig ist.</summary>
    bool ProbeCalibrationOverdue,
    /// <summary>Bereits gelaufene Dosen dieser Pumpe seit Mitternacht.</summary>
    IReadOnlyList<DoseEvent> DosesToday,
    /// <summary>Füllstand über dem Minimum? null = unbekannt, dann kein Hindernis.</summary>
    bool? WaterLevelOk,
    /// <summary>
    /// Die jüngste Dosis IRGENDEINER Pumpe dieses Zelts. Die Mischpause gehört
    /// dem Becken, nicht der Pumpe: nach jeder Dosis in dasselbe Wasser sagt
    /// der Messwert erst einmal nichts — egal, wer dosiert hat.
    /// </summary>
    DateTime? LastTentDoseUtc = null,
    /// <summary>
    /// Wartet im Zelt noch eine zweite Dünger-Hälfte (A gegeben, B steht aus)?
    /// Solange ja, dosiert hier NIEMAND — sonst korrigiert eine pH-Dosis einen
    /// Zustand, den B gleich wieder verschiebt.
    /// </summary>
    bool TentHasPendingDose = false,
    /// <summary>
    /// Läuft die Umwälzpumpe? null = unbekannt (kein Sensor gemappt oder Wert
    /// veraltet). In stehendes Wasser dosiert niemand: ohne Umwälzung verteilt
    /// sich nichts, ein Topf bekommt das Konzentrat ab.
    /// </summary>
    bool? CirculationOn = null);

/// <summary>Das Urteil vor einer Dosis.</summary>
public sealed record DosingDecision(bool Allowed, double Ml, double Seconds, string Reason)
{
    public static DosingDecision No(string reason) => new(false, 0, 0, reason);
}

/// <summary>
/// Rechnet Dosen aus und prüft, ob überhaupt dosiert werden darf.
/// </summary>
/// <remarks>
/// Beide Teile sind rein und ohne Datenbank prüfbar. Das ist hier kein
/// Selbstzweck: am Ende dieser Rechnung drückt eine Pumpe Säure in ein Becken
/// mit lebenden Pflanzen. Ein Vorzeichenfehler wäre nicht „ein falscher Wert
/// auf einer Kachel", sondern ein verlorener Lauf.
/// </remarks>
public static class DosingCalculator
{
    /// <summary>Wie lange die Pumpe für diese Menge laufen muss.</summary>
    public static double SecondsFor(double ml, double mlPerMinute)
        => mlPerMinute <= 0 ? 0 : Math.Round(ml / mlPerMinute * 60.0, 2);

    /// <summary>Wie viel bei dieser Laufzeit herauskommt — die Gegenrichtung, fürs Kalibrieren.</summary>
    public static double MlFor(double seconds, double mlPerMinute)
        => Math.Round(seconds / 60.0 * mlPerMinute, 3);

    /// <summary>
    /// Wie lange die Pumpe laufen muss, um ungefähr die Zielmenge auszugeben.
    /// </summary>
    /// <remarks>
    /// Nur eine Schätzung für den Kalibrierlauf — sie muss nicht stimmen. Was
    /// zählt, ist die Menge, die danach wirklich im Becher steht: daraus wird
    /// die Fördermenge gerechnet. Die Schätzung sorgt nur dafür, dass man in
    /// einem gut ablesbaren Bereich landet.
    /// </remarks>
    public static double? SecondsForTarget(double targetMl, double? mlPerMinute)
        => mlPerMinute is { } rate && rate > 0 && targetMl > 0
            ? Math.Round(targetMl / rate * 60.0, 1)
            : null;

    /// <summary>
    /// Fördermenge aus einem Kalibrierlauf: gemessene Milliliter auf die Minute
    /// hochgerechnet.
    /// </summary>
    public static double? MlPerMinuteFrom(double measuredMl, double seconds)
        => seconds <= 0 || measuredMl <= 0 ? null : Math.Round(measuredMl / seconds * 60.0, 2);

    /// <summary>
    /// Was aus dem Protokoll gelernt wurde: Änderung des Messwerts je Milliliter.
    /// </summary>
    /// <remarks>
    /// Aus der Konzentration allein lässt sich das nicht ausrechnen — wie stark
    /// eine Lösung gegen pH-Änderung gegenhält, hängt an Wasserhärte und Dünger.
    /// Also wird gemessen statt gerechnet: nur Dosen mit Wert davor UND danach
    /// zählen, und erst ab dreien überhaupt.
    /// </remarks>
    /// <param name="sinceUtc">
    /// Schneidet das Lernfenster — in der Regel am letzten Wasserwechsel.
    /// Frisches Wasser hat frische Puffer: der pH-Down-Bedarf ist direkt nach
    /// dem Wechsel hoeher und sinkt ueber die Standzeit. Dosen von davor
    /// beschreiben ein anderes Wasser und wuerden den Schnitt verwaessern.
    /// </param>
    public static double? LearnedChangePerMl(IEnumerable<DoseEvent> history, DateTime? sinceUtc = null)
    {
        var brauchbar = history
            .Where(dose => dose.Outcome == DoseOutcome.Done && dose.DosedMl > 0)
            // Testdosen fliegen raus: es ist nichts geflossen, jede Aenderung
            // danach hat eine andere Ursache. Sonst stuende hier spaeter eine
            // Zahl, hinter der nie ein Tropfen war.
            .Where(dose => !dose.Simulated)
            .Where(dose => sinceUtc is not { } schnitt || dose.OccurredAtUtc >= schnitt)
            .Where(dose => dose.ValueBefore is not null && dose.ValueAfter is not null)
            .Select(dose => (dose.ValueAfter!.Value - dose.ValueBefore!.Value) / dose.DosedMl)
            .ToList();

        return brauchbar.Count < 3 ? null : Math.Round(brauchbar.Average(), 4);
    }

    /// <summary>
    /// Skaliert eine Dosis auf den aktuellen Fuellstand.
    /// </summary>
    /// <remarks>
    /// Die gelernte Wirkung je ml stammt aus Dosen ins volle Becken. Ist das
    /// Becken nur halb voll, wirkt dieselbe Menge fast doppelt — die Dosis muss
    /// also mit dem Fuellstand schrumpfen (ml · aktuell/voll). Nach oben wird
    /// nie skaliert: ein uebervolles Becken macht eine Dosis nur schwaecher,
    /// und schwaecher ist die sichere Richtung. Unter 30 % wird nicht weiter
    /// verkleinert, sondern beim Faktor 0,3 gedeckelt — bei so wenig Wasser
    /// stimmt meist etwas anderes nicht.
    /// </remarks>
    public static double VolumeFactor(double? currentLiters, double? referenceLiters)
    {
        if (currentLiters is not { } aktuell || referenceLiters is not { } voll) return 1;
        if (aktuell <= 0 || voll <= 0) return 1;

        return Math.Clamp(aktuell / voll, 0.3, 1.0);
    }

    /// <summary>
    /// Die Menge für den Weg vom Ist- zum Zielwert.
    /// </summary>
    /// <remarks>
    /// Ohne Erfahrung gibt es keine Zahl — geraten wird nicht. Mit Erfahrung
    /// wird bewusst nur die halbe Strecke gegangen: nach unten ist ein pH
    /// schnell, zurück fast nicht. Lieber zweimal wenig als einmal zu viel.
    /// </remarks>
    public static double? MlToReach(double current, double target, double? changePerMl)
    {
        if (changePerMl is not { } proMl || Math.Abs(proMl) < 1e-9) return null;

        var strecke = target - current;
        // Wirkt die Pumpe in die falsche Richtung, ist hier nichts zu tun.
        if (Math.Sign(strecke) != Math.Sign(proMl)) return null;

        var voll = strecke / proMl;
        return voll <= 0 ? null : Math.Round(voll * 0.5, 2);
    }
}

/// <summary>Prüft die Anschläge — getrennt vom Rechnen, damit jeder Riegel einzeln belegt ist.</summary>
public static class DosingGuard
{
    /// <summary>Keine DOSIS ist je länger, egal was die Rechnung sagt.</summary>
    public const double AbsoluteMaxSeconds = 60;

    /// <summary>
    /// Der Kalibrierlauf darf länger — er geht in den Messbecher, nicht ins Becken.
    /// </summary>
    /// <remarks>
    /// Für die Genauigkeit ist das entscheidend. Wer 23 ml abliest, liest sich
    /// leicht um 1 ml — das sind 4 % Fehler, die in jeder späteren Dosis
    /// stecken. Bei 100 ml ist derselbe Ablesefehler 1 %. Mit der Dosis-Grenze
    /// von 60 s käme man bei 46 ml/min nie über 46 ml hinaus.
    /// </remarks>
    public const double MaxCalibrationSeconds = 300;

    public static DosingDecision Evaluate(DosingPump pump, double requestedMl, DosingContext context, DateTime nowUtc)
        => Pruefen(pump, requestedMl, context, nowUtc, zweiteHaelfte: false);

    /// <summary>
    /// Die Anschläge für die zweite Hälfte eines Zweikomponenten-Düngers (B).
    /// </summary>
    /// <remarks>
    /// <para><b>Der Anlass (01.10.2026).</b> B lief bis hierher an allen
    /// Anschlägen vorbei — keine Einzel-, keine Tagesgrenze. Begründet war das
    /// mit der Mischpause: sie hat gerade erst A gesehen und würde B genau
    /// deshalb ablehnen.</para>
    ///
    /// <para><b>Was jetzt gilt.</b> Zwei Riegel entfallen, weil sie B
    /// <i>dauerhaft</i> sperrten, obwohl B legitim ist:</para>
    /// <list type="bullet">
    /// <item>die <b>Mischpause</b> — A liegt per Konstruktion nur die Trennzeit
    /// zurück, sie wäre immer zu kurz;</item>
    /// <item>„<b>im Becken steht noch eine Hälfte aus</b>" — das ist B selbst.</item>
    /// </list>
    /// <para>Alles andere gilt wie für jede Dosis: Entität, Fördermenge, die
    /// stehende Umwälzung, die größte Einzeldosis, Tagesanzahl und Tagesmenge
    /// der Pumpe B. Was davon greift, löst sich von selbst (spätestens um
    /// Mitternacht) — B wartet dann, und solange wartet auch A
    /// (<see cref="DosingContext.TentHasPendingDose"/>). Lieber eine Weile
    /// keine Düngung als mehr B, als der Nutzer je erlaubt hat.</para>
    ///
    /// <para><b>Die harte Laufzeitgrenze wird vorab eingerechnet</b>, nicht
    /// erst geprüft: <see cref="Evaluate"/> lehnt über 60 s ab, und bei einer
    /// langsamen Pumpe wäre das für B jeden Takt dieselbe Ablehnung — eine
    /// ewig wartende Hälfte. Stattdessen geht ein Lauf bis zur Grenze, der
    /// Rest folgt im nächsten Takt.</para>
    /// </remarks>
    public static DosingDecision PruefeZweiteHaelfte(DosingPump partner, double requestedMl, DosingContext context, DateTime nowUtc)
    {
        if (partner.MlPerMinute is { } rate && rate > 0)
        {
            requestedMl = Math.Min(requestedMl, HoechstensJeLauf(rate));
        }

        return Pruefen(partner, requestedMl, context, nowUtc, zweiteHaelfte: true);
    }

    /// <summary>
    /// Wie viel eine Pumpe in <see cref="AbsoluteMaxSeconds"/> höchstens
    /// fördert — abgerundet auf 0,01 ml, damit die Rückrechnung in Sekunden
    /// nie über der Grenze landet.
    /// </summary>
    public static double HoechstensJeLauf(double mlPerMinute)
        => mlPerMinute <= 0 ? 0 : Math.Floor(mlPerMinute * AbsoluteMaxSeconds / 60.0 * 100) / 100;

    private static DosingDecision Pruefen(DosingPump pump, double requestedMl, DosingContext context, DateTime nowUtc, bool zweiteHaelfte)
    {
        // Im Testbetrieb wird nichts geschaltet — dann braucht es auch keine
        // Entität. Alles andere gilt unverändert, sonst prüfte der Test etwas
        // anderes als der Ernstfall.
        if (!pump.SimulationMode && string.IsNullOrWhiteSpace(pump.HaEntityId))
        {
            return DosingDecision.No("Keine Home-Assistant-Entität hinterlegt.");
        }

        // Wartet im Zelt noch eine zweite Dünger-Hälfte, dosiert hier niemand —
        // auch keine andere Pumpe. Sonst korrigiert pH einen Zustand, den B
        // gleich wieder verschiebt, oder A läuft doppelt, bevor B je kam.
        // Für B selbst gilt das nicht: die ausstehende Hälfte IST B.
        if (context.TentHasPendingDose && !zweiteHaelfte)
        {
            return DosingDecision.No("Im Becken steht noch eine zweite Dünger-Hälfte aus — erst wird die Düngung vollständig.");
        }

        // In stehendes Wasser dosiert niemand: ohne Umwälzung verteilt sich
        // nichts, ein Topf bekommt das Konzentrat ab und die Wurzeln darin den
        // Schaden. Unbekannt (kein Sensor) blockt von Hand nicht — wer selbst
        // drückt, steht daneben und hört die Pumpe.
        if (context.CirculationOn == false)
        {
            return DosingDecision.No("Die Umwälzpumpe steht — ohne Umwälzung bliebe die Dosis als Konzentrat an einer Stelle.");
        }

        if (pump.MlPerMinute is not { } mlPerMinute || mlPerMinute <= 0)
        {
            return DosingDecision.No("Pumpe ist nicht kalibriert — ohne Fördermenge sind Milliliter keine Laufzeit.");
        }

        if (requestedMl <= 0)
        {
            return DosingDecision.No("Nichts zu dosieren.");
        }

        // Deckeln statt ablehnen: die gewünschte Wirkung kommt dann eben in
        // zwei Schritten. Ablehnen hiesse, dass gar nichts passiert.
        var ml = Math.Min(requestedMl, pump.MaxSingleDoseMl);

        // Was auf Tagesgrenze und Mischzeit zaehlt: alles, was in die Loesung
        // gegangen sein KANN — auch eine Dosis, deren Einschalten nicht
        // bestaetigt wurde (DosingService.KannGelaufenSein). Kalibrierlaeufe
        // gehen in den Messbecher und aendern an der Loesung nichts — beim
        // ersten Durchspielen war deshalb nach dem Kalibrieren 18 Minuten lang
        // keine Dosis moeglich.
        var gelaufen = context.DosesToday
            .Where(dose => DosingService.KannGelaufenSein(dose) && dose.Trigger != DoseTrigger.Calibration)
            .ToList();
        if (gelaufen.Count >= pump.MaxDosesPerDay)
        {
            return DosingDecision.No($"Tagesgrenze erreicht: schon {gelaufen.Count} Dosierungen.");
        }

        var heuteMl = gelaufen.Sum(DosingService.HoechstensGegeben);
        if (heuteMl >= pump.MaxMlPerDay)
        {
            return DosingDecision.No($"Tagesmenge erreicht: schon {heuteMl:0.#} ml.");
        }

        // Nur so viel, wie bis zur Tagesmenge noch frei ist.
        ml = Math.Min(ml, pump.MaxMlPerDay - heuteMl);
        if (ml <= 0)
        {
            return DosingDecision.No($"Tagesmenge erreicht: schon {heuteMl:0.#} ml.");
        }

        // Die Mischpause gehört dem Becken, nicht der Pumpe: nach JEDER Dosis
        // in dasselbe Wasser sagt der Messwert erst einmal nichts — egal, wer
        // dosiert hat. Vorher zählte nur die eigene Historie, und eine Minute
        // nach der B-Hälfte hätte die pH-Pumpe in die Schliere gemessen.
        var letzteEigene = gelaufen.MaxBy(dose => dose.OccurredAtUtc)?.OccurredAtUtc;
        // Für B gilt sie nicht: A liegt per Konstruktion nur die Trennzeit
        // zurueck, die Pause waere immer zu kurz (PruefeZweiteHaelfte).
        var letzteImBecken = new[] { letzteEigene, context.LastTentDoseUtc }.Max();
        if (letzteImBecken is { } zuletzt && !zweiteHaelfte)
        {
            var seit = nowUtc - zuletzt;
            if (seit < TimeSpan.FromMinutes(pump.MinIntervalMinutes))
            {
                var rest = (int)Math.Ceiling((TimeSpan.FromMinutes(pump.MinIntervalMinutes) - seit).TotalMinutes);
                return DosingDecision.No($"Noch {rest} min mischen — erst danach sagt der Messwert etwas.");
            }
        }

        if (context.WaterLevelOk == false)
        {
            return DosingDecision.No("Wasserstand unter Minimum.");
        }

        var seconds = DosingCalculator.SecondsFor(ml, mlPerMinute);
        if (seconds <= 0)
        {
            return DosingDecision.No("Errechnete Laufzeit ist null.");
        }

        if (seconds > AbsoluteMaxSeconds)
        {
            return DosingDecision.No($"Laufzeit {seconds:0.#} s über der harten Grenze von {AbsoluteMaxSeconds:0} s.");
        }

        return new DosingDecision(true, Math.Round(ml, 2), seconds, "Freigegeben.");
    }

    /// <summary>
    /// Zusätzliche Riegel, die nur für die Automatik gelten. Von Hand darf man
    /// mehr — wer selbst drückt, steht daneben und sieht, was passiert.
    /// </summary>
    public static DosingDecision EvaluateAutomatic(DosingPump pump, double requestedMl, DosingContext context, DateTime nowUtc)
    {
        if (!pump.AutomationEnabled)
        {
            return DosingDecision.No("Automatik ist für diese Pumpe aus.");
        }

        // Ohne Abschaltung in Home Assistant läuft die Pumpe weiter, wenn Grow OS
        // zwischen Ein- und Ausschalten abstürzt. Von Hand ist das vertretbar —
        // jemand steht daneben. Unbeaufsichtigt nicht. Im Testbetrieb entfällt
        // die Forderung: es gibt nichts, das weiterlaufen könnte.
        if (!pump.SimulationMode && !pump.HasHomeAssistantAutoOff)
        {
            return DosingDecision.No("Ohne Abschaltung in Home Assistant bleibt die Automatik gesperrt.");
        }

        // Unbeaufsichtigt reicht „unbekannt" nicht: eine stehende Umwälzpumpe
        // ist oft genau der Grund, warum die Werte driften, die die Automatik
        // korrigieren will. Sie dosiert nur bei BESTÄTIGT laufender Umwälzung —
        // im Testbetrieb entfällt das, dort fliesst nichts.
        if (!pump.SimulationMode && context.CirculationOn != true)
        {
            return DosingDecision.No("Automatik dosiert nur bei bestätigt laufender Umwälzung — Umwälzpumpe in Home Assistant mappen.");
        }

        if (context.Reading is null)
        {
            return DosingDecision.No("Kein Messwert vorhanden.");
        }

        if (context.ReadingAge is not { } age || age > TimeSpan.FromMinutes(pump.MaxReadingAgeMinutes))
        {
            var alt = context.ReadingAge is { } a ? $"{(int)a.TotalMinutes} min alt" : "unbekannt alt";
            return DosingDecision.No($"Messwert ist {alt} — es wird nicht auf alte Werte dosiert.");
        }

        if (context.ProbeCalibratedAtUtc is null)
        {
            return DosingDecision.No("Sonde wurde nie kalibriert — eine driftende Sonde dosiert blind.");
        }

        if (context.ProbeCalibrationOverdue)
        {
            return DosingDecision.No("Sonden-Kalibrierung überfällig — erst kalibrieren, dann dosieren.");
        }

        return Evaluate(pump, requestedMl, context, nowUtc);
    }
}

/// <summary>Wie ein Pumpenlauf ausgegangen ist.</summary>
public enum Pumpenlauf
{
    /// <summary>Eingeschaltet, gelaufen, ausgeschaltet.</summary>
    Gelaufen,
    /// <summary>
    /// Nichts gesendet — Home Assistant war nicht konfiguriert oder nicht
    /// erreichbar. Sicher: es ist nichts geflossen.
    /// </summary>
    NichtGesendet,
    /// <summary>
    /// Gesendet, aber das Einschalten wurde nicht bestätigt. Die Pumpe kann
    /// kurz gelaufen sein; sie ist vorsorglich ausgeschaltet.
    /// </summary>
    Unsicher,
}

/// <summary>Schaltet die Pumpe tatsächlich — und schaltet sie garantiert wieder aus.</summary>
public sealed class DosingService
{
    private readonly GrowRepository _repository;
    private readonly DosingRepository _dosing;
    private readonly IAcFunk _funk;
    private readonly Ausschalter _ausschalter;
    private readonly ILogger<DosingService> _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _warten;

    public DosingService(
        GrowRepository repository,
        DosingRepository dosing,
        IAcFunk funk,
        Ausschalter ausschalter,
        ILogger<DosingService> logger,
        Func<TimeSpan, CancellationToken, Task>? warten = null)
    {
        _repository = repository;
        _dosing = dosing;
        _funk = funk;
        _ausschalter = ausschalter;
        _logger = logger;
        _warten = warten ?? ((dauer, token) => Task.Delay(dauer, token));
    }

    /// <summary>
    /// Zählt diese Dosis auf Tagesgrenze und Mischpause?
    /// </summary>
    /// <remarks>
    /// <para><c>Failed</c> zählt mit. „Home Assistant hat nicht geschaltet"
    /// heisst oft nur: die Antwort kam nicht binnen vier Sekunden — der Befehl
    /// kann trotzdem angekommen sein, und die Pumpe lief. Vorher zählte nur
    /// <c>Done</c>; die Automatik dosierte dann eine Minute später erneut, auf
    /// einen Messwert aus der noch nicht durchmischten Lösung.</para>
    /// <para>Im Zweifel also: gelaufen. Eine ausgefallene Dosis kostet eine
    /// Mischpause, eine doppelte kostet womöglich den Lauf.</para>
    /// </remarks>
    public static bool KannGelaufenSein(DoseEvent dose)
        => dose.Outcome is DoseOutcome.Done or DoseOutcome.Failed;

    /// <summary>Die Menge, die eine Dosis höchstens ins Becken gebracht hat.</summary>
    /// <remarks>Bei <c>Failed</c> die angeforderte — ob etwas floss, weiss niemand.</remarks>
    public static double HoechstensGegeben(DoseEvent dose)
        => dose.Outcome == DoseOutcome.Failed ? dose.RequestedMl : dose.DosedMl;

    /// <summary>Wie ein Pumpenlauf ins Protokoll kommt.</summary>
    /// <remarks>
    /// <para><c>NichtGesendet</c> wird <c>Rejected</c>: es ist sicher nichts
    /// geflossen, also zählt es weder auf Tagesgrenze noch Mischpause. Vorher
    /// (bis 29.09.2026) landete auch das als <c>Failed</c> — bei ausgefallenem
    /// Home Assistant sperrten sechs Versuche die Pumpe bis Mitternacht, auch
    /// von Hand.</para>
    /// <para><c>Unsicher</c> wird <c>Failed</c> und zählt (<see cref="KannGelaufenSein"/>).</para>
    /// </remarks>
    public static DoseOutcome Ausgang(Pumpenlauf lauf) => lauf switch
    {
        Pumpenlauf.Gelaufen => DoseOutcome.Done,
        Pumpenlauf.NichtGesendet => DoseOutcome.Rejected,
        _ => DoseOutcome.Failed,
    };

    /// <summary>Der Satz fürs Protokoll und die Oberfläche, wenn nicht gelaufen.</summary>
    public static string Grund(Pumpenlauf lauf) => lauf switch
    {
        Pumpenlauf.NichtGesendet => "Home Assistant nicht erreichbar — nichts gesendet, nichts geflossen.",
        Pumpenlauf.Unsicher => "Einschalten nicht bestätigt — die Pumpe kann kurz gelaufen sein und ist vorsorglich aus.",
        _ => string.Empty,
    };

    /// <summary>
    /// Lässt die Pumpe für die angegebene Zeit laufen.
    /// </summary>
    /// <remarks>
    /// <para>Das Ausschalten steht in einem <c>finally</c> und läuft auch dann,
    /// wenn der Aufruf abgebrochen wird — und auch dann, wenn schon das
    /// Einschalten scheiterte: ein Zeitlimit beim „an" heisst nicht, dass die
    /// Pumpe steht. Ausgeschaltet wird mit Nachkontrolle
    /// (<see cref="Ausschalter"/>).</para>
    /// <para>Was es NICHT übersteht, ist ein Absturz des ganzen Add-ons —
    /// dagegen hilft nur die Abschaltung in Home Assistant und der Auswurf beim
    /// Start (<see cref="TurnAllOffAsync"/>).</para>
    /// <para>Das Ergebnis unterscheidet, ob überhaupt gesendet wurde
    /// (<see cref="Pumpenlauf"/>, <see cref="Ausgang"/>): nur ein gesendetes,
    /// unbestätigtes Einschalten zählt auf Tagesgrenze und Mischpause.</para>
    /// </remarks>
    public async Task<Pumpenlauf> RunForSecondsAsync(DosingPump pump, double seconds, CancellationToken cancellationToken = default, double? maxSeconds = null)
    {
        var kappt = Math.Clamp(seconds, 0, maxSeconds ?? DosingGuard.AbsoluteMaxSeconds);
        if (kappt <= 0) return Pumpenlauf.NichtGesendet;

        // Testbetrieb: die Zeit vergeht wirklich, damit die Anzeige die echte
        // Dauer zeigt — geschaltet wird nichts. Ohne Home Assistant liesse sich
        // sonst kein einziger Schritt durchspielen.
        if (pump.SimulationMode)
        {
            _logger.LogInformation("Testbetrieb: Pumpe {Pump} laeuft {Seconds:0.#} s — es fliesst nichts.", pump.Name, kappt);
            await _warten(TimeSpan.FromSeconds(kappt), CancellationToken.None);
            return Pumpenlauf.Gelaufen;
        }

        var settings = _repository.GetEffectiveHomeAssistantSettings();
        if (!settings.IsConfigured) return Pumpenlauf.NichtGesendet;

        var (domain, _) = SplitEntity(pump.HaEntityId);

        // Erst nachsehen, ob Home Assistant antwortet. Kommt nichts zurück,
        // wird auch nichts gesendet — dann ist sicher, dass nichts floss, und
        // der Versuch sperrt nicht die Tagesgrenze.
        if (await _funk.ZustandAsync(settings, pump.HaEntityId, CancellationToken.None) is null)
        {
            _logger.LogWarning("Pumpe {Pump}: Home Assistant meldet keinen Zustand — nichts gesendet.", pump.Name);
            return Pumpenlauf.NichtGesendet;
        }

        var an = false;
        try
        {
            // Ohne den uebergebenen Token: ein Abbruch zwischen Senden und
            // Antwort liesse offen, ob die Pumpe laeuft.
            an = await _funk.SchickenAsync(
                settings, domain, "turn_on", pump.HaEntityId, new Dictionary<string, object>(), CancellationToken.None);
            if (!an)
            {
                // Kein Rueckweg ohne Ausschalten: das „an" kann angekommen
                // sein, nur die Antwort nicht.
                _logger.LogWarning("Pumpe {Pump}: Einschalten nicht bestätigt — wird vorsorglich ausgeschaltet.", pump.Name);
                return Pumpenlauf.Unsicher;
            }

            // Bewusst ohne den uebergebenen Token: bricht der Aufrufer ab, soll
            // trotzdem die volle Zeit gewartet und danach ausgeschaltet werden.
            // Ein Abbruch mitten im Lauf darf die Pumpe nicht laufen lassen.
            await _warten(TimeSpan.FromSeconds(kappt), CancellationToken.None);
        }
        finally
        {
            var aus = await _ausschalter.AusschaltenAsync(settings, pump.HaEntityId, _warten);
            if (!aus)
            {
                _logger.LogError(
                    "Pumpe {Pump} ({Entity}) liess sich NICHT ausschalten — in Home Assistant prüfen.",
                    pump.Name, pump.HaEntityId);
            }
        }

        return Pumpenlauf.Gelaufen;
    }

    /// <summary>Schaltet eine einzelne Pumpe aus — ohne Bedingungen.</summary>
    public async Task<bool> TurnOffAsync(DosingPump pump, CancellationToken cancellationToken = default)
    {
        // Eine Pumpe im Testbetrieb war nie an; „aus" ist trivial wahr.
        if (pump.SimulationMode) return true;

        var settings = _repository.GetEffectiveHomeAssistantSettings();
        if (!settings.IsConfigured || string.IsNullOrWhiteSpace(pump.HaEntityId)) return false;

        return await _ausschalter.AusschaltenAsync(settings, pump.HaEntityId, _warten);
    }

    /// <summary>Wie oft der Auswurf beim Start höchstens ansetzt.</summary>
    public const int AuswurfRunden = 10;

    /// <summary>Pause zwischen zwei Runden des Auswurfs.</summary>
    /// <remarks>
    /// Zehn Runden à 30 s decken fünf Minuten ab — so lange braucht Home
    /// Assistant nach einem gemeinsamen Neustart des Hosts im schlechten Fall,
    /// bis es Dienste annimmt.
    /// </remarks>
    public static readonly TimeSpan AuswurfPause = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Wirft beim Start jede eingerichtete Pumpe aus — bis sie „aus" meldet.
    /// </summary>
    /// <remarks>
    /// <para>Der Totmann: Ist Grow OS mitten in einer Dosis abgestürzt, läuft die
    /// Pumpe seither. Der erste Handgriff nach dem Hochfahren ist deshalb, alle
    /// abzuschalten — das kostet nichts, wenn ohnehin alles aus war.</para>
    /// <para>Vorher ein einziger Versuch ohne Blick aufs Ergebnis. Startet das
    /// Add-on zusammen mit Home Assistant, nimmt HA in den ersten Sekunden
    /// nichts an — der Totmann griff dann genau im Fall nicht, für den es ihn
    /// gibt. Jetzt: je Runde alle noch nicht bestätigten Pumpen, bis alle aus
    /// sind oder die Runden aufgebraucht.</para>
    /// </remarks>
    /// <returns>Die Pumpen, die sich nicht bestätigt ausschalten liessen.</returns>
    public async Task<IReadOnlyList<DosingPump>> TurnAllOffAsync(CancellationToken cancellationToken = default)
    {
        // Auch Pumpen im Testbetrieb: wer eine echte Pumpe nach einem Absturz
        // auf Testbetrieb stellt, soll den Totmann trotzdem behalten.
        var offen = _dosing.GetPumps()
            .Where(pump => !string.IsNullOrWhiteSpace(pump.HaEntityId))
            .ToList();

        for (var runde = 1; runde <= AuswurfRunden && offen.Count > 0; runde++)
        {
            if (runde > 1) await _warten(AuswurfPause, cancellationToken);

            // Jede Runde neu lesen: die Einstellungen koennen beim Start noch
            // fehlen und erst danach eingetragen werden.
            var settings = _repository.GetEffectiveHomeAssistantSettings();
            if (!settings.IsConfigured) continue;

            var nochOffen = new List<DosingPump>();
            foreach (var pump in offen)
            {
                if (!await _ausschalter.AusschaltenAsync(settings, pump.HaEntityId, _warten))
                {
                    nochOffen.Add(pump);
                }
            }
            offen = nochOffen;
        }

        foreach (var pump in offen)
        {
            _logger.LogError(
                "Pumpen-Auswurf beim Start: {Pump} ({Entity}) meldet nicht „aus\" — in Home Assistant prüfen.",
                pump.Name, pump.HaEntityId);
        }

        return offen;
    }

    /// <summary>„switch.dosier_ph_minus" → („switch", "dosier_ph_minus").</summary>
    public static (string Domain, string Name) SplitEntity(string entityId)
    {
        var index = entityId.IndexOf('.');
        return index <= 0
            ? ("switch", entityId)
            : (entityId[..index], entityId[(index + 1)..]);
    }
}
