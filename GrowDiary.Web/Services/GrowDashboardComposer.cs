using System.Globalization;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services.Knowledge;
using Microsoft.Extensions.Logging;

namespace GrowDiary.Web.Services;

public sealed class GrowDashboardComposer
{
    private readonly TargetValueService _targetValues;
    private readonly AlertRuleRepository? _alertRules;
    private readonly HydroSetupRepository? _hydroSetups;
    private readonly SetpointProfileRepository? _setpointProfiles;
    private readonly LightRepository? _lights;
    private readonly GrowRepository? _grows;
    private readonly HarvestRepository? _harvests;
    private readonly LightCycleReader? _lightCycles;
    private readonly KnowledgeBaseLoader? _knowledge;
    private readonly ILogger<GrowDashboardComposer> _logger;

    public GrowDashboardComposer(
        TargetValueService targetValues,
        ILogger<GrowDashboardComposer> logger,
        AlertRuleRepository? alertRules = null,
        HydroSetupRepository? hydroSetups = null,
        SetpointProfileRepository? setpointProfiles = null,
        LightRepository? lights = null,
        GrowRepository? grows = null,
        HarvestRepository? harvests = null,
        LightCycleReader? lightCycles = null,
        KnowledgeBaseLoader? knowledge = null)
    {
        _knowledge = knowledge;
        _targetValues = targetValues;
        _alertRules = alertRules;
        _hydroSetups = hydroSetups;
        _setpointProfiles = setpointProfiles;
        _lights = lights;
        _grows = grows;
        _harvests = harvests;
        _lightCycles = lightCycles;
        _logger = logger;
    }

    /// <summary>
    /// Das Band einer Messgröße — die eine Lesart, gemeinsam mit den Alarmen.
    /// </summary>
    /// <remarks>
    /// Die Umrechnung selbst steht seit dem 10.09.2026 in <see cref="Zielband.FuerMetrik"/>,
    /// weil die Alarmauswertung sie ebenfalls braucht. Eine Abschrift hier hätte
    /// genau die zweite Wahrheit erzeugt, gegen die Zielband angetreten ist.
    /// </remarks>
    private static (double? Min, double? Max) TargetFor(
        string key, HydroTargetValues? t, double? rampenBodenC = null)
        => Zielband.FuerMetrik(key, t, rampenBodenC);

