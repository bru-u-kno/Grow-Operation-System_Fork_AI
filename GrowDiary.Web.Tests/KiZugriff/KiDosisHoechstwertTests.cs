using System.Net;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge;
using GrowDiary.Web.Tests.TestFakes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.KiZugriff;

/// <summary>
/// Fork AI (A-003, 03.10.2026): Der KI-Höchstwert je Dosierbefehl — am echten
/// Controller, mit echter Datenbank und Pumpen im Testbetrieb.
/// </summary>
/// <remarks>
/// <para>Ob die Pumpe lief, steht nicht im Rückgabewert, sondern in der
/// Wartezeit, die <see cref="DosingService"/> im Testbetrieb abwartet: jede
/// wird hier mitgeschrieben statt abgewartet. Kein Eintrag heisst, es lief
/// nichts.</para>
/// <para>Ob eine Anfrage über einen Schlüssel kam, sagt allein der
/// <see cref="KiZugriffKontext"/> in <see cref="HttpContext.Items"/> — genau
/// das, was die Sperre vor dem Routing hineinlegt.</para>
/// </remarks>
public sealed class KiDosisHoechstwertTests : IDisposable
{
    private const double KiGrenzeMl = 10;

    private readonly string _wurzel;
    private readonly AppPaths _pfade;
    private readonly GrowRepository _grows;
    private readonly DosingRepository _dosing;
    private readonly int _zelt;
    private readonly List<TimeSpan> _laeufe = new();
    private readonly DosingService _dienst;

