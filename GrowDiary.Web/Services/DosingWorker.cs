using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using Microsoft.Extensions.DependencyInjection;

namespace GrowDiary.Web.Services;

/// <summary>
/// Der Takt hinter der Dosierung — zwei Aufgaben, eine Schleife.
/// </summary>
/// <remarks>
/// <para><b>Erstens: die Wirkung nachtragen.</b> Ohne das lernt keine Pumpe je
/// etwas, weil bei jeder Dosis nur der Wert davor festgehalten wurde. Das ist
/// die Voraussetzung für den Vorschlag — und für alles, was darauf aufbaut.</para>
///
/// <para><b>Zweitens: die Automatik.</b> Für Pumpen, bei denen sie eingeschaltet
/// ist, wird gerechnet, geprüft und gegebenenfalls dosiert. Die Anschläge dafür
/// stehen in <see cref="DosingGuard.EvaluateAutomatic"/> und sind schärfer als
/// von Hand: wer selbst drückt, steht daneben.</para>
///
/// <para>Jede unbeaufsichtigte Dosis geht als Nachricht raus. Eine Pumpe, die
/// von allein Säure ins Becken gedrückt hat, ist keine Randnotiz im
/// Protokoll — das gehört aufs Handy.</para>
/// </remarks>
public sealed class DosingWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DosingWorker> _logger;

    public DosingWorker(IServiceProvider serviceProvider, ILogger<DosingWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Versetzt zu den anderen Schleifen starten, damit nicht alle im selben
        // Moment auf Home Assistant losgehen.
        try { await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Dosier-Durchlauf fehlgeschlagen.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dosing = scope.ServiceProvider.GetRequiredService<DosingRepository>();
        var situations = scope.ServiceProvider.GetRequiredService<DosingContextBuilder>();
        var service = scope.ServiceProvider.GetRequiredService<DosingService>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
        var grows = scope.ServiceProvider.GetRequiredService<GrowRepository>();
        var homeAssistant = scope.ServiceProvider.GetRequiredService<HomeAssistantService>();

        var nowUtc = DateTime.UtcNow;

        // Live-Zustaende je Zelt einmal holen — daraus kommt die Umwaelzung.
        var statesByTent = new Dictionary<int, IReadOnlyDictionary<string, HomeAssistantState>?>();

        // Zuerst die ausstehenden zweiten Haelften: A steht schon im Becken, B
        // fehlt noch. Das hat Vorrang vor allem, was neu dazukaeme.
        await GivePendingAsync(dosing, service, situations, grows, homeAssistant, statesByTent, nowUtc, cancellationToken);

        /* Reihenfolge wie am echten Becken und eine Dosis je Zelt — beide Regeln
           stehen in Dosierreihenfolge, samt Begruendung und neun Pruefungen.
           Bis zum 02.09.2026 standen sie hier als Kommentar mitten im Takt und
           waren damit praktisch nicht pruefbar: 4,8 % Zeilen, 0 % Zweige. */
        var pumps = Dosierreihenfolge.Reihenfolge(dosing.GetPumps());
        var dosedTents = new HashSet<int>();

        foreach (var pump in pumps)
        {
            var liveStates = pump.AutomationEnabled && !pump.SimulationMode
                ? await StatesForTentAsync(statesByTent, grows, homeAssistant, pump.TentId, cancellationToken)
                : null;

            var situation = situations.Build(pump, nowUtc, liveStates);

            RecordEffects(dosing, pump, situation, nowUtc);

            if (Dosierreihenfolge.DarfDosieren(pump, dosedTents))
            {
                var dosed = await DoseIfNeededAsync(dosing, service, notifications, pump, situation, nowUtc, cancellationToken);
                if (dosed) dosedTents.Add(pump.TentId);
            }
        }
    }

    /// <summary>Live-Zustaende eines Zelts, einmal je Takt — nicht je Pumpe.</summary>
    private async Task<IReadOnlyDictionary<string, HomeAssistantState>?> StatesForTentAsync(
        Dictionary<int, IReadOnlyDictionary<string, HomeAssistantState>?> cache,
        GrowRepository grows,
        HomeAssistantService homeAssistant,
        int tentId,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(tentId, out var cached)) return cached;

        IReadOnlyDictionary<string, HomeAssistantState>? states = null;
        try
        {
            var settings = grows.GetEffectiveHomeAssistantSettings();
            var tent = grows.GetTent(tentId);
            if (settings.IsConfigured && tent is not null)
            {
                states = await homeAssistant.GetStatesAsync(settings, tent, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // Kein Zustand ist eine gueltige Antwort: die Umwaelzung bleibt dann
            // unbekannt, und die Automatik dosiert nicht. Genau richtig.
            _logger.LogDebug(ex, "Live-Zustaende fuer Zelt {TentId} nicht erreichbar.", tentId);
        }

        cache[tentId] = states;
        return states;
    }

    /// <summary>
    /// Die fälligen zweiten Hälften eines Zweikomponenten-Düngers abholen.
    /// </summary>
    /// <remarks>
    /// Hier steht nur, was Home Assistant braucht (die Umwälzung). Was mit einer
    /// einzelnen Hälfte geschieht, steht in <see cref="GibZweiteHaelfteAsync"/>.
    /// </remarks>
    private async Task GivePendingAsync(
        DosingRepository dosing,
        DosingService service,
        DosingContextBuilder situations,
        GrowRepository grows,
        HomeAssistantService homeAssistant,
        Dictionary<int, IReadOnlyDictionary<string, HomeAssistantState>?> statesByTent,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        foreach (var pending in dosing.GetDuePending(nowUtc))
        {
            var ziel = dosing.GetPump(pending.PumpId);
            if (ziel is null)
            {
                dosing.DeletePending(pending.Id);
                continue;
            }

            // Auch die zweite Haelfte geht nicht in stehendes Wasser. Ist die
            // Umwaelzung BESTAETIGT aus, bleibt B liegen und der naechste Takt
            // versucht es wieder — solange haelt TentHasPendingDose das ganze
            // Zelt an. Unbekannt laesst B durch: die meisten Anlagen haben
            // keinen Umwaelz-Sensor, und ein ewig gestrandetes B hiesse A ohne
            // B im Becken.
            var states = ziel.SimulationMode
                ? null
                : await StatesForTentAsync(statesByTent, grows, homeAssistant, ziel.TentId, cancellationToken);

            // Die Entscheidung steht in Dosierreihenfolge, samt Begruendung
            // fuer den Fall „unbekannt" — mit sieben Pruefungen.
            if (!Dosierreihenfolge.ZweiteHaelfteJetzt(
                    ziel.SimulationMode, DosingContextBuilder.CirculationFrom(states)))
            {
                _logger.LogWarning(
                    "Zweite Hälfte für {Pump} wartet: Umwälzpumpe steht.", ziel.Name);
                continue;
            }

            var kontext = situations.Build(ziel, nowUtc, states).Context;
            await GibZweiteHaelfteAsync(dosing, service, pending, ziel, kontext, nowUtc, cancellationToken);
        }
    }

    /// <summary>
    /// Eine fällige zweite Hälfte geben — oder begründet warten lassen.
    /// </summary>
    /// <remarks>
    /// <para><b>Anschläge.</b> B geht durch <see cref="DosingGuard.PruefeZweiteHaelfte"/>:
    /// Einzel- und Tagesgrenzen der Pumpe B gelten, die Mischpause nicht (sie
    /// hätte gerade erst A gesehen). Lehnt der Wächter ab, bleibt der Eintrag
    /// stehen; was dort greift, löst sich von selbst. Was sich nie löst — keine
    /// Fördermenge, keine Entität (<see cref="PartnerDosing.Unbrauchbar"/>) —,
    /// wird verworfen und protokolliert, sonst stünde das Becken für immer.</para>
    ///
    /// <para><b>Der Eintrag wird vor dem Schalten entfernt.</b> Bliebe er stehen
    /// und das Add-on stürzte nach dem Schalten ab, käme B beim nächsten Start
    /// ein zweites Mal. Nach dem Lauf wird er, wo nötig, neu angelegt:</para>
    /// <list type="bullet">
    /// <item><b>Gelaufen, aber gedeckelt</b> (Laufzeit- oder Mengengrenze): der
    /// Rest folgt als neuer Eintrag eine Minute später. Verwerfen hiesse, das
    /// Verhältnis bewusst zu kippen; die Grenzen gelten für den Rest genauso.</item>
    /// <item><b>Nicht gesendet</b> (Home Assistant weg, nachweislich nichts
    /// geflossen): derselbe Eintrag kommt wieder, bis
    /// <see cref="PartnerDosing.MaxFehlversuche"/> — danach verworfen und
    /// protokolliert. Vorher (bis 01.10.2026) war B hier sofort weg.</item>
    /// <item><b>Unsicher</b> (gesendet, nicht bestätigt): keine Wiederholung und
    /// kein Rest — vielleicht ist B schon geflossen, und doppeltes B ist das
    /// schlimmere Ende.</item>
    /// </list>
    ///
    /// <para><b>Protokolliert wird, was geflossen ist</b> — die Menge dieses
    /// einen Laufs, nicht der ganze ausstehende Rest. Vorher stand dort
    /// <c>pending.Ml</c> mit ungekappten Sekunden, während
    /// <see cref="DosingService.RunForSecondsAsync"/> nach 60 s abschaltete.</para>
    ///
    /// <para>Öffentlich, damit sie ohne Takt, Zeitgeber und Home Assistant
    /// geprüft werden kann.</para>
    /// </remarks>
    public async Task GibZweiteHaelfteAsync(
        DosingRepository dosing,
        DosingService service,
        PendingDose pending,
        DosingPump ziel,
        DosingContext kontext,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (PartnerDosing.Unbrauchbar(ziel) is { } unbrauchbar)
        {
            dosing.DeletePending(pending.Id);
            dosing.InsertEvent(Haelfte(ziel, nowUtc, kontext, DoseOutcome.Rejected, pending.Ml, 0, 0,
                $"Zweite Hälfte verworfen: {unbrauchbar} {pending.Ml:0.##} ml von Hand nachgeben."));
            _logger.LogError(
                "Zweite Hälfte für {Pump} verworfen: {Grund} {Ml:0.##} ml von Hand nachgeben.",
                ziel.Name, unbrauchbar, pending.Ml);
            return;
        }

        var urteil = DosingGuard.PruefeZweiteHaelfte(ziel, pending.Ml, kontext, nowUtc);
        if (!urteil.Allowed)
        {
            // Kein Protokolleintrag: der Takt fragt jede Minute, und eine
            // erreichte Tagesgrenze waere sonst bis Mitternacht eine Zeile je Minute.
            _logger.LogWarning("Zweite Hälfte für {Pump} wartet: {Grund}", ziel.Name, urteil.Reason);
            return;
        }

        dosing.DeletePending(pending.Id);

        var lauf = await service.RunForSecondsAsync(ziel, urteil.Seconds, cancellationToken);
        var rest = Math.Round(pending.Ml - urteil.Ml, 2);

        switch (lauf)
        {
            case Pumpenlauf.Gelaufen:
            {
                var folgt = rest >= 0.01;
                dosing.InsertEvent(Haelfte(ziel, nowUtc, kontext, DoseOutcome.Done, urteil.Ml, urteil.Ml, urteil.Seconds,
                    (pending.Reason ?? "Zweite Hälfte.")
                        + (folgt ? $" {urteil.Ml:0.##} von {pending.Ml:0.##} ml — {rest:0.##} ml folgen im nächsten Takt ({urteil.Reason})." : string.Empty)));
                if (folgt)
                {
                    dosing.InsertPending(Erneut(pending, rest, nowUtc, fehlversuche: 0));
                }

                _logger.LogInformation("Zweite Hälfte: {Pump} hat {Ml:0.##} ml gegeben, {Rest:0.##} ml stehen aus.",
                    ziel.Name, urteil.Ml, folgt ? rest : 0);
                break;
            }

            case Pumpenlauf.NichtGesendet:
            {
                var versuche = pending.Fehlversuche + 1;
                if (versuche >= PartnerDosing.MaxFehlversuche)
                {
                    dosing.InsertEvent(Haelfte(ziel, nowUtc, kontext, DoseOutcome.Rejected, urteil.Ml, 0, 0,
                        $"Zweite Hälfte verworfen: Home Assistant {versuche}-mal nicht erreichbar, nichts geflossen. "
                        + $"{pending.Ml:0.##} ml von Hand nachgeben."));
                    _logger.LogError(
                        "Zweite Hälfte für {Pump} nach {Versuche} Versuchen verworfen — {Ml:0.##} ml fehlen im Becken.",
                        ziel.Name, versuche, pending.Ml);
                    break;
                }

                dosing.InsertPending(Erneut(pending, pending.Ml, nowUtc, versuche));

                // Eine Zeile beim ERSTEN Fehlschlag, nicht bei jedem: dreissig
                // Ablehnungen verdraengten sonst die echten Dosen aus dem
                // Protokoll, aus dem Mischpause und Lernen lesen.
                if (pending.Fehlversuche == 0)
                {
                    dosing.InsertEvent(Haelfte(ziel, nowUtc, kontext, DoseOutcome.Rejected, urteil.Ml, 0, 0,
                        "Zweite Hälfte: " + DosingService.Grund(Pumpenlauf.NichtGesendet)
                        + $" Wird bis zu {PartnerDosing.MaxFehlversuche}-mal wiederholt."));
                }

                _logger.LogWarning(
                    "Zweite Hälfte für {Pump}: Home Assistant nicht erreichbar, Versuch {Versuch} von {Max}.",
                    ziel.Name, versuche, PartnerDosing.MaxFehlversuche);
                break;
            }

            default:
            {
                dosing.InsertEvent(Haelfte(ziel, nowUtc, kontext, DosingService.Ausgang(lauf), urteil.Ml, 0, 0,
                    "Zweite Hälfte: " + DosingService.Grund(lauf)
                    + (rest >= 0.01 ? $" Der Rest von {rest:0.##} ml wird nicht mehr gegeben." : string.Empty)));
                _logger.LogError(
                    "Zweite Hälfte: {Pump} liess sich nicht sicher schalten — bis zu {Ml:0.##} ml fehlen im Becken.",
                    ziel.Name, pending.Ml);
                break;
            }
        }
    }

    private static DoseEvent Haelfte(
        DosingPump ziel, DateTime nowUtc, DosingContext kontext, DoseOutcome ausgang,
        double angefordert, double gegeben, double sekunden, string grund) => new()
    {
        PumpId = ziel.Id,
        TentId = ziel.TentId,
        OccurredAtUtc = nowUtc,
        Trigger = DoseTrigger.Partner,
        Outcome = ausgang,
        RequestedMl = angefordert,
        DosedMl = gegeben,
        SecondsRun = sekunden,
        ValueBefore = kontext.Reading,
        Simulated = ziel.SimulationMode,
        Reason = grund,
    };

    /// <summary>Derselbe Auftrag, neu angelegt — Herkunft und Anlagezeit bleiben.</summary>
    private static PendingDose Erneut(PendingDose alt, double ml, DateTime nowUtc, int fehlversuche) => new()
    {
        PumpId = alt.PumpId,
        Ml = ml,
        DueAtUtc = nowUtc.AddMinutes(PartnerDosing.MinDelayMinutes),
        SourceDoseEventId = alt.SourceDoseEventId,
        Reason = alt.Reason,
        CreatedAtUtc = alt.CreatedAtUtc,
        Fehlversuche = fehlversuche,
    };

    /// <summary>Den Wert nach einer Dosis eintragen, sobald sie durchmischt ist.</summary>
    private void RecordEffects(DosingRepository dosing, DosingPump pump, DosingSituation situation, DateTime nowUtc)
    {
        if (situation.Context.Reading is not { } jetzt) return;

        foreach (var dose in dosing.GetEvents(pumpId: pump.Id, limit: 50))
        {
            // Die Liste kommt neu zuerst. Ist das Fenster einer Dosis zu, sind
            // alle aelteren erst recht durch — dann lohnt kein Weiterschauen.
            if (DosingFollowUp.WindowHasClosed(dose, pump.MinIntervalMinutes, nowUtc)) break;
            if (!DosingFollowUp.IsReadyForEffect(dose, pump.MinIntervalMinutes, nowUtc)) continue;

            dosing.SetValueAfter(dose.Id, jetzt);
            _logger.LogInformation(
                "Wirkung nachgetragen: Pumpe {Pump}, {Ml:0.##} ml, {Before:0.00} → {After:0.00}.",
                pump.Name, dose.DosedMl, dose.ValueBefore, jetzt);
        }
    }

    /// <summary>Eine automatische Dosis, wenn Rechnung und Anschläge es erlauben.</summary>
    /// <remarks>
    /// <para><b>Zweikomponenten-Dünger (01.10.2026).</b> Bis hierher gab die
    /// Automatik A und plante B nie ein — das tat nur das Hand-Dosieren. Jetzt
    /// plant sie B über dieselbe Stelle ein
    /// (<see cref="PartnerDosing.Einplanen"/>), und sie gibt A gar nicht erst,
    /// wenn B danach nicht laufen könnte (<see cref="PartnerDosing.AutomatikSperre"/>).
    /// Solange B aussteht, sperrt <see cref="DosingContext.TentHasPendingDose"/>
    /// das Becken — ein zweites A kommt nicht.</para>
    /// <para>Öffentlich, damit sie ohne Takt und Dienst-Container geprüft
    /// werden kann.</para>
    /// </remarks>
    /// <returns>true, wenn dosiert wurde.</returns>
    public async Task<bool> DoseIfNeededAsync(
        DosingRepository dosing,
        DosingService service,
        NotificationService notifications,
        DosingPump pump,
        DosingSituation situation,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (situation.Context.Reading is not { } ist || situation.Target is not { } ziel) return false;

        var partner = pump.PartnerPumpId is { } partnerId ? dosing.GetPump(partnerId) : null;
        if (PartnerDosing.AutomatikSperre(pump, partner) is { } sperre)
        {
            _logger.LogDebug("Automatik {Pump}: {Reason}", pump.Name, sperre);
            return false;
        }

        // Lernen seit dem letzten Wasserwechsel, Dosis auf den Fuellstand
        // skaliert — halb leeres Becken heisst halbe Menge.
        var gelernt = DosingCalculator.LearnedChangePerMl(
            dosing.GetEvents(pumpId: pump.Id, limit: 50), situation.LearnSinceUtc);
        if (DosingCalculator.MlToReach(ist, ziel, gelernt) is not { } ml) return false;

        var skaliert = Math.Round(ml * situation.VolumeFactor, 2);
        var decision = DosingGuard.EvaluateAutomatic(pump, skaliert, situation.Context, nowUtc);
        if (!decision.Allowed)
        {
            // Kein Protokolleintrag: die Automatik prueft jede Minute, und
            // „Mischpause laeuft noch" waere sonst 18 Zeilen pro Dosis.
            _logger.LogDebug("Automatik {Pump}: {Reason}", pump.Name, decision.Reason);
            return false;
        }

        var lauf = await service.RunForSecondsAsync(pump, decision.Seconds, cancellationToken);
        var ok = lauf == Pumpenlauf.Gelaufen;
        var ereignisId = dosing.InsertEvent(new DoseEvent
        {
            PumpId = pump.Id,
            TentId = pump.TentId,
            OccurredAtUtc = nowUtc,
            Trigger = DoseTrigger.Automatic,
            Outcome = DosingService.Ausgang(lauf),
            RequestedMl = decision.Ml,
            DosedMl = ok ? decision.Ml : 0,
            SecondsRun = ok ? decision.Seconds : 0,
            ValueBefore = ist,
            TargetValue = ziel,
            Simulated = pump.SimulationMode,
            Reason = ok
                ? (pump.SimulationMode ? "Automatik im Testbetrieb — es ist nichts geflossen." : "Automatik.")
                : "Automatik: " + DosingService.Grund(lauf),
        });

        if (!ok)
        {
            _logger.LogError("Automatik {Pump}: Home Assistant hat nicht geschaltet.", pump.Name);
            return false;
        }

        _logger.LogInformation("Automatik {Pump}: {Ml:0.##} ml, {Ist:0.00} → Ziel {Ziel:0.00}.", pump.Name, decision.Ml, ist, ziel);

        // Vor der Meldung: scheitert sie, muss B trotzdem eingeplant sein.
        var haelfte = PartnerDosing.Einplanen(dosing, pump, decision.Ml, nowUtc, ereignisId);

        await notifications.SendAsync(
            NotificationCategory.System,
            $"{pump.Name} hat dosiert",
            $"{decision.Ml:0.##} ml automatisch gegeben. Wert war {ist:0.00}, Ziel {ziel:0.00}."
                + (haelfte is null ? string.Empty
                    : $" {haelfte.Partner.Name} gibt in {haelfte.Minuten} min {haelfte.Ml:0.##} ml nach.")
                + (pump.SimulationMode ? " (Testbetrieb — es ist nichts geflossen.)" : string.Empty),
            cancellationToken);
        return true;
    }
}