    public List<MetricCard> BuildTentMetrics(Tent tent, Dictionary<string, HomeAssistantState> states, IReadOnlyList<Measurement> measurements)
    {
        // Use the latest non-null value PER metric, so a partial measurement
        // (e.g. an auto-measurement that only captured temp/humidity) does not
        // blank out pH/EC/ORP that an earlier manual measurement recorded.
        var latest = BuildLatestComposite(measurements);

        // Fuer die Herkunft am Wert: die Verbund-Messung oben verschmilzt Werte
        // aus MEHREREN Messungen, traegt aber nur den juengsten Zeitstempel. Ein
        // pH von vor fuenf Tagen saehe damit aus wie von heute — genau die
        // Alter einer Messung in Minuten, oder nichts.
        //
        // Nichts bei einem Zeitstempel aus der Zukunft: so eine Messung ist
        // kein frischer Wert, sondern ein Datenfehler. Sie als „gerade eben"
        // auszugeben hat auf der Startseite die echte letzte Messung
        // verdeckt.
        static int? AlterInMinuten(DateTime? gemessenAm)
        {
            if (gemessenAm is not { } zeit) return null;
            var minuten = (int)(DateTime.Now - zeit).TotalMinutes;
            return minuten >= 0 ? minuten : null;
        }

        // Der Name, unter dem eine Kachel-Groesse in der physikalischen Tabelle
        // steht.
        //
        // Die beiden Seiten heissen verschieden — hier Kachel-Schluessel, dort
        // die Namen aus MeasurementSanityService.PhysikalischeGrenzen. Unbekannte
        // Groessen gelten dort ABSICHTLICH als plausibel; ohne diese Uebersetzung
        // liefe die Pruefung bei fuenf von zehn Groessen ins Leere und meldete
        // trotzdem nichts.
        static string? PhysikSchluessel(string kachelSchluessel) => kachelSchluessel switch
        {
            "temperature" => "air-temp",
            "humidity" => "humidity",
            "reservoir-ph" => "ph",
            "reservoir-ec" => "ec",
            "reservoir-temp" => "water-temp",
            "dissolved-oxygen" => "do",
            "orp" => "orp",
            "co2" => "co2",
            "ppfd" => "ppfd",
            "vpd" => "vpd",
            _ => null,
        };

        // Sorte Ehrlichkeit, um die es hier geht. Also je Messgroesse einzeln
        // nachsehen, aus welcher Messung der Wert wirklich stammt.
        var geordnet = measurements
            .OrderByDescending(measurement => measurement.TakenAt)
            .ThenByDescending(measurement => measurement.Id)
            .ToList();

        // Die Herkunft gehoert mit heraus. Ohne sie stand auf der Startseite
        // „Hand" ueber Werten, die die Automatik gemessen hatte — samt der
        // Mahnung „nachmessen?", die dort nichts zu suchen hat.
        //
        // Sie wird JE KENNZAHL aufgeloest, nicht je Messung: die Schleife
        // sucht fuer jede Groesse die letzte Messung, die einen Wert dafuer
        // hat, und das koennen verschiedene Messungen mit verschiedenen
        // Quellen sein.
        (double? Wert, DateTime? Zeit, ValueOrigin? Herkunft) LetzteMessung(Func<Measurement, double?> selector, string kachelSchluessel)
        {
            foreach (var messung in geordnet)
            {
                if (selector(messung) is not { } wert) continue;

                // Physikalisch Unmoegliches ueberspringen und weitersuchen.
                //
                // Der Bestand enthaelt Testzeilen mit 9000 Grad Luft, 5000 Grad
                // Wasser und -500 ppm CO2. Ohne diese Zeile stand -500 ppm als
                // aktueller Messwert auf einer Kachel der Startseite — wochenlang.
                //
                // Der Wert verschwindet dabei NICHT aus der Anzeige: im
                // Messprotokoll steht er weiter, mit ausgeschriebenem Grund. Er
                // darf nur nicht als der aktuelle Stand des Zelts gelten.
                //
                // FALLE: die Kachel-Schluessel heissen anders als die Tabelle
                // (`temperature` gegen `air-temp`, `reservoir-ph` gegen `ph`).
                // Unbekannte Groessen gelten absichtlich als plausibel — ohne die
                // Uebersetzung in PhysikSchluessel taete diese Pruefung bei fuenf
                // von zehn Groessen nichts und waere trotzdem gruen.
                if (PhysikSchluessel(kachelSchluessel) is { } physik
                    && !MeasurementSanityService.IstPhysikalischMoeglich(physik, wert))
                {
                    continue;
                }

                return (wert, messung.TakenAt, messung.Source);
            }

            return (null, null, null);
        }

        // Die Phase des aktivsten Grows im Zelt bestimmt die Sollwerte. Ohne
        // aktiven Grow gibt es keinen Zielbereich — dann zeigen die Kacheln nur
        // die Zahl, was richtig ist: ein leeres Zelt hat kein Ziel.
        //
        // Die Phase kommt aus dem GROW, nicht aus der letzten Messung. Vorher
        // hing hier alles an `latest`: wer noch nie von Hand gemessen hatte, sah
        // den ganzen Bildschirm ohne einen einzigen Zielbereich — grau, ohne
        // „im Ziel" — obwohl die Sensoren lieferten und oben „Veg · Tag 7" stand.
        //
        // Bis zum 02.09.2026 durfte eine erfasste Messung die Phase überstimmen
        // („wer sie eingetragen hat, weiss es besser"). Das stimmt nur, solange
        // sie frisch ist: die Aufschrift beschreibt DIESE Messung. Wer im Juli
        // gemessen, im August geflippt und danach die Sensoren machen lassen
        // hat, sah oben „Blüte · Tag 20" und daneben Veg-Bänder. Was der Nutzer
        // besser weiss, steht ohnehin im Grow — Flip-Datum, Samentyp, geplante
        // Veg-Dauer —, und genau daraus rechnet der Ermittler.
        var activeGrow = tent.ActiveGrows.FirstOrDefault();
        var stage = activeGrow is null ? (GrowStage?)null : GrowStageResolver.Resolve(activeGrow, DateTime.Today);

        // Welches Profil gilt: der Grow, sonst sein Hydro-System, sonst der
        // Anbaustil. Ohne diese Kette griffe immer nur der Anbaustil, und ein
        // eigenes Profil bliebe wirkungslos.
        var resolved = activeGrow is null
            ? null
            : SetpointProfileResolver.Resolve(
                activeGrow.SetpointProfileId,
                SystemProfileFor(activeGrow),
                activeGrow.HydroStyle);

        // Die ganze Kette an EINER Stelle — Profil, Phase, Feedchart. Die
        // eigenen Grenzen kommen hier NICHT mit: ApplyUserTargets legt sie
        // weiter unten je Kachel darueber, damit auch Kacheln ohne Profil eine
        // eingetragene Grenze bekommen.
        var targets = activeGrow is null || stage is null
            ? null
            : Zielband.FuerGrow(
                _targetValues, _knowledge, activeGrow, stage.Value,
                SystemProfileFor(activeGrow), null);

        // Wohin die Nachtabsenkung faehrt — sonst meldet die Kachel die eigene
        // Regelung der App als Abweichung, waehrend Messprotokoll und Diagnose
        // sie kennen.
        var rampenBoden = activeGrow is null || resolved is null ? null : Wasserband.RampenBodenC(
            activeGrow,
            _targetValues.GetTargets(resolved.ProfileId, GrowStage.Flower),
            _targetValues.GetTargets(resolved.ProfileId, GrowStage.Finish));

        MetricCard Build(string label, string key, Func<Measurement?, double?> fallback, string tone = "default", string? explicitUnit = null)
        {
            if (states.TryGetValue(key, out var state))
            {
                return new MetricCard
                {
                    Key = key,
                    Label = label,
                    Value = state.NumericValue.HasValue
                        ? FormatMetricValue(key, state.NumericValue.Value)
                        : state.State,
                    Unit = explicitUnit ?? state.UnitOfMeasurement,
                    Tone = tone,
                    Hint = state.FriendlyName,
                    NumericValue = state.NumericValue,
                    ValueSource = "live",
                    TargetMin = TargetFor(key, targets, rampenBoden).Min,
                    TargetMax = TargetFor(key, targets, rampenBoden).Max
                };
            }

            var (value, gemessenAm, herkunft) = LetzteMessung(m => fallback(m), key);
            return new MetricCard
            {
                Key = key,
                Label = label,
                Value = value.HasValue ? FormatMetricValue(key, value.Value) : "–",
                Unit = explicitUnit,
                Tone = tone,
                Hint = value.HasValue
                    ? "Letzte Messung"
                    : states.Count == 0 ? "Noch nicht mit Home Assistant verbunden" : "Kein Entity gemappt",
                NumericValue = value,
                // „auto" nur, wenn der Wert wirklich aus Home Assistant kam.
                // Alles andere (von Hand, importiert, berechnet) bleibt
                // „hand" — es ist jedenfalls nichts, was ein Sensor gerade
                // liefert.
                ValueSource = value.HasValue
                    ? (herkunft == ValueOrigin.HomeAssistant ? "auto" : "hand")
                    : null,
                // TakenAt ist Ortszeit (so wird sie erfasst und gelesen), also
                // gegen DateTime.Now rechnen — gegen UtcNow waere der Wert um
                // die Zeitzone zu alt. Die Falle aus beta.20, andersherum.
                //
                // Eine Messung, deren Zeitstempel in der ZUKUNFT liegt, ist
                // kein frischer Wert. Vorher wurde das negative Alter auf 0
                // geklemmt — eine Zeile mit dem Datum 2099 galt damit als
                // „gerade eben" und verdraengte die echten Messungen.
                MeasuredAgeMinutes = AlterInMinuten(gemessenAm),
                TargetMin = TargetFor(key, targets, rampenBoden).Min,
                TargetMax = TargetFor(key, targets, rampenBoden).Max
            };
        }

        var cards = new List<MetricCard>
        {
            Build("Temperatur", "temperature", m => m?.AirTemperatureC, explicitUnit: "°C"),
            Build("Luftfeuchte", "humidity", m => m?.HumidityPercent, explicitUnit: "%"),
            BuildVpdMetric(tent, states, latest, targets),
            BuildLightCycleMetric(tent, states, _lightCycles),
            BuildPpfdMetric(tent, states, latest)
        };

        if (tent.Co2Available || measurements.Any(m => m.Co2Ppm.HasValue))
        {
            var co2 = Build("CO2", "co2", m => m?.Co2Ppm, explicitUnit: "ppm");

            // Ein Sensor misst nur. Ohne Anreicherung steht die Luft bei
            // ~400-500 ppm, und das Profilziel (800-1400) stuende fuer immer
            // „daneben" — die Kachel war dann dauerhaft rot, obwohl alles
            // normal ist. Ohne Brenner also kein Ziel, mit Erklaerung.
            if (!tent.HasCo2Enrichment)
            {
                co2.TargetMin = null;
                co2.TargetMax = null;
                co2.Hint = "Ohne CO₂-Anreicherung ist Umgebungsluft (~400–500 ppm) normal";
            }

            cards.Add(co2);
        }

        var hasActiveHydro = tent.ActiveGrows.Any(g => g.IrrigationType == IrrigationType.ActiveHydro);

        // A reservoir metric is shown when its sensor is mapped and Home Assistant returns a
        // live value (states.ContainsKey), OR there is an active hydro grow, OR a past
        // measurement recorded it. Keying on the mapped sensor is essential: without it, freshly
        // mapped pH/EC/water-temp sensors stayed blank on the live dashboard until a grow was
        // flagged active-hydro or a manual measurement existed.
        if (hasActiveHydro || states.ContainsKey("reservoir-ph") || measurements.Any(m => m.ReservoirPh.HasValue))
            cards.Add(Build("pH", "reservoir-ph", m => m?.ReservoirPh));

        if (hasActiveHydro || states.ContainsKey("reservoir-ec") || measurements.Any(m => m.ReservoirEc.HasValue))
            cards.Add(Build("EC", "reservoir-ec", m => m?.ReservoirEc, explicitUnit: "mS/cm"));

        if (hasActiveHydro || states.ContainsKey("orp") || measurements.Any(m => m.OrpMv.HasValue))
            cards.Add(Build("ORP", "orp", m => m?.OrpMv, explicitUnit: "mV"));

        if (hasActiveHydro || states.ContainsKey("dissolved-oxygen") || measurements.Any(m => m.DissolvedOxygenMgL.HasValue))
        {
            var doKarte = Build("DO", "dissolved-oxygen", m => m?.DissolvedOxygenMgL, explicitUnit: "mg/L");

            // Ohne DO-Messwert wenigstens die Physik: wie viel Sauerstoff das
            // Wasser bei seiner Temperatur ueberhaupt halten KANN. Das ist keine
            // Messung und heisst auch nicht so — aber es zeigt den oft
            // uebersehenen Hebel: warmes Wasser haelt wenig, egal wie gross die
            // Pumpe ist.
            var wasserTemp = states.TryGetValue("reservoir-temp", out var tempState)
                ? tempState.NumericValue
                : LetzteMessung(m => m.ReservoirWaterTempC, "reservoir-temp").Wert;
            if (doKarte.NumericValue is null && wasserTemp is { } temp)
            {
                doKarte.Hint = $"max. ~{AerationCheck.SaettigungMgL(temp).ToString("0.0", AppCulture.German)} mg/L "
                    + $"bei {temp.ToString("0.0", AppCulture.German)} °C möglich (berechnet)";
            }

            cards.Add(doKarte);
        }

        // Water level can be measured in liters (scale/flow) or centimeters (distance
        // sensor) — two separate mapping slots so units are always unambiguous.
        if (states.ContainsKey("reservoir-level") || measurements.Any(m => m.ReservoirLevelLiters.HasValue))
        {
            states.TryGetValue("reservoir-level", out var levelState);
            cards.Add(new MetricCard
            {
                Key = "reservoir-level",
                Label = "Wasserstand",
                Value = levelState is not null
                    ? levelState.NumericValue?.ToString("0.0") ?? levelState.State
                    : latest?.ReservoirLevelLiters?.ToString("0.0") ?? "–",
                // Als Zahl, nicht nur als Text: sonst haelt die Kachel den Wert
                // fuer eine Beschriftung, laesst die Einheit weg — und „72"
                // ohne „L" ist neben einem cm-Sensor schlicht zweideutig.
                NumericValue = levelState?.NumericValue ?? latest?.ReservoirLevelLiters,
                Unit = levelState?.UnitOfMeasurement ?? "L",
                Tone = "info",
                Hint = levelState is not null
                    ? levelState.FriendlyName
                    : latest?.ReservoirLevelLiters.HasValue == true ? "Letzte Messung" : null
            });
        }

        if (states.ContainsKey("reservoir-level-cm") || measurements.Any(m => m.ReservoirLevelCm.HasValue))
        {
            states.TryGetValue("reservoir-level-cm", out var levelCmState);
            cards.Add(new MetricCard
            {
                Key = "reservoir-level-cm",
                Label = "Wasserstand",
                Value = levelCmState is not null
                    ? levelCmState.NumericValue?.ToString("0.0") ?? levelCmState.State
                    : latest?.ReservoirLevelCm?.ToString("0.0") ?? "–",
                NumericValue = levelCmState?.NumericValue ?? latest?.ReservoirLevelCm,
                Unit = levelCmState?.UnitOfMeasurement ?? "cm",
                Tone = "info",
                Hint = levelCmState is not null
                    ? levelCmState.FriendlyName
                    : latest?.ReservoirLevelCm.HasValue == true ? "Letzte Messung" : null
            });
        }

        if (hasActiveHydro || states.ContainsKey("reservoir-temp") || measurements.Any(m => m.ReservoirWaterTempC.HasValue))
            cards.Add(Build("Wassertemp.", "reservoir-temp", m => m?.ReservoirWaterTempC, explicitUnit: "°C"));

        // Ist im Zelt gerade Tag? Nachts ist PPFD 0 richtig, CO₂ bei
        // Umgebungsluft richtig, und VPD-Ziele gelten für die Lichtphase.
        // Vorher malte jede Nacht rote Kacheln, der Score sank grundlos, und
        // genau so entsteht Alarm-Müdigkeit. Unbekannt heisst: wie bisher —
        // lieber ein unnötiges Nacht-Urteil als ein unterdrücktes Tag-Urteil.
        states.TryGetValue("light-status", out var lightNow);
        var lights = LightClock.Resolve(lightNow, _lights?.GetActiveLightScheduleForTent(tent.Id), DateTime.UtcNow);

        if (lights == LightsNow.Off)
        {
            foreach (var card in cards.Where(card => LightClock.IsDaytimeOnly(card.Key)))
            {
                card.TargetMin = null;
                card.TargetMax = null;
                card.TargetNote = null;
                card.TargetDerived = false;
                card.Hint = card.Key switch
                {
                    "ppfd" => "Licht aus — 0 ist hier richtig",
                    "co2" => "Licht aus — nachts braucht die Pflanze kein CO₂",
                    _ => "Licht aus — das VPD-Ziel gilt bei Licht an",
                };
            }
        }
        else
        {
            // Die abgeleiteten Klima-Bänder hängen am VPD-Ziel und damit am Tag.
            ApplyClimateBands(cards, targets, tent.LeafTempOffsetC, stage);
        }

        // Nach der Ernte wird das Zelt zum Trockenraum — 7 bis 14 Tage, und es
        // ist das hoechste Schimmelrisiko des ganzen Zyklus. Vorher schaute die
        // App ab der Ernte einfach weg: Grow abgeschlossen, keine Ziele mehr,
        // obwohl die Sensoren weiter haengen und genau jetzt zaehlen.
        ApplyDryingTargets(cards, tent, activeGrow);

        // Der Profilname auf die Kacheln, solange es nicht das Mitgelieferte ist.
        ApplyProfileNote(cards, resolved);

        // Zuletzt und damit ueber allem: was der Nutzer selbst eingetragen hat.
        // Erst hier, damit es auch die zurueckgerechneten Klimabaender schlaegt.
        ApplyUserTargets(cards, tent.Id, lights);

        return cards;
    }

