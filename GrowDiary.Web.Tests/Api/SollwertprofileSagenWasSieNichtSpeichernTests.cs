using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Sollwert-Profile: was angenommen wird, wirkt — und was nicht, wird gesagt.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (02.09.2026).</b> <c>SetpointProfilesApiController</c>
/// stand bei <b>0 %</b> Abdeckung. Aus diesen Profilen kommt jedes Zielband der
/// App, und <c>waterTempNightC</c> geht über die Nachtabsenkung an das
/// Zielgerät in Home Assistant — also an den <b>echten Kühler</b> im Zelt.</para>
///
/// <para>Es gibt <c>SollwertProfilNimmtKeinenUnsinnTests</c>, aber das prüft
/// die reine Funktion <c>SetpointProfilGrenzen.Pruefe</c>. Hier geht es um den
/// Weg dorthin — und um <c>Clean</c>, das <b>still wegwirft</b>, was nicht in
/// die Tabelle gehört.</para>
///
/// <para><b>Der teure Fall ist nicht die Ablehnung, sondern die stille
/// Annahme.</b> Wer eine Phase falsch schreibt („Bluete" statt „Flower"),
/// bekommt HTTP 201 und ein Profil, in dem seine Werte nicht stehen. Er hat
/// eingetragen, gespeichert, eine Bestätigung gesehen — und nichts ist
/// passiert.</para>
/// </remarks>
public sealed class SollwertprofileSagenWasSieNichtSpeichernTests : IDisposable
{
    private readonly string _wurzel;
    private readonly AppPaths _pfade;