    public KiDosisHoechstwertTests()
    {
        _wurzel = Path.Combine(Path.GetTempPath(), "KiDosis_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_wurzel);
        _pfade = new AppPaths(_wurzel);
        _zelt = TestDatabase.InitializeWithDefaultTent(_pfade).Id;
        _grows = new GrowRepository(_pfade);
        _dosing = new DosingRepository(_pfade);

        var funk = new KeinFunk();
        _dienst = new DosingService(_grows, _dosing, funk,
            new Ausschalter(funk, NullLogger<Ausschalter>.Instance),
            NullLogger<DosingService>.Instance,
            (dauer, _) => { _laeufe.Add(dauer); return Task.CompletedTask; });
    }

    public void Dispose()
    {
        try { Directory.Delete(_wurzel, recursive: true); } catch { /* Windows haelt manchmal fest */ }
    }

    // ---------------------------------------------------------- Dosierbefehl

    [Fact]
    public async Task UeberDerKiGrenze_422_NichtsLaeuft_UndDasNeinStehtImProtokoll()
    {
        var pumpe = Pumpe();

        var controller = Controller(mitSchluessel: true);
        var antwort = await controller.Dose(pumpe.Id, new ManualDoseRequest { Ml = 12 }, CancellationToken.None);

        var fehler = Abgewiesen(antwort.Result);
        Assert.Equal("ki_hoechstwert", fehler.Code);
        // Fork AI (A-003, 03.10.2026): vorgemerkt fürs Prüfprotokoll — dort heisst es
        // dann „Höchstwert erreicht" und nicht bloss „422".
        Assert.Equal("ki_hoechstwert", KiZugriffSperre.GemerkterFehlercode(controller.HttpContext));
        Assert.Contains("12 ml", fehler.Message);
        Assert.Contains("10 ml", fehler.Message);
        Assert.True(_laeufe.Count == 0,
            $"Die Pumpe lief {string.Join(", ", _laeufe)} — über der KI-Grenze darf nichts laufen.");

        var zeile = Assert.Single(_dosing.GetEvents(pumpId: pumpe.Id));
        Assert.Equal(DoseTrigger.Manual, zeile.Trigger);
        Assert.Equal(DoseOutcome.Rejected, zeile.Outcome);
        Assert.Equal(12, zeile.RequestedMl);
        Assert.Equal(0, zeile.DosedMl);
        Assert.Equal(fehler.Message, zeile.Reason);
    }

    [Fact]
    public async Task GenauAufDerKiGrenze_Laeuft()
    {
        var pumpe = Pumpe();

        var antwort = await Controller(mitSchluessel: true).Dose(pumpe.Id, new ManualDoseRequest { Ml = KiGrenzeMl }, CancellationToken.None);

        var ergebnis = Gelaufen(antwort.Result);
        Assert.Equal(KiGrenzeMl, ergebnis.Ml, 2);
        // 60 ml/min: 10 ml sind 10 s.
        Assert.Equal(TimeSpan.FromSeconds(10), Assert.Single(_laeufe));
        Assert.Contains(_dosing.GetEvents(pumpId: pumpe.Id), e => e.Outcome == DoseOutcome.Done && e.DosedMl == KiGrenzeMl);
    }

    [Fact]
    public async Task OhneSchluessel_GiltDieKiGrenzeNicht()
    {
        var pumpe = Pumpe();

        var antwort = await Controller(mitSchluessel: false).Dose(pumpe.Id, new ManualDoseRequest { Ml = 12 }, CancellationToken.None);

        var ergebnis = Gelaufen(antwort.Result);
        Assert.Equal(12, ergebnis.Ml, 2);
        Assert.Equal(TimeSpan.FromSeconds(12), Assert.Single(_laeufe));
    }

    /// <summary>Unter der KI-Grenze deckelt weiter die Pumpe — beide Grenzen gelten.</summary>
    [Fact]
    public async Task UnterDerKiGrenze_DeckeltWeiterDieGrenzeDerPumpe()
    {
        var pumpe = Pumpe(maxEinzeldosisMl: 5);

        var antwort = await Controller(mitSchluessel: true).Dose(pumpe.Id, new ManualDoseRequest { Ml = 8 }, CancellationToken.None);

        var ergebnis = Gelaufen(antwort.Result);
        Assert.Equal(5, ergebnis.Ml, 2);
        Assert.Equal(TimeSpan.FromSeconds(5), Assert.Single(_laeufe));
    }

    /// <summary>
    /// Die zweite Hälfte eines Zweikomponenten-Paares zählt mit: 6 ml A zögen
    /// bei 1:2 zwölf ml B nach.
    /// </summary>
    [Fact]
    public async Task ZweiteHaelfteUeberDerKiGrenze_422_UndNichtsWirdEingeplant()
    {
        var b = Pumpe(name: "Dünger B");
        var a = Pumpe(name: "Dünger A");
        a.PartnerPumpId = b.Id;
        a.PartnerRatio = 2;
        a.PartnerDelayMinutes = 5;
        _dosing.UpdatePump(a);

        var antwort = await Controller(mitSchluessel: true).Dose(a.Id, new ManualDoseRequest { Ml = 6 }, CancellationToken.None);

        var fehler = Abgewiesen(antwort.Result);
        Assert.Contains("12 ml", fehler.Message);
        Assert.Contains("Dünger B", fehler.Message);
        Assert.Empty(_laeufe);
        Assert.Empty(_dosing.GetPendingForPump(b.Id));
        Assert.Equal(DoseOutcome.Rejected, Assert.Single(_dosing.GetEvents(pumpId: a.Id)).Outcome);
    }

    /// <summary>Die Gegenprobe: ohne Schlüssel plant dasselbe Paar B ein wie immer.</summary>
    [Fact]
    public async Task ZweiteHaelfteOhneSchluessel_WirdWieImmerEingeplant()
    {
        var b = Pumpe(name: "Dünger B");
        var a = Pumpe(name: "Dünger A");
        a.PartnerPumpId = b.Id;
        a.PartnerRatio = 2;
        a.PartnerDelayMinutes = 5;
        _dosing.UpdatePump(a);

        var antwort = await Controller(mitSchluessel: false).Dose(a.Id, new ManualDoseRequest { Ml = 6 }, CancellationToken.None);

        Gelaufen(antwort.Result);
        Assert.Equal(12, Assert.Single(_dosing.GetPendingForPump(b.Id)).Ml, 2);
    }

    // -------------------------------------------------------- Kalibrierlauf

    [Fact]
    public async Task KalibrierlaufUeberDerKiGrenze_422_NichtsLaeuft()
    {
        var pumpe = Pumpe();

        // 30 s bei 60 ml/min sind 30 ml.
        var antwort = await Controller(mitSchluessel: true).CalibrationRun(pumpe.Id, new CalibrationRunRequest { Seconds = 30 }, CancellationToken.None);

        var fehler = Abgewiesen(antwort.Result);
        Assert.Contains("30 ml", fehler.Message);
        Assert.Contains("10 ml", fehler.Message);
        Assert.Empty(_laeufe);
        var zeile = Assert.Single(_dosing.GetEvents(pumpId: pumpe.Id));
        Assert.Equal(DoseTrigger.Calibration, zeile.Trigger);
        Assert.Equal(DoseOutcome.Rejected, zeile.Outcome);
    }

    [Fact]
    public async Task KalibrierlaufGenauAufDerKiGrenze_Laeuft()
    {
        var pumpe = Pumpe();

        var antwort = await Controller(mitSchluessel: true).CalibrationRun(pumpe.Id, new CalibrationRunRequest { Seconds = 10 }, CancellationToken.None);

        Gelaufen(antwort.Result);
        Assert.Equal(TimeSpan.FromSeconds(10), Assert.Single(_laeufe));
    }

    /// <summary>Ohne Fördermenge lässt sich ein Lauf nicht in ml begrenzen — über einen Schlüssel läuft er dann nicht.</summary>
    [Fact]
    public async Task KalibrierlaufEinerUnkalibriertenPumpe_UeberSchluessel_422()
    {
        var pumpe = Pumpe();
        pumpe.MlPerMinute = null;
        _dosing.UpdatePump(pumpe);

        var antwort = await Controller(mitSchluessel: true).CalibrationRun(pumpe.Id, new CalibrationRunRequest { Seconds = 5 }, CancellationToken.None);

        Assert.Equal("ki_hoechstwert", Abgewiesen(antwort.Result).Code);
        Assert.Empty(_laeufe);
    }

    [Fact]
    public async Task KalibrierlaufOhneSchluessel_LaeuftWieImmer()
    {
        var pumpe = Pumpe();

        var antwort = await Controller(mitSchluessel: false).CalibrationRun(pumpe.Id, new CalibrationRunRequest { Seconds = 30 }, CancellationToken.None);

        Gelaufen(antwort.Result);
        Assert.Equal(TimeSpan.FromSeconds(30), Assert.Single(_laeufe));
    }

    // ----------------------------------------------------------- Innenleben

    private static ApiError Abgewiesen(ActionResult? ergebnis)
    {
        var objekt = Assert.IsType<ObjectResult>(ergebnis);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, objekt.StatusCode);
        var fehler = Assert.IsType<ApiError>(objekt.Value);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, fehler.Status);
        return fehler;
    }

    private static DoseResultDto Gelaufen(ActionResult? ergebnis)
    {
        var ok = Assert.IsType<OkObjectResult>(ergebnis);
        var dto = Assert.IsType<DoseResultDto>(ok.Value);
        Assert.True(dto.Dosed, $"Nichts gelaufen: {dto.Reason}");
        return dto;
    }

    /// <summary>Eine Pumpe im Testbetrieb, 60 ml/min — eine Sekunde ist ein Milliliter.</summary>
    private DosingPump Pumpe(double maxEinzeldosisMl = 50, string name = "pH-Minus")
    {
        var pumpe = new DosingPump
        {
            TentId = _zelt, Name = name, Purpose = DosingPurpose.PhDown,
            SimulationMode = true, MlPerMinute = 60,
            MaxSingleDoseMl = maxEinzeldosisMl, MaxMlPerDay = 200, MaxDosesPerDay = 10,
        };
        pumpe.Id = _dosing.InsertPump(pumpe);
        return _dosing.GetPump(pumpe.Id)!;
    }

    private DosingApiController Controller(bool mitSchluessel)
    {
        var http = new DefaultHttpContext();
        if (mitSchluessel)
        {
            http.Items[KiZugriffKontext.ItemKey] = new KiZugriffKontext(
                1, "Testassistent", KiStufe.GeraeteSchalten, new KiHoechstwerteDto(KiGrenzeMl, 20));
        }

        var bauer = new DosingContextBuilder(_grows, _dosing, new SensorReadingRepository(_pfade), new AlertRuleRepository(_pfade),
            new TargetValueService(new KnowledgeBaseLoader(_pfade, NullLogger<KnowledgeBaseLoader>.Instance)),
            new HydroSetupRepository(_pfade, new TentRepository(_pfade)));
        var ha = new HomeAssistantService(
            new StubHttpClientFactory(new RecordingHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound))),
            NullLogger<HomeAssistantService>.Instance);

        return new DosingApiController(_grows, _dosing, new AlertRuleRepository(_pfade), _dienst, bauer, ha)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    /// <summary>Im Testbetrieb wird nie gefunkt — jeder Aufruf hier wäre ein Fehler im Aufbau.</summary>
    private sealed class KeinFunk : IAcFunk
    {
        public Task<HomeAssistantState?> ZustandAsync(HomeAssistantSettings einstellungen, string entityId, CancellationToken ct)
            => throw new InvalidOperationException("Im Testbetrieb darf nichts gefunkt werden.");

        public Task<bool> SchickenAsync(
            HomeAssistantSettings einstellungen, string domain, string dienst, string entityId,
            IReadOnlyDictionary<string, object> daten, CancellationToken ct)
            => throw new InvalidOperationException("Im Testbetrieb darf nichts gefunkt werden.");
    }
}