    /// <summary>
    /// Gibt Temperatur und Luftfeuchte ihr Zielband — zurückgerechnet aus dem
    /// VPD-Ziel und dem jeweils anderen gemessenen Wert.
    /// </summary>
    /// <remarks>
    /// Das Wissen kennt für Klima nur ein VPD-Band. Die beiden größten Kacheln
    /// des Bildschirms blieben dadurch immer ohne Bewertung, während die kleine
    /// VPD-Kachel daneben eine hatte. Gerechnet wird mit derselben Formel wie
    /// hin, nur nach der anderen Variablen aufgelöst; erfunden wird nichts.
    ///
    /// Ohne den Partnerwert gibt es kein Band: ein Temperaturziel ohne bekannte
    /// Feuchte wäre geraten.
    /// </remarks>
    /// <summary>Das Profil des Hydro-Systems, an dem der Grow hängt.</summary>
    private string? SystemProfileFor(GrowRun grow)
        => grow.SystemId is { } systemId ? _hydroSetups?.GetSystem(systemId)?.SetpointProfileId : null;

    /// <summary>
    /// Schreibt den Profilnamen auf die Kacheln, wenn ein eigenes Profil gilt.
    /// </summary>
    /// <remarks>
    /// Nur bei Abweichung vom Mitgelieferten. Stünde der Name überall, wäre er
    /// auf jeder Kachel Text, der nichts beantwortet — dieselbe Regel wie bei
    /// „dein Wert".
    /// </remarks>
    private void ApplyProfileNote(List<MetricCard> cards, ResolvedProfile? resolved)
    {
        if (resolved is null) return;
        if (SetpointProfile.IdFromReference(resolved.ProfileId) is not { } customId) return;

        var name = _setpointProfiles?.Get(customId)?.Name;
        if (string.IsNullOrWhiteSpace(name)) return;

        foreach (var card in cards)
        {
            if (card.TargetMin is null && card.TargetMax is null) continue;
            if (card.TargetNote is not null) continue;   // zurueckgerechnete Baender behalten ihren Zusatz
            card.TargetNote = name;
        }
    }

