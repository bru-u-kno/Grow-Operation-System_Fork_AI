using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Was an ist, wird wieder aus — auch wenn schon das Einschalten „scheiterte".
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (29.09.2026).</b> Jeder Aufruf an Home Assistant hat ein
/// Zeitlimit von vier Sekunden. Kommt die Antwort später, meldet der Aufruf
/// <c>false</c> — obwohl der Befehl angekommen sein kann. Dosierpumpe und
/// CO₂-Probe kehrten in diesem Fall zurück, <b>ohne je „aus" zu senden</b>: die
/// Pumpe lief bis zur Abschaltung in Home Assistant, das Ventil blieb offen.
/// Und weil die Dosis als <c>Failed</c> galt, zählte sie weder auf Mischpause
/// noch Tagesgrenze — die Automatik dosierte eine Minute später erneut.</para>
///
/// <para>Die Anlage hier bildet genau das nach: ein Befehl, der wirkt, dessen
/// Antwort aber ausbleibt; und ein „aus", das die Wolke still verwirft.</para>
/// </remarks>
public sealed class AusschaltenNachFehlschlagTests : IDisposable
{
    private const string Pumpe = "switch.dosier_ph_minus";
    private const string Ventil = "switch.co2_ventil";

    private readonly string _wurzel;
    private readonly AppPaths _pfade;
    private readonly GrowRepository _grows;
    private readonly DosingRepository _dosing;

