using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>Wo der Assistent gerade steht.</summary>
public enum CalibrationStep
{
    /// <summary>Wartet darauf, dass der Stand im leeren System zur Ruhe kommt.</summary>
    WaitingForEmpty,
    /// <summary>Nullpunkt steht — jetzt füllt der Nutzer.</summary>
    Filling,
    /// <summary>Der Stand steht still. Ist es voll, oder war es nur eine Pause?</summary>
    AwaitingConfirmation,
    /// <summary>Fertig, die Gerade ist gespeichert.</summary>
    Done,
    /// <summary>Der Sensor liefert nichts.</summary>
    NoSensor,
}

/// <summary>Was die Oberfläche gerade anzeigt.</summary>
public sealed record CalibrationState(
    int SystemId,
    CalibrationStep Step,
    double? CurrentRaw,
    double? EmptyRaw,
    double? StableRaw,
    int SecondsSteady,
    int SecondsNeeded,
    int SampleCount,
    string Message);

/// <summary>
/// Der geführte Kalibrierlauf: Sensor mitlesen, während der Nutzer füllt.
/// </summary>
/// <remarks>
/// <para>Der Ablauf kommt aus der Praxis und nicht aus dem Formular: Wer sein
/// System kalibrieren will, steht mit dem Schlauch daneben und liest an der
/// Wasseruhr ab. Er soll nicht Zahlen abtippen, sondern füllen — Cultivate Control schaut
/// dabei zu.</para>
///
/// <list type="number">
/// <item>System ist leer, Assistent starten. Cultivate Control liest sofort mit und nimmt
/// den Stand, der 15 s ruhig ist, als Nullpunkt. <b>Nicht</b> „den ersten Wert,
/// den wir bekommen": wer schon gießt, bevor er klickt, hätte sonst einen
/// Nullpunkt mitten im Füllen.</item>
/// <item>Füllen. Der Wert steigt, die Anzeige zeigt ihn live.</item>
/// <item>Steht der Wert 60 s im Band, meldet Cultivate Control „voll?" — <b>und fragt</b>.
/// Eine Füllpause sieht für den Sensor genauso aus wie „fertig".</item>
/// <item>Der Nutzer bestätigt und trägt die Liter von der Wasseruhr ein.</item>
/// </list>
///
/// <para>Kalibriert wird mit <b>laufender Umwälzung</b> — gemessen wird später
/// auch so. Im stehenden Wasser kalibriert wäre jeder spätere Wert um den
/// Pumpen-Versatz daneben.</para>
///
/// <para>Die Sitzung lebt im Speicher: ein Neustart mittendrin bricht sie ab,
/// und das ist richtig — danach steht das Becken anders da als vorher.</para>
/// </remarks>
public sealed class LevelCalibrationService
{
    /// <summary>Ablesungen älter als das interessieren nicht mehr.</summary>
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(3);

    /// <summary>Nach so langer Untätigkeit gilt die Sitzung als vergessen.</summary>
    private static readonly TimeSpan Abandoned = TimeSpan.FromMinutes(30);

    private sealed class Session
    {
        public List<LevelSample> Samples { get; } = [];
        public double? EmptyRaw { get; set; }
        public DateTime LastTouchedUtc { get; set; } = DateTime.UtcNow;
    }

    private readonly Dictionary<int, Session> _sessions = [];
    private readonly object _lock = new();

    private readonly HydroSetupRepository _hydroSetups;
    private readonly GrowRepository _grows;
    private readonly HomeAssistantService _homeAssistant;
    private readonly ILogger<LevelCalibrationService> _logger;

    public LevelCalibrationService(
        HydroSetupRepository hydroSetups,
        GrowRepository grows,
        HomeAssistantService homeAssistant,
        ILogger<LevelCalibrationService> logger)
    {
        _hydroSetups = hydroSetups;
        _grows = grows;
        _homeAssistant = homeAssistant;
        _logger = logger;
    }

    public void Start(int systemId)
    {
        lock (_lock)
        {
            _sessions[systemId] = new Session();
        }
    }