    /// <summary>
    /// Der eingetragene Wert des Nutzers gewinnt — über dem Wissen und über
    /// jedem zurückgerechneten Band.
    /// </summary>
    /// <remarks>
    /// Und die Kachel sagt es: „dein Wert". Ohne den Zusatz stünde dort eine
    /// Zahl, die von den mitgelieferten abweicht, ohne dass jemand erkennen
    /// könnte warum — genau die Verwirrung, die das hier abstellt.
    /// </remarks>
    /// <summary>
    /// Trocknungs-Klima auf Temperatur und Feuchte, solange im Zelt frisch
    /// geerntet haengt.
    /// </summary>
    /// <remarks>
    /// Trocknung liegt vor, wenn kein Grow mehr laeuft, der letzte in den
    /// vergangenen drei Wochen geerntet wurde und noch kein Trockengewicht
    /// eingetragen ist — das Gewicht ist der natuerliche Abschluss: gewogen
    /// wird nach dem Trocknen.
    /// </remarks>
    private void ApplyDryingTargets(List<MetricCard> cards, Tent tent, GrowRun? activeGrow)
    {
        if (activeGrow is not null) return;
        if (DryingWindow.DayFor(_grows, _harvests, tent.Id, DateTime.Today) is not { } tag) return;

        if (cards.FirstOrDefault(card => card.Key == "temperature") is { } temperatur)
        {
            temperatur.TargetMin = MoldGuard.DryingTempMinC;
            temperatur.TargetMax = MoldGuard.DryingTempMaxC;
            temperatur.TargetNote = $"Trocknung · Tag {tag}";
            temperatur.TargetDerived = false;
        }

        if (cards.FirstOrDefault(card => card.Key == "humidity") is { } feuchte)
        {
            feuchte.TargetMin = MoldGuard.DryingHumidityMin;
            feuchte.TargetMax = MoldGuard.DryingHumidityMax;
            feuchte.TargetNote = $"Trocknung · Tag {tag}";
            feuchte.TargetDerived = false;
        }
    }