    public SollwertprofileSagenWasSieNichtSpeichernTests()
    {
        _wurzel = Path.Combine(Path.GetTempPath(), "Sollwerte_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_wurzel);
        KopiereWissen();
        _pfade = new AppPaths(_wurzel);
        TestDatabase.Initialize(_pfade);
    }

    public void Dispose()
    {
        try { Directory.Delete(_wurzel, recursive: true); } catch { /* Windows haelt manchmal fest */ }
    }

    /// <summary>Ein gewöhnliches Profil geht durch und steht danach da.</summary>
    /// <remarks>
    /// Mengenwächter für alles darunter: liesse der Endpunkt gar nichts durch,
    /// bestünden alle Ablehnungsfälle — und die Seite wäre unbrauchbar.
    /// </remarks>
    [Fact]
    public void EinGewoehnlichesProfil_GehtDurchUndStehtDanachDa()
    {
        var endpunkt = Endpunkt();

        var antwort = endpunkt.Create(new SetpointProfileUpsertRequest
        {
            Name = "Mein RDWC",
            BaseProfileId = "rdwc-default",
            Overrides = new() { ["Flower"] = new() { ["phMin"] = 5.6, ["phMax"] = 6.0 } },
        });

        Assert.IsType<CreatedAtActionResult>(antwort.Result);

        var alle = Assert.IsType<OkObjectResult>(endpunkt.GetAll().Result).Value
            as IReadOnlyList<SetpointProfileDto>;
        Assert.True(alle!.Any(p => p.Name == "Mein RDWC"),
            "Das Profil wurde angelegt und steht danach nicht in der Liste.");
    }

    /// <summary>
    /// Eine <b>unbekannte Phase</b> wird nicht still weggeworfen.
    /// </summary>
    /// <remarks>
    /// <para>Der Kommentar an <c>Clean</c> sagt es selbst: „sie aber zu
    /// speichern hiesse, dem Nutzer eine Änderung zu bestätigen, die nie
    /// wirkt". Genau das passiert aber — die Werte werden weggeworfen und der
    /// Endpunkt antwortet mit 201.</para>
    ///
    /// <para>Für den Nutzer: er trägt Werte für seine Blütephase ein, schreibt
    /// die Phase deutsch, bekommt „Gespeichert" und hat ein leeres Profil. Beim
    /// nächsten Öffnen sind seine Zahlen weg, ohne ein Wort.</para>
    /// </remarks>
    [Theory]
    [InlineData("Bluete")]
    [InlineData("Blüte")]
    [InlineData("Vegetation")]
    public void EineUnbekanntePhase_WirdNichtStillWeggeworfen(string phase)
    {
        var antwort = Endpunkt().Create(new SetpointProfileUpsertRequest
        {
            Name = "Mit Tippfehler",
            BaseProfileId = "rdwc-default",
            Overrides = new() { [phase] = new() { ["phMin"] = 5.6, ["phMax"] = 6.0 } },
        });

        Assert.True(antwort.Result is not CreatedAtActionResult,
            $"„{phase}\" ist keine bekannte Phase. Die Werte wurden weggeworfen, und der Nutzer "
            + "bekam trotzdem eine Bestaetigung — beim naechsten Oeffnen sind seine Zahlen weg, "
            + "ohne ein Wort.");
    }

    /// <summary>Ein unbekanntes Feld ebenso wenig.</summary>
    /// <remarks>
    /// Dieselbe Klasse: <c>ecMinimum</c> statt <c>ecMin</c> ist ein Tippfehler,
    /// den niemand bemerkt — die Zahl steht im Formular, sie verschwindet beim
    /// Speichern.
    /// </remarks>
    [Fact]
    public void EinUnbekanntesFeld_WirdNichtStillWeggeworfen()
    {
        var antwort = Endpunkt().Create(new SetpointProfileUpsertRequest
        {
            Name = "Mit Tippfehler",
            BaseProfileId = "rdwc-default",
            Overrides = new() { ["Flower"] = new() { ["ecMinimum"] = 1.2 } },
        });

        Assert.True(antwort.Result is not CreatedAtActionResult,
            "„ecMinimum\" gibt es nicht (es heisst ecMin). Der Wert wurde weggeworfen und der "
            + "Nutzer bekam eine Bestaetigung.");
    }

    /// <summary>Ein Profil ohne Namen wird abgelehnt.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void OhneNamen_WirdAbgelehnt(string name)
    {
        var antwort = Endpunkt().Create(new SetpointProfileUpsertRequest
        {
            Name = name, BaseProfileId = "rdwc-default",
        });

        Assert.True(antwort.Result is BadRequestObjectResult,
            "Ein Profil ohne Namen ging durch — in der Auswahlliste steht danach eine leere Zeile.");
    }

    /// <summary>Ein erfundenes Grundprofil wird abgelehnt.</summary>
    [Fact]
    public void EinErfundenesGrundprofil_WirdAbgelehnt()
    {
        var antwort = Endpunkt().Create(new SetpointProfileUpsertRequest
        {
            Name = "Fantasie", BaseProfileId = "gibt-es-nicht",
        });

        Assert.True(antwort.Result is BadRequestObjectResult,
            "Ein Profil auf einem Grundprofil, das es nicht gibt. Danach faellt jede Ableitung "
            + "auf den Standard zurueck — still.");
    }

    /// <summary>
    /// Ein widersprüchliches Band erreicht die Ablage nicht.
    /// </summary>
    /// <remarks>
    /// pH-Min über pH-Max: danach ist <i>jede</i> Messung „daneben". Die Regel
    /// steht in <c>SetpointProfilGrenzen</c> und ist dort geprüft; hier geht es
    /// darum, dass der Endpunkt sie auch fragt.
    /// </remarks>
    [Fact]
    public void EinWidersprueclichesBand_ErreichtDieAblageNicht()
    {
        var endpunkt = Endpunkt();

        endpunkt.Create(new SetpointProfileUpsertRequest
        {
            Name = "Verdreht",
            BaseProfileId = "rdwc-default",
            Overrides = new() { ["Flower"] = new() { ["phMin"] = 6.5, ["phMax"] = 5.5 } },
        });

        var alle = Assert.IsType<OkObjectResult>(endpunkt.GetAll().Result).Value
            as IReadOnlyList<SetpointProfileDto>;
        Assert.True(alle!.All(p => p.Name != "Verdreht"),
            "Ein Profil mit pH-Min ueber pH-Max steht in der Ablage. Danach ist JEDE pH-Messung "
            + "„daneben\", egal welcher Wert.");
    }

    /// <summary>
    /// Ein gelöschtes Profil lässt keine Kennung im Grow zurück.
    /// </summary>
    /// <remarks>
    /// Sonst zeigt der Grow auf ein Profil, das es nicht mehr gibt — und die
    /// Sollwert-Kette fällt still auf den Standard zurück, während in der
    /// Oberfläche noch der alte Name steht.
    /// </remarks>
    [Fact]
    public void EinGeloeschtesProfil_LaesstKeineKennungImGrowZurueck()
    {
        var endpunkt = Endpunkt();
        var angelegt = Assert.IsType<CreatedAtActionResult>(endpunkt.Create(
            new SetpointProfileUpsertRequest { Name = "Weg damit", BaseProfileId = "rdwc-default" }).Result);
        var profil = Assert.IsType<SetpointProfileDto>(angelegt.Value);

        var grows = new GrowRepository(_pfade);
        var zelt = grows.CreateTent(new Tent { Name = "Zelt", TentType = TentType.Production });
        var growId = grows.CreateGrow(new GrowRun
        {
            Name = "Lauf", TentId = zelt.Id, HydroStyle = HydroStyle.RDWC,
            Status = GrowStatus.Running, StartDate = DateTime.Today.AddDays(-5),
            // Die DTO-Kennung IST schon die Referenz ("custom:7").
            SetpointProfileId = profil.Id,
        });

        var zahl = int.Parse(profil.Id[SetpointProfile.Prefix.Length..],
            System.Globalization.CultureInfo.InvariantCulture);
        endpunkt.Delete(zahl);

        Assert.True(grows.GetGrow(growId)!.SetpointProfileId is null,
            "Der Grow zeigt weiter auf ein Profil, das es nicht mehr gibt. Die Sollwert-Kette "
            + "faellt dann still auf den Standard zurueck, waehrend in der Oberflaeche noch der "
            + "alte Name steht.");
    }

    /// <summary>
    /// Eine Hälfte allein darf das Paar nicht gegen die Basis verdrehen.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Fund (Fork AI, 02.10.2026).</b> Geprüft wurde ein Paar nur,
    /// wenn beide Werte in den Abweichungen standen. <c>phMin 7</c> allein ging
    /// durch; zusammengesetzt mit dem <c>phMax</c> der Basis war danach jede
    /// pH-Messung „daneben".</para>
    /// </remarks>
    [Fact]
    public void EineHaelfteGegenDieBasis_ErreichtDieAblageNicht()
    {
        var basis = WertungMitEigenen().GetTargets("rdwc-default", GrowStage.Flower)!;
        Assert.True(basis.PhMax < 7, $"Vorbedingung: Basis-phMax {basis.PhMax} muss unter 7 liegen.");

        var antwort = Endpunkt().Create(new SetpointProfileUpsertRequest
        {
            Name = "Nur Untergrenze",
            BaseProfileId = "rdwc-default",
            Overrides = new() { ["Flower"] = new() { ["phMin"] = 7.0 } },
        });

        Assert.True(antwort.Result is not CreatedAtActionResult,
            $"phMin 7 allein wurde gespeichert. Mit dem phMax {basis.PhMax} der Basis ist danach "
            + "JEDE pH-Messung „daneben\", egal welcher Wert.");
    }

    /// <summary>Die Gegenrichtung: eine passende Hälfte allein geht durch.</summary>
    [Fact]
    public void EinePassendeHaelfte_GehtDurch()
    {
        var antwort = Endpunkt().Create(new SetpointProfileUpsertRequest
        {
            Name = "Etwas tiefer",
            BaseProfileId = "rdwc-default",
            Overrides = new() { ["Flower"] = new() { ["phMin"] = 5.6 } },
        });

        Assert.IsType<CreatedAtActionResult>(antwort.Result);
    }

    /// <summary>
    /// „flower" klein geschrieben wird angenommen — und WIRKT dann auch.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Fund (Fork AI, 02.10.2026).</b> Die Prüfung war
    /// groß/klein-unabhängig, gespeichert wurde roh, <c>Apply</c> sucht genau
    /// „Flower". Bestätigt, gespeichert, nie gewirkt.</para>
    /// </remarks>
    [Fact]
    public void KleinGeschriebenePhase_WirktNachDemSpeichern()
    {
        var angelegt = Assert.IsType<CreatedAtActionResult>(Endpunkt().Create(new SetpointProfileUpsertRequest
        {
            Name = "Klein",
            BaseProfileId = "rdwc-default",
            Overrides = new() { ["flower"] = new() { ["phMin"] = 5.55, ["phMax"] = 6.05 } },
        }).Result);
        var profil = Assert.IsType<SetpointProfileDto>(angelegt.Value);

        // Die Antwort selbst: gespeichert unter dem Namen, den Apply sucht.
        var bluete = profil.Stages.Single(s => s.Stage == nameof(GrowStage.Flower));
        Assert.True(bluete.Changed.Contains("phMin"),
            "Die Antwort zeigt fuer Flower keine eigenen Werte — gespeichert wurde unter „flower\", "
            + "einem Namen, den niemand sucht.");

        var wirkt = WertungMitEigenen().GetTargets(profil.Id, GrowStage.Flower)!;
        Assert.True(wirkt.PhMin == 5.55 && wirkt.PhMax == 6.05,
            $"Gespeichert mit Phase „flower\", wirksam ist pH {wirkt.PhMin}–{wirkt.PhMax} statt "
            + "5,55–6,05. Der Nutzer bekam eine Bestaetigung fuer Werte, die nie gelten.");
    }

    /// <summary>
    /// Eine Ablage von vor der Reparatur („flower" steht schon drin) wird beim Lesen gefunden.
    /// </summary>
    [Fact]
    public void AlteAblageMitKleinerPhase_WirdBeimLesenGefunden()
    {
        var ablage = new SetpointProfileRepository(_pfade);
        var id = ablage.Insert(new SetpointProfile
        {
            Name = "Alt",
            BaseProfileId = "rdwc-default",
            Overrides = new() { ["flower"] = new() { ["ecMin"] = 1.33 } },
        });

        var wirkt = WertungMitEigenen().GetTargets(SetpointProfile.Reference(id), GrowStage.Flower)!;
        Assert.True(wirkt.EcMin == 1.33,
            $"In der Ablage steht „flower\" mit ecMin 1,33, wirksam ist {wirkt.EcMin}. "
            + "Werte, die vor der Reparatur gespeichert wurden, bleiben sonst fuer immer wirkungslos.");
    }

    // ------------------------------------------------------------------ Hilfe

    /// <summary>Die Sollwert-Kette MIT den eigenen Profilen — so, wie die App sie liest.</summary>
    private TargetValueService WertungMitEigenen()
    {
        var wissen = new KnowledgeBaseLoader(_pfade, NullLogger<KnowledgeBaseLoader>.Instance);
        wissen.Initialize();
        return new TargetValueService(wissen, new SetpointProfileRepository(_pfade));
    }

    private SetpointProfilesApiController Endpunkt()
    {
        var wissen = new KnowledgeBaseLoader(_pfade, NullLogger<KnowledgeBaseLoader>.Instance);
        wissen.Initialize();

        return new SetpointProfilesApiController(
            new SetpointProfileRepository(_pfade),
            new TargetValueService(wissen))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext(),
            },
        };
    }

    private void KopiereWissen()
    {
        var quelle = Path.Combine(ProjektWurzel(), "GrowDiary.Web", "wwwroot", "knowledge-defaults");
        var ziel = Path.Combine(_wurzel, "wwwroot", "knowledge-defaults");
        foreach (var datei in Directory.EnumerateFiles(quelle, "*.json", SearchOption.AllDirectories))
        {
            var pfad = Path.Combine(ziel, Path.GetRelativePath(quelle, datei));
            Directory.CreateDirectory(Path.GetDirectoryName(pfad)!);
            File.Copy(datei, pfad);
        }
    }

    private static string ProjektWurzel()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "GrowDiary.Web"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Projektwurzel nicht gefunden.");
    }
}
