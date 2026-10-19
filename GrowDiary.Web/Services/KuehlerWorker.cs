using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Schaltet den Wasserkühler über eine smarte Steckdose — im Minutentakt.
///
/// <para><b>Warum ein eigener Takt und nicht die Lichtflanke.</b> Die
/// Nachtabsenkung hängt an der Flanke und schreibt deshalb zweimal am Tag einen
/// Sollwert. Für einen Regler ist das nutzlos: die Wassertemperatur wandert
/// zwischen den Flanken, und genau dann muss geschaltet werden. Eine Minute ist
/// dicht genug für ein 100-Liter-Becken und weit genug, dass die Sperren des
/// Kompressors überhaupt greifen können.</para>
///
/// <para><b>Was hier NICHT entschieden wird.</b> Nichts. Die ganze Regelung
/// steht in <see cref="KuehlerService.Entscheiden"/> — rein, ohne Datenbank und
/// ohne Home Assistant, und dort geprüft. Dieser Worker holt die Lage zusammen,
/// führt aus und schreibt ins Protokoll.</para>
///
/// <para><b>Der Fehlerfall.</b> Stirbt das Add-on mit eingeschalteter
/// Steckdose, kühlt der Kühler auf seinen EIGENEN Thermostat herunter und
/// stoppt dort. Deshalb muss der auf eine Untergrenze gestellt sein — das ist
/// die Bedingung, unter der diese Steuerung überhaupt verantwortbar ist, und
/// sie steht so auch in der Oberfläche. Im Kopf von
/// <see cref="NachtabsenkungWriter"/> steht die Gegenposition („Cultivate Control taktet
/// keinen Chiller"); sie galt, solange es diese Notabschaltung nicht gab.</para>
/// </summary>
public sealed class KuehlerWorker : BackgroundService
{
    private static readonly TimeSpan Takt = TimeSpan.FromMinutes(1);

    /// <summary>Schlüssel, unter dem der letzte Schaltzeitpunkt je Zelt liegt.</summary>
    /// <remarks>
    /// <b>In der Datenbank, nicht im Speicher.</b> Ein Feld im Objekt wäre nach
    /// jedem Add-on-Update auf null — und dann taktet der Kompressor genau
    /// dann, wenn jemand ein Update einspielt. Dasselbe Muster wie
    /// <c>MinIntervalMinutes</c> bei der Dosierung, das aus
    /// <c>DoseEvent.OccurredAtUtc</c> rechnet.
    /// </remarks>
    public const string SchaltzeitKey = "chiller-last-switch";

    /// <summary>Der Ereignistyp im Anlagen-Protokoll.</summary>
    /// <remarks>
    /// Eigener Typ, nicht „night-ramp": sonst vermischen sich das Schreiben
    /// eines Sollwerts und das Schalten einer Steckdose in derselben Liste.
    /// </remarks>
    public const string ProtokollTyp = "chiller-control";

    /// <summary>Schluessel, unter dem der zuletzt GESENDETE Befehl je Zelt liegt.</summary>
    /// <remarks>
    /// <b>Wozu, wenn es die Schaltzeit schon gibt.</b> Der Anlagen-Waechter
    /// meldet einen stehenden Kuehler als Ausfall. Solange dieser Regler ihn
    /// besitzt, ist „aus" aber der Normalfall und kein Ausfall — und zwar
    /// beliebig lange, nicht nur ein paar Minuten nach dem Schalten. Eine
    /// erste Fassung gab dem Waechter dafuer ein Zeitfenster von 20 Minuten;
    /// ab Minute 21 kam die kritische Meldung samt Push doch. Eine kuehle
    /// Nacht ist genau dieser Fall.
    ///
    /// Mit dem letzten Befehl wird die Frage richtig gestellt: <b>aus, weil
    /// ich es wollte</b> ist in Ordnung — <b>ich habe EIN befohlen und die
    /// Steckdose meldet aus</b> ist der echte Ausfall.
    /// </remarks>
    public const string BefehlKey = "chiller-last-command";

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<KuehlerWorker> _logger;

