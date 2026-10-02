using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Tests.TestFakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Die Licht-Steuerung schreibt keine alten Befehle nach und setzt keinen
/// Zeitplan ohne seine Zeiten.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (29.09.2026).</b> Zwei Wege an denselben Controller:</para>
/// <list type="bullet">
/// <item>Ein „aus", das die AC-Infinity-Wolke verwarf, blieb ohne Frist im
/// Speicher. Das nächste Öffnen der Seite schrieb es nach — auch Tage später,
/// mitten in der Lichtphase, nachdem das Licht in der AC-App längst wieder an
/// war.</item>
/// <item>Ein Preset schrieb Ein-Zeit, Aus-Zeit und Modus der Reihe nach, ohne
/// auf das Ergebnis zu sehen. Kam die Ein-Zeit nicht an, stand der Controller
/// trotzdem auf „Schedule" — und schaltete nach der alten Zeit.</item>
/// </list>
/// </remarks>
public sealed class LichtNachschreibenTests : IDisposable
{
    private const string Modus = "select.licht_modus";
    private const string Ein = "time.licht_ein";
    private const string Aus = "time.licht_aus";

    private static readonly DateTime Jetzt = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
    private static readonly LichtEinstellungen Vorgabe = new(); // 20 s Prüffrist, 2 Wiederholungen

    private readonly string _wurzel;
    private readonly AppPaths _pfade;