    private void ApplyUserTargets(List<MetricCard> cards, int tentId, LightsNow lights = LightsNow.Unknown)
    {
        var rules = _alertRules?.GetForTent(tentId);
        if (rules is null || rules.Count == 0) return;

        foreach (var card in cards)
        {
            // Auch der eigene Grenzwert für PPFD/CO₂/VPD meint den Tag — bei
            // Licht aus würde er jede Nacht anschlagen.
            if (lights == LightsNow.Off && LightClock.IsDaytimeOnly(card.Key)) continue;

            if (UserTargets.For(card.Key, rules) is not { } eigene) continue;

            card.TargetMin = eigene.Min;
            card.TargetMax = eigene.Max;
            card.TargetNote = UserTargets.SourceLabel;
            // Kein abgeleiteter Wert mehr: was der Nutzer setzt, zaehlt voll in
            // den Score. Sonst waere sein eigener Grenzwert der einzige, der
            // nicht bewertet wird.
            card.TargetDerived = false;
        }
    }

    private static void ApplyClimateBands(List<MetricCard> cards, HydroTargetValues? targets, double leafOffsetC, GrowStage? stage)
    {
        if (targets is null) return;

        var temperature = cards.FirstOrDefault(card => card.Key == "temperature");
        var humidity = cards.FirstOrDefault(card => card.Key == "humidity");

        // Beide Bänder aus den Werten VOR der Änderung rechnen, sonst hinge das
        // zweite am gerade gesetzten Ziel des ersten.
        var luft = temperature?.NumericValue;
        var feuchte = humidity?.NumericValue;

        // Der Schimmeldeckel der Phase begrenzt jede Feuchte-Empfehlung. Die
        // reine VPD-Rückrechnung kennt nur Physik — in der Blüte bei 32 °C käme
        // sonst „64–68 %" heraus, und ab ~60 % droht dort Grauschimmel.
        var deckel = stage is { } phase ? MoldGuard.MaxHumidityPercent(phase) : (double?)null;

        if (luft is { } l && humidity is not null && humidity.TargetMin is null)
        {
            var (min, max) = ClimateBandCalculator.HumidityBand(l, targets.VpdMin, targets.VpdMax, leafOffsetC, deckel);
            if (min is not null)
            {
                humidity.TargetMin = min;
                humidity.TargetMax = max;
                // Geschuetztes Leerzeichen zwischen Zahl und Einheit: sonst stand
                // die Zahl am Zeilenende und ihr „°C" allein in der naechsten.
                humidity.TargetNote = deckel is { } cap && max >= cap
                    ? $"bei {l.ToString("0.#", AppCulture.German)}\u00A0°C · Schimmelschutz: höchstens {cap.ToString("0", AppCulture.German)}\u00A0%"
                    : $"bei {l.ToString("0.#", AppCulture.German)}\u00A0°C";
                humidity.TargetDerived = true;
            }
            else if (ClimateBandCalculator.HumidityBand(l, targets.VpdMin, targets.VpdMax, leafOffsetC).Min is not null)
            {
                // Ohne Deckel gäbe es ein Band — der Schimmelschutz hat es
                // geschluckt. Dann ist die ehrliche Ansage: nicht die Feuchte
                // hochziehen, sondern die Temperatur senken.
                humidity.Hint = $"Fürs VPD-Ziel bräuchte es mehr als {deckel!.Value.ToString("0", AppCulture.German)} % — ab da droht Schimmel. Besser die Temperatur senken.";
            }
        }

        if (feuchte is { } f && temperature is not null && temperature.TargetMin is null)
        {
            var (min, max) = ClimateBandCalculator.TemperatureBand(f, targets.VpdMin, targets.VpdMax, leafOffsetC);
            if (min is not null)
            {
                temperature.TargetMin = min;
                temperature.TargetMax = max;
                temperature.TargetNote = $"bei {f.ToString("0.#", AppCulture.German)}\u00A0% RLF";
                temperature.TargetDerived = true;
            }
            else
            {
                // Kein empfehlbares Temperatur-Ziel bei dieser Feuchte. Das hat
                // zwei moegliche Gruende — physikalisch keine Loesung, oder die
                // Loesung laege ausserhalb dessen, was ein Growraum sein kann
                // (Keimlings-Ziel bei 54 % RLF: rechnerisch 5 °C). Die Antwort
                // ist in beiden Faellen dieselbe: der Hebel ist die Feuchte,
                // nicht die Temperatur. Bei Keimlingen mit dem konkreten
                // Werkzeug dazu — niemand soll raten, WIE die Feuchte hoch soll.
                var werkzeug = stage is GrowStage.Seedling or GrowStage.Clone
                    ? " Bei Keimlingen: Haube drauf oder Luftbefeuchter."
                    : string.Empty;
                temperature.Hint = $"Kein sinnvolles Temperatur-Ziel bei {f.ToString("0.#", AppCulture.German)} % RLF"
                    + $" — hier muss die Luftfeuchte rauf, nicht die Temperatur runter.{werkzeug}";
            }
        }
    }