    public AusschaltenNachFehlschlagTests()
    {
        _wurzel = Path.Combine(Path.GetTempPath(), "Ausschalten_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_wurzel);
        _pfade = new AppPaths(_wurzel);
        TestDatabase.InitializeWithDefaultTent(_pfade);
        _grows = new GrowRepository(_pfade);
        _dosing = new DosingRepository(_pfade);
        _grows.SaveHomeAssistantSettings(new HomeAssistantSettings
        {
            BaseUrl = "http://ha.local:8123", AccessToken = "token", Enabled = true,
        });
    }

    public void Dispose()
    {
        try { Directory.Delete(_wurzel, recursive: true); } catch { /* Windows haelt manchmal fest */ }
    }

    /// <summary>Eine Anlage mit Schaltern, die sich wie die echten danebenbenehmen.</summary>
    private sealed class Anlage : IAcFunk
    {
        private readonly Dictionary<string, string> _stand = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Diese Entitäten schalten, aber die Antwort kommt zu spät: der Aufruf
        /// meldet <c>false</c>, der Zustand ist trotzdem umgelegt.
        /// </summary>
        public HashSet<string> AntwortZuSpaet { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>So viele „aus" verwirft die Wolke still, bevor eines greift.</summary>
        public Dictionary<string, int> AusVerworfen { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// So oft meldet die Wolke nach einem angekommenen „aus" noch den alten
        /// Zustand — sie ist träge, nicht taub.
        /// </summary>
        public Dictionary<string, int> SpaeteMeldung { get; } = new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, int> _nochAlt = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Solange wahr, ist Home Assistant nicht erreichbar — nichts kommt an, nichts ist lesbar.</summary>
        public bool Weg { get; set; }

        /// <summary>Jeder Aufruf als „entity:dienst", in der Reihenfolge des Sendens.</summary>
        public List<string> Gesendet { get; } = new();

        public void Setzen(string entityId, string zustand) => _stand[entityId] = zustand;

        public string? Stand(string entityId) => _stand.TryGetValue(entityId, out var z) ? z : null;

        public int Anzahl(string entityId, string dienst)
            => Gesendet.Count(g => g == $"{entityId}:{dienst}");

        public Task<HomeAssistantState?> ZustandAsync(
            HomeAssistantSettings einstellungen, string entityId, CancellationToken ct)
        {
            if (Weg || !_stand.TryGetValue(entityId, out var z)) return Task.FromResult<HomeAssistantState?>(null);
            if (_nochAlt.TryGetValue(entityId, out var rest) && rest > 0)
            {
                _nochAlt[entityId] = rest - 1;
                z = "on";
            }
            return Task.FromResult<HomeAssistantState?>(new HomeAssistantState { EntityId = entityId, State = z });
        }

        public Task<bool> SchickenAsync(
            HomeAssistantSettings einstellungen, string domain, string dienst, string entityId,
            IReadOnlyDictionary<string, object> daten, CancellationToken ct)
        {
            Gesendet.Add($"{entityId}:{dienst}");
            if (Weg) return Task.FromResult(false);

            var an = dienst == "turn_on"
                || (dienst == "select_option" && Equals(daten.GetValueOrDefault("option"), "On"));

            if (!an && AusVerworfen.TryGetValue(entityId, out var rest) && rest > 0)
            {
                // Still verworfen: Home Assistant meldet Erfolg, am Gerät ändert sich nichts.
                AusVerworfen[entityId] = rest - 1;
                return Task.FromResult(true);
            }

            _stand[entityId] = an ? "on" : "off";
            if (!an && SpaeteMeldung.TryGetValue(entityId, out var spaet)) _nochAlt[entityId] = spaet;
            return Task.FromResult(!AntwortZuSpaet.Contains(entityId));
        }
    }

    /// <summary>Wartet nicht — Pumpenlauf und Nachlesepausen vergehen sofort.</summary>
    private static Task Sofort(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    private DosingService Dosierer(Anlage anlage)
        => new(_grows, _dosing, anlage,
            new Ausschalter(anlage, NullLogger<Ausschalter>.Instance),
            NullLogger<DosingService>.Instance, Sofort);

    private static SteuerungProbeService Probe(Anlage anlage)
        => new(anlage, new Ausschalter(anlage, NullLogger<Ausschalter>.Instance),
            NullLogger<SteuerungProbeService>.Instance, Sofort);

    private static DosingPump PhPumpe() => new()
    {
        Id = 1, TentId = 1, Name = "pH Minus", Purpose = DosingPurpose.PhDown,
        HaEntityId = Pumpe, MlPerMinute = 46,
    };

    private static readonly HomeAssistantSettings Einstellungen = new()
    {
        BaseUrl = "http://ha.local:8123", AccessToken = "token", Enabled = true,
    };

    // ---------- K1: Dosierpumpe ----------

    [Fact]
    public async Task EinschaltenOhneAntwort_PumpeWirdTrotzdemAusgeschaltet()
    {
        var anlage = new Anlage();
        anlage.Setzen(Pumpe, "off");
        anlage.AntwortZuSpaet.Add(Pumpe);

        var gelaufen = await Dosierer(anlage).RunForSecondsAsync(PhPumpe(), 3);

        Assert.Equal(Pumpenlauf.Unsicher, gelaufen);
        Assert.Equal(DoseOutcome.Failed, DosingService.Ausgang(gelaufen));
        Assert.Equal(1, anlage.Anzahl(Pumpe, "turn_on"));
        Assert.True(anlage.Anzahl(Pumpe, "turn_off") >= 1, "Nach einem unbestätigten Einschalten kam kein „aus\".");
        Assert.Equal("off", anlage.Stand(Pumpe));
    }

    [Fact]
    public async Task VerworfenesAus_WirdWiederholtBisDiePumpeSteht()
    {
        var anlage = new Anlage();
        anlage.Setzen(Pumpe, "off");
        anlage.AusVerworfen[Pumpe] = 1;

        var gelaufen = await Dosierer(anlage).RunForSecondsAsync(PhPumpe(), 3);

        Assert.Equal(Pumpenlauf.Gelaufen, gelaufen);
        Assert.Equal(2, anlage.Anzahl(Pumpe, "turn_off"));
        Assert.Equal("off", anlage.Stand(Pumpe));
    }

    /// <summary>
    /// Ist Home Assistant weg, wird nichts gesendet — und der Versuch sperrt
    /// die Pumpe nicht. Vorher (Prüfer-Befund 29.09.2026) zählte jeder
    /// Versuch als <c>Failed</c> auf die Tagesgrenze: sechs Versuche bei
    /// ausgefallenem HA sperrten die Pumpe bis Mitternacht, auch von Hand.
    /// </summary>
    [Fact]
    public async Task HomeAssistantWeg_NichtsGesendetUndKeineSperre()
    {
        var anlage = new Anlage { Weg = true };

        var lauf = await Dosierer(anlage).RunForSecondsAsync(PhPumpe(), 3);

        Assert.Equal(Pumpenlauf.NichtGesendet, lauf);
        Assert.Equal(0, anlage.Anzahl(Pumpe, "turn_on"));
        Assert.Equal(DoseOutcome.Rejected, DosingService.Ausgang(lauf));

        // Sechs solche Versuche sperren die siebte Dosis nicht.
        var jetzt = new DateTime(2026, 9, 29, 20, 0, 0, DateTimeKind.Utc);
        var versuche = Enumerable.Range(1, 6).Select(i => new DoseEvent
        {
            PumpId = 1, TentId = 1, OccurredAtUtc = jetzt.AddMinutes(-30 * i),
            Trigger = DoseTrigger.Manual, Outcome = DosingService.Ausgang(lauf), RequestedMl = 2,
        }).ToList();
        var kontext = new DosingContext(6.4, TimeSpan.FromMinutes(1), jetzt.AddDays(-3), false,
            versuche, WaterLevelOk: null, CirculationOn: true);
        Assert.True(DosingGuard.Evaluate(PhPumpe(), 2, kontext, jetzt).Allowed);
    }

    [Fact]
    public async Task NichtKonfiguriert_NichtsGesendet()
    {
        _grows.SaveHomeAssistantSettings(new HomeAssistantSettings { Enabled = false });
        var anlage = new Anlage();
        anlage.Setzen(Pumpe, "off");

        var lauf = await Dosierer(anlage).RunForSecondsAsync(PhPumpe(), 3);

        Assert.Equal(Pumpenlauf.NichtGesendet, lauf);
        Assert.Empty(anlage.Gesendet);
    }

    /// <summary>
    /// Eine träge Wolke meldet „aus" erst nach einigen Sekunden. Das ist kein
    /// Fehlschlag — es wird nachgefragt, nicht erneut gesendet, und nicht
    /// „nicht geschlossen" gemeldet (Prüfer-Befund 29.09.2026: vorher las der
    /// Ausschalter nach zwei Sekunden einmal nach).
    /// </summary>
    [Fact]
    public async Task TraegeWolke_WirdAbgewartetStattFalschGemeldet()
    {
        var anlage = new Anlage();
        anlage.Setzen(Ventil, "off");
        anlage.SpaeteMeldung[Ventil] = 8; // acht Nachfragen lang noch „on"

        var ergebnis = await Probe(anlage).ProbierenAsync(Zuordnung(), Einstellungen);

        Assert.True(ergebnis.WiederZu);
        Assert.Equal(1, anlage.Anzahl(Ventil, "turn_off"));
    }

    [Fact]
    public async Task AuswurfBeimStart_SchaltetAuchPumpenImTestbetriebAus()
    {
        var pumpe = PhPumpe();
        pumpe.SimulationMode = true;
        _dosing.InsertPump(pumpe);
        var anlage = new Anlage();
        anlage.Setzen(Pumpe, "on");

        await Dosierer(anlage).TurnAllOffAsync();

        Assert.Equal("off", anlage.Stand(Pumpe));
    }

    [Fact]
    public async Task AusschaltenMeldetFalsch_WennDiePumpeImmerNochLaeuft()
    {
        var anlage = new Anlage();
        anlage.Setzen(Pumpe, "on");
        anlage.AusVerworfen[Pumpe] = 99;

        var aus = await Dosierer(anlage).TurnOffAsync(PhPumpe());

        Assert.False(aus);
        Assert.Equal(Ausschalter.Versuche, anlage.Anzahl(Pumpe, "turn_off"));
    }

    [Fact]
    public void UnbestaetigteDosis_ZaehltAufMischpause()
    {
        var jetzt = new DateTime(2026, 9, 29, 20, 0, 0, DateTimeKind.Utc);
        var gescheitert = new DoseEvent
        {
            PumpId = 1, TentId = 1, OccurredAtUtc = jetzt.AddMinutes(-1),
            Trigger = DoseTrigger.Automatic, Outcome = DoseOutcome.Failed,
            RequestedMl = 2, DosedMl = 0,
        };
        var kontext = new DosingContext(6.4, TimeSpan.FromMinutes(1), jetzt.AddDays(-3), false,
            [gescheitert], WaterLevelOk: null, CirculationOn: true);

        var urteil = DosingGuard.Evaluate(PhPumpe(), 2, kontext, jetzt);

        Assert.False(urteil.Allowed);
        Assert.Contains("mischen", urteil.Reason);
    }

    [Fact]
    public void UnbestaetigteDosen_ZaehlenMitDerAngefordertenMengeAufDieTagesmenge()
    {
        var jetzt = new DateTime(2026, 9, 29, 20, 0, 0, DateTimeKind.Utc);
        var pumpe = PhPumpe();
        pumpe.MaxMlPerDay = 10;
        var dosen = Enumerable.Range(1, 2).Select(i => new DoseEvent
        {
            PumpId = 1, TentId = 1, OccurredAtUtc = jetzt.AddHours(-i),
            Trigger = DoseTrigger.Automatic, Outcome = DoseOutcome.Failed,
            RequestedMl = 5, DosedMl = 0,
        }).ToList();
        var kontext = new DosingContext(6.4, TimeSpan.FromMinutes(1), jetzt.AddDays(-3), false,
            dosen, WaterLevelOk: null, CirculationOn: true);

        var urteil = DosingGuard.Evaluate(pumpe, 2, kontext, jetzt);

        Assert.False(urteil.Allowed);
        Assert.Contains("Tagesmenge", urteil.Reason);
    }

    [Fact]
    public void AbgelehnteDosis_ZaehltWeiterhinNicht()
    {
        var jetzt = new DateTime(2026, 9, 29, 20, 0, 0, DateTimeKind.Utc);
        var abgelehnt = new DoseEvent
        {
            PumpId = 1, TentId = 1, OccurredAtUtc = jetzt.AddMinutes(-1),
            Trigger = DoseTrigger.Automatic, Outcome = DoseOutcome.Rejected, RequestedMl = 2,
        };
        var kontext = new DosingContext(6.4, TimeSpan.FromMinutes(1), jetzt.AddDays(-3), false,
            [abgelehnt], WaterLevelOk: null, CirculationOn: true);

        Assert.True(DosingGuard.Evaluate(PhPumpe(), 2, kontext, jetzt).Allowed);
        Assert.Null(DosingContextBuilder.LetzteImBecken([abgelehnt]));
    }

    [Fact]
    public void UnbestaetigteDosis_ZaehltAlsLetzteImBecken()
    {
        var zeit = new DateTime(2026, 9, 29, 19, 59, 0, DateTimeKind.Utc);
        var gescheitert = new DoseEvent
        {
            PumpId = 2, TentId = 1, OccurredAtUtc = zeit,
            Trigger = DoseTrigger.Automatic, Outcome = DoseOutcome.Failed, RequestedMl = 2,
        };

        Assert.Equal(zeit, DosingContextBuilder.LetzteImBecken([gescheitert]));
    }

    [Fact]
    public void TagesabfrageLiefertAuchUnbestaetigteDosen()
    {
        var pumpId = _dosing.InsertPump(PhPumpe());
        var seit = DateTime.UtcNow.AddHours(-1);
        _dosing.InsertEvent(new DoseEvent
        {
            PumpId = pumpId, TentId = 1, OccurredAtUtc = DateTime.UtcNow.AddMinutes(-5),
            Trigger = DoseTrigger.Automatic, Outcome = DoseOutcome.Failed, RequestedMl = 2,
        });
        _dosing.InsertEvent(new DoseEvent
        {
            PumpId = pumpId, TentId = 1, OccurredAtUtc = DateTime.UtcNow.AddMinutes(-4),
            Trigger = DoseTrigger.Automatic, Outcome = DoseOutcome.Rejected, RequestedMl = 2,
        });

        var heute = _dosing.GetDosesSince(pumpId, seit);

        Assert.Equal([DoseOutcome.Failed], heute.Select(d => d.Outcome));
    }

    [Fact]
    public async Task AuswurfBeimStart_WartetBisHomeAssistantErreichbarIst()
    {
        _dosing.InsertPump(PhPumpe());
        var anlage = new Anlage { Weg = true };
        anlage.Setzen(Pumpe, "on");
        var runden = 0;
        var dosierer = new DosingService(_grows, _dosing, anlage,
            new Ausschalter(anlage, NullLogger<Ausschalter>.Instance),
            NullLogger<DosingService>.Instance,
            (dauer, _) =>
            {
                // Nach der zweiten Runden-Pause ist Home Assistant hochgefahren.
                if (dauer == DosingService.AuswurfPause && ++runden == 2) anlage.Weg = false;
                return Task.CompletedTask;
            });

        var nichtAus = await dosierer.TurnAllOffAsync();

        Assert.Empty(nichtAus);
        Assert.Equal("off", anlage.Stand(Pumpe));
    }

    [Fact]
    public async Task AuswurfBeimStart_MeldetDiePumpeDieNichtAusgeht()
    {
        _dosing.InsertPump(PhPumpe());
        var anlage = new Anlage { Weg = true };

        var nichtAus = await Dosierer(anlage).TurnAllOffAsync();

        Assert.Equal([Pumpe], nichtAus.Select(p => p.HaEntityId));
        Assert.Equal(DosingService.AuswurfRunden * Ausschalter.Versuche, anlage.Anzahl(Pumpe, "turn_off"));
    }

    // ---------- K2: CO₂-Probeschaltung ----------

    private static Dictionary<string, string> Zuordnung() => new()
    {
        ["port_schalter"] = Ventil,
        ["port_zustand"] = Ventil,
    };

    [Fact]
    public async Task ProbeOhneAntwortBeimOeffnen_SchliesstDasVentilTrotzdem()
    {
        var anlage = new Anlage();
        anlage.Setzen(Ventil, "off");
        anlage.AntwortZuSpaet.Add(Ventil);

        var ergebnis = await Probe(anlage).ProbierenAsync(Zuordnung(), Einstellungen);

        Assert.False(ergebnis.Geschaltet);
        Assert.True(anlage.Anzahl(Ventil, "turn_off") >= 1, "Nach einem unbestätigten Öffnen kam kein „zu\".");
        Assert.Equal("off", anlage.Stand(Ventil));
        Assert.True(ergebnis.WiederZu);
    }

    [Fact]
    public async Task ProbeMitVerworfenemZu_WiederholtBisDasVentilZuIst()
    {
        var anlage = new Anlage();
        anlage.Setzen(Ventil, "off");
        anlage.AusVerworfen[Ventil] = 2;

        var ergebnis = await Probe(anlage).ProbierenAsync(Zuordnung(), Einstellungen);

        Assert.True(ergebnis.Geschaltet);
        Assert.Equal(3, anlage.Anzahl(Ventil, "turn_off"));
        Assert.Equal("off", anlage.Stand(Ventil));
        Assert.True(ergebnis.WiederZu);
    }

    [Fact]
    public async Task ProbeDieNichtZuGeht_SagtEsDeutlich()
    {
        var anlage = new Anlage();
        anlage.Setzen(Ventil, "off");
        anlage.AntwortZuSpaet.Add(Ventil);
        anlage.AusVerworfen[Ventil] = 99;

        var ergebnis = await Probe(anlage).ProbierenAsync(Zuordnung(), Einstellungen);

        Assert.False(ergebnis.WiederZu);
        Assert.Contains("von Hand", ergebnis.Urteil);
    }

    [Fact]
    public async Task ProbeAmSelectPort_SchliesstMitOptionOff()
    {
        const string port = "select.co2_port_modus";
        var anlage = new Anlage();
        anlage.Setzen(port, "off");
        anlage.AntwortZuSpaet.Add(port);

        await Probe(anlage).ProbierenAsync(
            new Dictionary<string, string> { ["port_schalter"] = port }, Einstellungen);

        Assert.True(anlage.Anzahl(port, "select_option") >= 2, "Am select-Port wurde nicht mit select_option geschlossen.");
        Assert.Equal("off", anlage.Stand(port));
    }
}