    public void Cancel(int systemId)
    {
        lock (_lock)
        {
            _sessions.Remove(systemId);
        }
    }

    /// <summary>
    /// Einmal ablesen und sagen, wo der Assistent steht.
    /// </summary>
    /// <remarks>
    /// Die Oberfläche ruft das im Sekundentakt. Jeder Aufruf liest den Sensor
    /// frisch — mit Absicht: die Messwert-Historie wird nur alle fünf Minuten
    /// geschrieben, und darauf zu warten hiesse, den Füllvorgang zu verpassen.
    /// </remarks>
    public async Task<CalibrationState> PollAsync(int systemId, CancellationToken cancellationToken = default)
    {
        var system = _hydroSetups.GetHydroSetup(systemId);
        if (system?.TentId is not { } tentId)
        {
            return new CalibrationState(systemId, CalibrationStep.NoSensor, null, null, null, 0, 0, 0,
                "Dieses System hängt an keinem Zelt — ohne Zelt gibt es keinen Sensor.");
        }

        var roh = await ReadRawAsync(tentId, cancellationToken);
        var nowUtc = DateTime.UtcNow;

        lock (_lock)
        {
            AufraeumenAbgelaufene(nowUtc);

            if (!_sessions.TryGetValue(systemId, out var session))
            {
                return new CalibrationState(systemId, CalibrationStep.NoSensor, roh, null, null, 0, 0, 0,
                    "Kein Kalibrierlauf offen.");
            }

            session.LastTouchedUtc = nowUtc;

            if (roh is not { } wert)
            {
                return new CalibrationState(systemId, CalibrationStep.NoSensor, null, session.EmptyRaw, null, 0, 0,
                    session.Samples.Count,
                    "Der Wasserstand-Sensor liefert gerade nichts. Ist er dem Zelt zugeordnet?");
            }

            session.Samples.Add(new LevelSample(nowUtc, wert));
            session.Samples.RemoveAll(sample => nowUtc - sample.AtUtc > Window);

            var ruhig = LevelStability.SecondsSteady(session.Samples, nowUtc);

            // Schritt 1: der Nullpunkt.
            if (session.EmptyRaw is null)
            {
                var stabil = LevelStability.StableValue(session.Samples, nowUtc, LevelStability.EmptySeconds);
                if (stabil is null)
                {
                    return new CalibrationState(systemId, CalibrationStep.WaitingForEmpty, wert, null, null,
                        ruhig, LevelStability.EmptySeconds, session.Samples.Count,
                        "System muss leer sein und der Wert ruhig. Umwälzpumpe an lassen — so wird später auch gemessen.");
                }

                session.EmptyRaw = stabil;
                session.Samples.Clear();
                return new CalibrationState(systemId, CalibrationStep.Filling, wert, stabil, null, 0,
                    LevelStability.FullSeconds, 0,
                    $"Nullpunkt steht bei {stabil:0.##}. Jetzt füllen und an der Wasseruhr mitzählen.");
            }

            // Schritt 2: füllen, bis der Wert wieder steht.
            var vollWert = LevelStability.StableValue(session.Samples, nowUtc, LevelStability.FullSeconds);
            if (vollWert is null)
            {
                return new CalibrationState(systemId, CalibrationStep.Filling, wert, session.EmptyRaw, null,
                    ruhig, LevelStability.FullSeconds, session.Samples.Count,
                    "Füllen läuft. Sobald der Stand eine Minute ruhig ist, melde ich mich.");
            }

            // Der Stand steht — aber ob das „voll" heisst, weiss nur der Mensch.
            if (vollWert <= session.EmptyRaw)
            {
                return new CalibrationState(systemId, CalibrationStep.Filling, wert, session.EmptyRaw, null,
                    ruhig, LevelStability.FullSeconds, session.Samples.Count,
                    "Der Stand liegt noch auf Höhe des Nullpunkts — es ist noch nichts drin.");
            }

            return new CalibrationState(systemId, CalibrationStep.AwaitingConfirmation, wert, session.EmptyRaw,
                vollWert, ruhig, LevelStability.FullSeconds, session.Samples.Count,
                $"Der Stand ist seit einer Minute ruhig bei {vollWert:0.##}. Ist das System voll?");
        }
    }