    private static Measurement? BuildLatestComposite(IReadOnlyList<Measurement> measurements)
    {
        var ordered = measurements
            .OrderByDescending(measurement => measurement.TakenAt)
            .ThenByDescending(measurement => measurement.Id)
            .ToList();
        if (ordered.Count == 0)
        {
            return null;
        }

        double? Pick(Func<Measurement, double?> selector)
        {
            foreach (var measurement in ordered)
            {
                var value = selector(measurement);
                if (value.HasValue)
                {
                    return value;
                }
            }

            return null;
        }

        var head = ordered[0];
        return new Measurement
        {
            Id = head.Id,
            GrowId = head.GrowId,
            TakenAt = head.TakenAt,
            Stage = head.Stage,
            Source = head.Source,
            AirTemperatureC = Pick(m => m.AirTemperatureC),
            HumidityPercent = Pick(m => m.HumidityPercent),
            ReservoirPh = Pick(m => m.ReservoirPh),
            ReservoirEc = Pick(m => m.ReservoirEc),
            ReservoirWaterTempC = Pick(m => m.ReservoirWaterTempC),
            ReservoirLevelLiters = Pick(m => m.ReservoirLevelLiters),
            ReservoirLevelCm = Pick(m => m.ReservoirLevelCm),
            DissolvedOxygenMgL = Pick(m => m.DissolvedOxygenMgL),
            OrpMv = Pick(m => m.OrpMv),
            PpfdMol = Pick(m => m.PpfdMol),
            Co2Ppm = Pick(m => m.Co2Ppm),
        };
    }