    public LichtNachschreibenTests()
    {
        _wurzel = Path.Combine(Path.GetTempPath(), "LichtNach_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_wurzel);
        _pfade = new AppPaths(_wurzel);
        TestDatabase.Initialize(_pfade);
    }

    public void Dispose()
    {
        try { Directory.Delete(_wurzel, recursive: true); } catch { /* Windows haelt manchmal fest */ }
    }

    // ---------- H1: kein Nachschreiben alter Befehle ----------

    private static LichtOffen Offen(string soll = "Off", string? vorher = "Schedule", int versuche = 1,
        TimeSpan? seit = null, TimeSpan? erst = null)
        => new(Modus, soll, Jetzt - (seit ?? TimeSpan.FromSeconds(30)), versuche,
            Jetzt - (erst ?? seit ?? TimeSpan.FromSeconds(30)), vorher);

    [Fact]
    public void AlterBefehl_WirdNieMehrNachgeschrieben()
    {
        // „aus" um 22:00, verworfen; die Seite wird am nächsten Mittag geöffnet.
        var offen = Offen(erst: TimeSpan.FromHours(14), seit: TimeSpan.FromHours(14));

        Assert.Equal(LichtSteuerungService.Nachpruefschritt.Aufgegeben,
            LichtSteuerungService.Nachpruefung(offen, "Schedule", Jetzt, Vorgabe));
    }

    [Fact]
    public void AnderswoGeschaltet_WirdNichtUeberschrieben()
    {
        // Befehl „aus" bei Stand „Schedule"; inzwischen in der AC-App auf „On".
        var offen = Offen();

        Assert.Equal(LichtSteuerungService.Nachpruefschritt.Ueberholt,
            LichtSteuerungService.Nachpruefung(offen, "On", Jetzt, Vorgabe));
    }

    [Fact]
    public void Unerreichbar_IstKeinFremdesSchalten()
    {
        Assert.Equal(LichtSteuerungService.Nachpruefschritt.Nachschreiben,
            LichtSteuerungService.Nachpruefung(Offen(), "unavailable", Jetzt, Vorgabe));
    }

    [Fact]
    public void Selbsttest_ImFensterWirdWeiterhinNachgeschrieben()
    {
        // Ohne diesen Fall wäre „nie nachschreiben" auch grün.
        Assert.Equal(LichtSteuerungService.Nachpruefschritt.Nachschreiben,
            LichtSteuerungService.Nachpruefung(Offen(), "Schedule", Jetzt, Vorgabe));
    }

    [Fact]
    public void Uebernommen_IstErledigt()
    {
        Assert.Equal(LichtSteuerungService.Nachpruefschritt.Erledigt,
            LichtSteuerungService.Nachpruefung(Offen(), "Off", Jetzt, Vorgabe));
    }

    [Fact]
    public void InDerPrueffrist_WirdGewartet()
    {
        Assert.Equal(LichtSteuerungService.Nachpruefschritt.Warten,
            LichtSteuerungService.Nachpruefung(Offen(seit: TimeSpan.FromSeconds(5)), "Schedule", Jetzt, Vorgabe));
    }

    [Fact]
    public void VersucheAufgebraucht_WirdAufgegeben()
    {
        Assert.Equal(LichtSteuerungService.Nachpruefschritt.Aufgegeben,
            LichtSteuerungService.Nachpruefung(Offen(versuche: 3), "Schedule", Jetzt, Vorgabe));
    }

    [Fact]
    public void EintragOhneBefehlszeit_LaeuftTrotzdemAus()
    {
        var alt = new LichtOffen(Modus, "Off", Jetzt - TimeSpan.FromDays(2), 1);

        Assert.Equal(LichtSteuerungService.Nachpruefschritt.Aufgegeben,
            LichtSteuerungService.Nachpruefung(alt, "Schedule", Jetzt, Vorgabe));
    }

    [Fact]
    public void Nachschreibfenster_SindMitDenVorgabenZweiMinuten()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), LichtSteuerungService.Nachschreibfenster(Vorgabe));
    }

    // ---------- H2: kein Zeitplan ohne seine Zeiten ----------

    /// <summary>Ein Controller, dessen Wolke bestimmte Entitäten still verwirft.</summary>
    private sealed class Wolke : IAcFunk
    {
        private readonly Dictionary<string, string> _stand = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Verwirft { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Gesendet { get; } = new();

        public void Setzen(string entityId, string zustand) => _stand[entityId] = zustand;
        public string? Stand(string entityId) => _stand.GetValueOrDefault(entityId);

        public Task<HomeAssistantState?> ZustandAsync(HomeAssistantSettings einstellungen, string entityId, CancellationToken ct)
            => Task.FromResult(_stand.TryGetValue(entityId, out var z)
                ? new HomeAssistantState { EntityId = entityId, State = z }
                : null);

        public Task<bool> SchickenAsync(HomeAssistantSettings einstellungen, string domain, string dienst, string entityId,
            IReadOnlyDictionary<string, object> daten, CancellationToken ct)
        {
            Gesendet.Add(entityId);
            if (!Verwirft.Contains(entityId))
            {
                var wert = daten.GetValueOrDefault("option") ?? daten.GetValueOrDefault("time") ?? daten.GetValueOrDefault("value");
                _stand[entityId] = Convert.ToString(wert, System.Globalization.CultureInfo.InvariantCulture) ?? "";
            }
            return Task.FromResult(true);
        }
    }

    private LichtSteuerungService Licht(Wolke wolke, List<TimeSpan>? wartezeiten = null, LichtEinstellungen? einstellungen = null)
    {
        var steuerung = new SteuerungRepository(_pfade);
        if (einstellungen is not null) steuerung.SetEinstellungen(LichtSteuerungService.Modul, einstellungen);
        steuerung.SetGeraet(LichtSteuerungService.Modul, LichtSteuerungService.Rollen.Modus, Modus);
        steuerung.SetGeraet(LichtSteuerungService.Modul, LichtSteuerungService.Rollen.EinZeit, Ein);
        steuerung.SetGeraet(LichtSteuerungService.Modul, LichtSteuerungService.Rollen.AusZeit, Aus);
        var haSettings = new HomeAssistantSettingsRepository(_pfade);
        haSettings.SaveHomeAssistantSettings(new HomeAssistantSettings
        {
            BaseUrl = "http://ha.local:8123", AccessToken = "token", Enabled = true,
        });

        var keinNetz = new HomeAssistantService(
            new StubHttpClientFactory(new RecordingHttpHandler((_, _) => throw new InvalidOperationException("Kein Netz im Test."))),
            NullLogger<HomeAssistantService>.Instance);

        return new LichtSteuerungService(
            steuerung, keinNetz, wolke, new AcSchreiber(wolke, NullLogger<AcSchreiber>.Instance),
            haSettings, new SteuerungGeraeteService(steuerung), NullLogger<LichtSteuerungService>.Instance,
            (dauer, _) => { wartezeiten?.Add(dauer); return Task.CompletedTask; });
    }

    [Fact]
    public async Task PresetMitVerworfenerEinZeit_SetztKeinenZeitplan()
    {
        var wolke = new Wolke();
        wolke.Setzen(Modus, "On");
        wolke.Setzen(Ein, "08:00:00");
        wolke.Setzen(Aus, "20:00:00");
        wolke.Verwirft.Add(Ein);

        var ok = await Licht(wolke).BefehlAsync("preset", "bluete", null, CancellationToken.None);

        Assert.False(ok);
        Assert.DoesNotContain(Modus, wolke.Gesendet);
        Assert.Equal("On", wolke.Stand(Modus));
    }

    [Fact]
    public async Task PresetOhneStoerung_SchreibtZeitenUndDannDenZeitplan()
    {
        var wolke = new Wolke();
        wolke.Setzen(Modus, "On");
        wolke.Setzen(Ein, "08:00:00");
        wolke.Setzen(Aus, "20:00:00");

        var ok = await Licht(wolke).BefehlAsync("preset", "bluete", null, CancellationToken.None);

        Assert.True(ok);
        Assert.Equal([Ein, Aus, Modus], wolke.Gesendet.Distinct());
        Assert.Equal(LichtSteuerungService.Modi.Zeitplan, wolke.Stand(Modus));
        Assert.Equal(Vorgabe.BlueteEin + ":00", wolke.Stand(Ein));
    }

    /// <summary>
    /// Die Felder „Abstand zwischen Befehlen", „Prüfen nach" und
    /// „Wiederholungen" wirken auf das Preset. Nach dem Umbau auf den
    /// AcSchreiber galten zunächst dessen feste Werte — die Felder standen auf
    /// der Seite und bewirkten nichts (Prüfer-Befund 29.09.2026).
    /// </summary>
    [Fact]
    public async Task Preset_BenutztAbstandUndWiederholungenAusDenEinstellungen()
    {
        var wolke = new Wolke();
        wolke.Setzen(Modus, "On");
        wolke.Setzen(Ein, "08:00:00");
        wolke.Setzen(Aus, "20:00:00");
        wolke.Verwirft.Add(Aus);
        var wartezeiten = new List<TimeSpan>();
        var einstellungen = new LichtEinstellungen { SchreibAbstandMs = 3500, VerifySekunden = 7, MaxWiederholungen = 0 };

        await Licht(wolke, wartezeiten, einstellungen).BefehlAsync("preset", "bluete", null, CancellationToken.None);

        Assert.Contains(TimeSpan.FromMilliseconds(3500), wartezeiten);
        // Keine Wiederholung: die verworfene Aus-Zeit wird genau einmal gesendet.
        Assert.Equal(1, wolke.Gesendet.Count(e => e == Aus));
        // Die Prüffrist: 7 Nachfragen im Sekundentakt nach dem einen Versuch.
        Assert.Equal(7, wartezeiten.Count(w => w == AcSchreiber.Nachfragetakt) - 1);
    }

    // ---------- Fork AI (02.10.2026): ein neuer Befehl ersetzt den offenen ----------

    /// <summary>Die offenen Sollwerte des Dienstes — statisch und privat, deshalb über Reflexion.</summary>
    private static System.Collections.Concurrent.ConcurrentDictionary<string, LichtOffen> Offene()
        => (System.Collections.Concurrent.ConcurrentDictionary<string, LichtOffen>)typeof(LichtSteuerungService)
            .GetField("Offene", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;

    /// <summary>
    /// Licht an, „aus" gesendet und verworfen, dann „an" — das stand schon so.
    /// </summary>
    /// <remarks>
    /// Vorher kehrte „an" sofort zurück, ohne den offenen „aus"-Eintrag zu
    /// entfernen. Das nächste Öffnen der Seite hätte „aus" nachgeschrieben —
    /// das Licht ging mitten in der Lichtphase aus, obwohl der letzte Befehl
    /// „an" war. Hier sichtbar daran, dass der Modus noch als „unbestätigt"
    /// gilt: genau der Eintrag, aus dem das Nachschreiben folgt.
    /// </remarks>
    [Fact]
    public async Task NeuerBefehl_AufDemSchonStehendenSoll_RaeumtDenVeraltetenAuf()
    {
        Offene().TryRemove(Modus, out _);
        var wolke = new Wolke();
        wolke.Setzen(Modus, LichtSteuerungService.Modi.An);
        wolke.Verwirft.Add(Modus);
        var licht = Licht(wolke);

        await licht.BefehlAsync("aus", null, null, CancellationToken.None);
        Assert.True(Offene().ContainsKey(Modus), "Selbsttest: das verworfene „aus\" muss offen sein.");

        wolke.Verwirft.Clear();
        var ok = await licht.BefehlAsync("an", null, null, CancellationToken.None);
        var live = await licht.LiveAsync(CancellationToken.None);

        Assert.True(ok);
        Assert.False(Offene().ContainsKey(Modus), "Der veraltete „aus\"-Befehl liegt noch zum Nachschreiben bereit.");
        Assert.DoesNotContain(Modus, live.Unbestaetigt);
    }

    /// <summary>
    /// Kommt während des Nachschreibens ein neuer Befehl, gewinnt der neue.
    /// </summary>
    /// <remarks>
    /// Vorher schrieb die Nachprüfung ihren Eintrag nach dem Senden blind
    /// zurück und überschrieb damit einen parallel gesetzten neuen — der alte
    /// Soll lebte weiter.
    /// </remarks>
    [Fact]
    public async Task NeuerBefehlWaehrendDesNachschreibens_WirdNichtUeberschrieben()
    {
        var offene = Offene();
        offene.TryRemove(Modus, out _);
        var vor30s = DateTime.UtcNow - TimeSpan.FromSeconds(30);
        var alt = new LichtOffen(Modus, LichtSteuerungService.Modi.Aus, vor30s, 1, vor30s, LichtSteuerungService.Modi.An);
        offene[Modus] = alt;

        var neu = new LichtOffen(Modus, LichtSteuerungService.Modi.An, DateTime.UtcNow, 1, DateTime.UtcNow, LichtSteuerungService.Modi.An);
        var wolke = new NachschreibWolke(() => offene[Modus] = neu);

        // Home Assistant ist im Test nicht erreichbar: der Ist-Stand ist
        // unbekannt, die Prüffrist ist um — die Nachprüfung schreibt nach.
        await LichtMit(wolke).LiveAsync(CancellationToken.None);

        Assert.Equal(1, wolke.Gesendet);
        Assert.True(offene.TryGetValue(Modus, out var danach));
        Assert.Equal(neu, danach);
        offene.TryRemove(Modus, out _);
    }

    /// <summary>Eine Wolke, die beim Senden einen parallelen Befehl auslöst.</summary>
    private sealed class NachschreibWolke : IAcFunk
    {
        private readonly Action _beimSenden;
        public int Gesendet { get; private set; }

        public NachschreibWolke(Action beimSenden) => _beimSenden = beimSenden;

        public Task<HomeAssistantState?> ZustandAsync(HomeAssistantSettings einstellungen, string entityId, CancellationToken ct)
            => Task.FromResult<HomeAssistantState?>(null);

        public Task<bool> SchickenAsync(HomeAssistantSettings einstellungen, string domain, string dienst, string entityId,
            IReadOnlyDictionary<string, object> daten, CancellationToken ct)
        {
            Gesendet++;
            _beimSenden();
            return Task.FromResult(true);
        }
    }

    private LichtSteuerungService LichtMit(IAcFunk funk)
    {
        var steuerung = new SteuerungRepository(_pfade);
        steuerung.SetGeraet(LichtSteuerungService.Modul, LichtSteuerungService.Rollen.Modus, Modus);
        var haSettings = new HomeAssistantSettingsRepository(_pfade);
        haSettings.SaveHomeAssistantSettings(new HomeAssistantSettings
        {
            BaseUrl = "http://ha.local:8123", AccessToken = "token", Enabled = true,
        });
        var keinNetz = new HomeAssistantService(
            new StubHttpClientFactory(new RecordingHttpHandler((_, _) => throw new InvalidOperationException("Kein Netz im Test."))),
            NullLogger<HomeAssistantService>.Instance);
        return new LichtSteuerungService(
            steuerung, keinNetz, funk, new AcSchreiber(funk, NullLogger<AcSchreiber>.Instance),
            haSettings, new SteuerungGeraeteService(steuerung), NullLogger<LichtSteuerungService>.Instance,
            (_, _) => Task.CompletedTask);
    }
}