    public KuehlerWorker(IServiceProvider serviceProvider, ILogger<KuehlerWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Versetzt zu den anderen Wächtern, damit sie nicht im Gleichschritt
        // auf Home Assistant einschlagen.
        try { await Task.Delay(TimeSpan.FromSeconds(50), stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EinmalAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Kühler-Durchlauf fehlgeschlagen.");
            }

            try { await Task.Delay(Takt, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task EinmalAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dienste = scope.ServiceProvider;

        var zelte = dienste.GetRequiredService<GrowRepository>().GetTents();
        foreach (var zelt in zelte)
        {
            if (!zelt.ChillerControlEnabled || string.IsNullOrWhiteSpace(zelt.ChillerSwitchEntityId))
            {
                continue;
            }

            await FuerZeltAsync(dienste, zelt, cancellationToken);
        }
    }

    private async Task FuerZeltAsync(IServiceProvider dienste, Tent zelt, CancellationToken cancellationToken)
    {
        var einstellungen = dienste.GetRequiredService<HomeAssistantSettingsRepository>()
            .GetEffectiveHomeAssistantSettings();
        if (!einstellungen.IsConfigured) return;

        var ha = dienste.GetRequiredService<HomeAssistantService>();
        var zustaende = await ha.GetStatesAsync(einstellungen, zelt, cancellationToken);

        // Der Zustand der Steckdose kommt NICHT aus `zustaende`: dessen
        // Schluessel sind Metrik-Kennungen, nie Entitaets-Kennungen.
        var steckdose = await ha.GetEntityStateAsync(
            einstellungen, zelt.ChillerSwitchEntityId!, cancellationToken);

        var lage = LageLesen(
            dienste.GetRequiredService<GrowRepository>(),
            dienste.GetRequiredService<NachtabsenkungWriter>(),
            dienste.GetRequiredService<AppSettingsRepository>(),
            zelt, zustaende, steckdose);
        var urteil = KuehlerService.Entscheiden(lage, zelt, DateTime.UtcNow);

        if (urteil.Schaltung == KuehlerSchaltung.Nichts)
        {
            // Bewusst still: eine Zeile je Minute je Zelt waere ein Protokoll,
            // das niemand liest. Der Grund steht in der Oberflaeche, dort ist
            // er nuetzlich.
            return;
        }

        var an = urteil.Schaltung == KuehlerSchaltung.Ein;
        var ok = await ha.CallEntityServiceAsync(
            einstellungen, "switch", an ? "turn_on" : "turn_off",
            zelt.ChillerSwitchEntityId!, cancellationToken);

        if (ok)
        {
            // Erst NACH dem erfolgreichen Schalten merken. Andersherum stuende
            // eine Sperre, obwohl nichts passiert ist.
            var ablage = dienste.GetRequiredService<AppSettingsRepository>();
            ablage.SetValue($"{SchaltzeitKey}:{zelt.Id}", DateTime.UtcNow.ToString("O"));
            ablage.SetValue($"{BefehlKey}:{zelt.Id}", an ? "on" : "off");
        }

        dienste.GetRequiredService<SystemAuditRepository>().Add(new SystemAuditEvent
        {
            EventType = ProtokollTyp,
            Action = ok ? (an ? "switched-on" : "switched-off") : "switch-failed",
            Summary = urteil.Grund,
            Severity = ok ? "info" : "warning",
            Success = ok,
        });

        _logger.LogInformation("Kühler Zelt {Zelt}: {Was} — {Grund}",
            zelt.Id, an ? "an" : "aus", urteil.Grund);
    }

    /// <summary>Die Lage aus Profil, Messwert und Schaltzustand zusammensuchen.</summary>
    /// <remarks>
    /// <b>Öffentlich, weil die Live-Seite dieselbe Lage braucht.</b> Sie zeigt,
    /// was der Regler gerade tut und warum — und müsste die Lage sonst ein
    /// zweites Mal zusammensuchen. Zwei Fassungen derselben Rechnung laufen
    /// auseinander; das ist in diesem Projekt für das EC-Ziel und die
    /// physikalischen Grenzen belegt.
    /// </remarks>
    /// <param name="steckdose">
    /// Der Zustand der Kuehler-Steckdose, EINZELN geholt. Nicht aus
    /// <paramref name="zustaende"/>: dessen Schluessel sind Metrik-Kennungen
    /// (<c>chiller</c>, <c>reservoir-temp</c>), nie Entitaets-Kennungen.
    /// </param>
    public static KuehlerLage LageLesen(
        GrowRepository grows,
        NachtabsenkungWriter writer,
        AppSettingsRepository einstellungen,
        Tent zelt,
        IReadOnlyDictionary<string, HomeAssistantState> zustaende,
        HomeAssistantState? steckdose)
    {
        // Licht: derselbe Schluessel, an dem auch die Nachtabsenkung haengt.
        var lichtSchluessel = TentSensorMetricKeyMap.Resolve(SensorMetricType.LightStatus);
        var lichtAn = zustaende.TryGetValue(lichtSchluessel, out var licht)
            && LightStateNormalizer.Normalize(licht.State) == LightState.On;

        // Der Sollwert kommt aus demselben Plan, den die Nachtabsenkung schreibt.
        var grow = grows.GetActiveGrowsForTent(zelt.Id).FirstOrDefault(g => g.NightRampEnabled);
        double? soll = null;
        if (grow is not null)
        {
            var plan = writer.PlanFuer(grow, DateTime.Now);
            soll = KuehlerService.SollJetzt(plan, lichtAn);
        }

        // Ist-Temperatur mit Alter — das Alter entscheidet mit.
        var tempSchluessel = TentSensorMetricKeyMap.Resolve(SensorMetricType.ReservoirWaterTemp);
        double? ist = null;
        TimeSpan? alter = null;
        if (zustaende.TryGetValue(tempSchluessel, out var temp) && temp.NumericValue is { } zahl)
        {
            ist = zahl;
            // LastUpdated, nicht LastChanged: Letzteres steht still, solange
            // derselbe Wert gemeldet wird — also gerade dann, wenn die
            // Regelung ihr Ziel getroffen hat. Fehlt beides, bleibt das Alter
            // unbekannt, und der Regler schaltet bewusst nicht.
            var frisch = temp.LastUpdated ?? temp.LastChanged;
            alter = frisch is { } zeitpunkt ? DateTime.UtcNow - zeitpunkt.ToUniversalTime() : null;
        }

        // Der Zustand der Steckdose selbst — NICHT der Chiller-Sensor. Der
        // sagt, ob das Geraet laeuft; hier zaehlt, ob Strom anliegt.
        bool? laeuft = null;
        if (steckdose is not null)
        {
            laeuft = !PumpWatchService.IstAus(steckdose.State);
        }
        else if (zustaende.TryGetValue(TentSensorMetricKeyMap.Resolve(SensorMetricType.Chiller), out var chiller))
        {
            // Rueckfall: antwortet die Steckdose nicht, sagt der Chiller-Sensor
            // wenigstens, ob das GERAET laeuft. Schlechter, weil dort auch
            // „an, aber ohne Strom" wie „aus" aussieht — aber besser als gar
            // keine Regelung.
            laeuft = !PumpWatchService.IstAus(chiller.State);
        }

        var letzte = LetzteSchaltung(einstellungen, zelt.Id);

        return new KuehlerLage(soll, ist, alter, laeuft, letzte, lichtAn);
    }

    /// <summary>
    /// Was zuletzt BEFOHLEN wurde: <c>true</c> = ein, <c>false</c> = aus,
    /// <c>null</c> = dieser Regler hat fuer dieses Zelt noch nie geschaltet.
    /// </summary>
    public static bool? LetzterBefehl(AppSettingsRepository einstellungen, int zeltId)
        => einstellungen.GetValue($"{BefehlKey}:{zeltId}") switch
        {
            "on" => true,
            "off" => false,
            _ => null,
        };

    /// <summary>Wann zuletzt geschaltet wurde — aus der Datenbank.</summary>
    public static DateTime? LetzteSchaltung(AppSettingsRepository einstellungen, int zeltId)
    {
        var roh = einstellungen.GetValue($"{SchaltzeitKey}:{zeltId}");
        return DateTime.TryParse(roh, null, System.Globalization.DateTimeStyles.RoundtripKind, out var wert)
            ? wert.ToUniversalTime()
            : null;
    }
}