    private static MetricCard BuildLightCycleMetric(
        Tent tent, Dictionary<string, HomeAssistantState> states, LightCycleReader? lightCycles)
    {
        // Der gelernte Zyklus aus den beobachteten Flanken — „18/6", „12/12".
        // Das TODO von Sprint B1b ist damit erledigt: niemand traegt den Zyklus
        // ein, Grow OS liest ihn aus dem, was ohnehin aufgezeichnet wird.
        var cycle = lightCycles?.CycleFor(tent.Id, DateTime.UtcNow);
        // Show the live light on/off state from the mapped LightStatus entity.
        if (states.TryGetValue(TentSensorMetricKeyMap.Resolve(SensorMetricType.LightStatus), out var lightState))
        {
            var normalized = LightStateNormalizer.Normalize(lightState.State);
            if (normalized != LightState.Unknown)
            {
                var isOn = normalized == LightState.On;
                return new MetricCard
                {
                    Key = "light-cycle",
                    Label = "Licht",
                    Value = isOn ? "An" : "Aus",
                    Tone = isOn ? "accent" : "info",
                    Hint = cycle is not null
                        ? $"{cycle.Label} · an {cycle.OnAt:HH:mm}, aus {cycle.OffAt:HH:mm}"
                        : lightState.FriendlyName ?? (isOn ? "Licht eingeschaltet" : "Licht ausgeschaltet")
                };
            }
        }

        return new MetricCard
        {
            Key = "light-cycle",
            Label = "Lichtzyklus",
            Value = cycle?.Label ?? "–",
            Tone = "info",
            Hint = cycle is not null
                ? $"aus den letzten {cycle.Days} Schaltvorgängen gelesen"
                : states.Count == 0
                    ? "Nicht mit Home Assistant verbunden"
                    // „Kein Sensor gemappt" war hier gelogen, wenn einer gemappt
                    // war und sein Wert nur nicht gelesen werden konnte. Wer den
                    // Sensor gerade eingetragen hat, sucht dann an der falschen
                    // Stelle. Also sagen, WAS ankommt.
                    : states.TryGetValue(TentSensorMetricKeyMap.Resolve(SensorMetricType.LightStatus), out var roh)
                        ? $"Sensor liefert „{roh.State}“ — daraus lässt sich kein An/Aus lesen."
                        : "Kein Licht-Sensor gemappt"
        };
    }