    /// <summary>
    /// Der Nutzer bestätigt „voll" und nennt die Liter von der Wasseruhr.
    /// </summary>
    public string? Finish(int systemId, double liters)
    {
        if (liters <= 0) return "Trag die Liter ein, die wirklich hineingegangen sind.";

        lock (_lock)
        {
            if (!_sessions.TryGetValue(systemId, out var session))
            {
                return "Kein Kalibrierlauf offen — bitte neu starten.";
            }

            /* Zwei verschiedene Lagen, zwei verschiedene Saetze.
               Bis zum 01.09.2026 stand hier eine Bedingung fuer beides, und wer
               mitten im Lauf zu frueh auf "voll" drueckte, las "bitte neu
               starten" — der schlechteste aller Rate: sein Lauf ist in Ordnung,
               er muss nur warten, bis der Nullpunkt steht. Wer wirklich neu
               startet, faengt das Fuellen von vorn an. */
            if (session.EmptyRaw is not { } leer)
            {
                return "Der Nullpunkt steht noch nicht — das leere System muss erst "
                       + "15 Sekunden ruhig sein. Umwaelzpumpe an lassen und kurz warten.";
            }

            var voll = LevelStability.StableValue(session.Samples, DateTime.UtcNow, LevelStability.FullSeconds);
            if (voll is not { } vollWert)
            {
                return "Der Stand ist gerade nicht ruhig — kurz warten und noch einmal bestätigen.";
            }

            if (vollWert <= leer)
            {
                return "Der Vollstand liegt nicht über dem Nullpunkt. Ist der richtige Sensor zugeordnet?";
            }

            var system = _hydroSetups.GetHydroSetup(systemId);
            if (system is null) return "Dieses Hydro-System existiert nicht mehr.";

            system.LevelSensorEmptyRaw = leer;
            system.LevelSensorFullRaw = vollWert;
            system.LevelSensorFullLiters = liters;
            system.LevelCalibratedAtUtc = DateTime.UtcNow;
            // Das gemessene Volumen ist die bessere Angabe als jede Schätzung.
            system.ReservoirLiters = liters;
            _hydroSetups.UpdateHydroSetup(system);

            _sessions.Remove(systemId);
            _logger.LogInformation(
                "Volumen kalibriert: System {SystemId}, {Empty:0.##} → {Full:0.##} entspricht {Liters:0.#} L.",
                systemId, leer, vollWert, liters);
            return null;
        }
    }

    /// <summary>Der aktuelle Rohwert des Pegelsensors — cm bevorzugt, sonst Liter.</summary>
    private async Task<double?> ReadRawAsync(int tentId, CancellationToken cancellationToken)
    {
        try
        {
            var settings = _grows.GetEffectiveHomeAssistantSettings();
            var tent = _grows.GetTent(tentId);
            if (!settings.IsConfigured || tent is null) return null;

            var states = await _homeAssistant.GetStatesAsync(settings, tent, cancellationToken);
            if (states.TryGetValue("reservoir-level-cm", out var cm)) return cm.NumericValue;
            if (states.TryGetValue("reservoir-level", out var liter)) return liter.NumericValue;
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Pegel für Zelt {TentId} nicht lesbar.", tentId);
            return null;
        }
    }

    /// <summary>Vergessene Sitzungen wegräumen — sonst wächst der Speicher still.</summary>
    private void AufraeumenAbgelaufene(DateTime nowUtc)
    {
        var alt = _sessions
            .Where(eintrag => nowUtc - eintrag.Value.LastTouchedUtc > Abandoned)
            .Select(eintrag => eintrag.Key)
            .ToList();

        foreach (var id in alt) _sessions.Remove(id);
    }
}