    private static MetricCard BuildPpfdMetric(Tent tent, IReadOnlyDictionary<string, HomeAssistantState> states, Measurement? latest)
    {
        if (states.TryGetValue("ppfd", out var state))
        {
            return new MetricCard
            {
                Key = "ppfd",
                Label = "PPFD",
                Value = state.NumericValue.HasValue
                    ? FormatMetricValue("ppfd", state.NumericValue.Value)
                    : state.State,
                Unit = state.UnitOfMeasurement ?? "µmol/m²/s",
                Tone = "accent",
                Hint = state.FriendlyName
            };
        }

        if (latest?.PpfdMol.HasValue == true)
        {
            return new MetricCard
            {
                Key = "ppfd",
                Label = "PPFD",
                Value = FormatMetricValue("ppfd", latest.PpfdMol.Value),
                Unit = "µmol/m²/s",
                Tone = "accent",
                Hint = "Letzte Messung"
            };
        }

        return new MetricCard
        {
            Key = "ppfd",
            Label = "PPFD",
            Value = "–",
            Unit = null,
            Tone = "accent",
            Hint = "Kein Sensor konfiguriert"
        };
    }

    private static string FormatMetricValue(string key, double value)
    {
        return key switch
        {
            "temperature"     => value.ToString("0.0"),
            "humidity"        => value.ToString("0"),
            "vpd"             => value.ToString("0.00"),
            "co2"             => value.ToString("0"),
            "reservoir-ph"    => value.ToString("0.00"),
            "reservoir-ec"    => value.ToString("0.00"),
            "orp"             => value.ToString("0"),
            "dissolved-oxygen" => value.ToString("0.0"),
            "reservoir-temp"  => value.ToString("0.0"),
            "reservoir-level" => value.ToString("0.0"),
            "ppfd"            => value.ToString("0"),
            "ups-battery"     => value.ToString("0"),
            _                 => value.ToString("0.#")
        };
    }

    /// <summary>
    /// VPD is either read from a mapped entity, or derived. When deriving, prefer the LIVE
    /// temperature/humidity over the last stored measurement (which could be days old), and
    /// apply the tent's leaf-temperature offset so the number reflects leaf VPD.
    /// </summary>
    private MetricCard BuildVpdMetric(Tent tent, Dictionary<string, HomeAssistantState> states, Measurement? latest, HydroTargetValues? targets)
    {
        if (states.TryGetValue("vpd", out var mapped))
        {
            return new MetricCard
            {
                Key = "vpd",
                Label = "VPD",
                Value = mapped.NumericValue.HasValue ? FormatMetricValue("vpd", mapped.NumericValue.Value) : mapped.State,
                Unit = "kPa",
                Tone = "accent",
                Hint = mapped.FriendlyName,
                NumericValue = mapped.NumericValue,
                TargetMin = TargetFor("vpd", targets).Min,
                TargetMax = TargetFor("vpd", targets).Max
            };
        }

        var liveTemp = states.TryGetValue("temperature", out var t) ? t.NumericValue : null;
        var liveHumidity = states.TryGetValue("humidity", out var h) ? h.NumericValue : null;
        var fromLive = liveTemp.HasValue && liveHumidity.HasValue;

        var value = VpdCalculator.Calculate(
            fromLive ? liveTemp : latest?.AirTemperatureC,
            fromLive ? liveHumidity : latest?.HumidityPercent,
            tent.LeafTempOffsetC);

        var offsetHint = tent.LeafTempOffsetC > 0 ? $", Blatt −{tent.LeafTempOffsetC:0.#} °C" : string.Empty;
        return new MetricCard
        {
            Key = "vpd",
            Label = "VPD",
            Value = value.HasValue ? FormatMetricValue("vpd", value.Value) : "–",
            Unit = "kPa",
            Tone = "accent",
            Hint = value.HasValue
                ? (fromLive ? $"Berechnet aus Live-Werten{offsetHint}" : $"Berechnet aus letzter Messung{offsetHint}")
                : "Temperatur und Luftfeuchte fehlen",
            NumericValue = value,
            TargetMin = TargetFor("vpd", targets).Min,
            TargetMax = TargetFor("vpd", targets).Max
        };
    }

}
