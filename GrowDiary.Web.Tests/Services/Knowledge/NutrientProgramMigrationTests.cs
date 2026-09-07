using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services.Knowledge;

public sealed class NutrientProgramMigrationTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly KnowledgeBaseLoader _loader;
    private readonly CultivationKnowledgeService _svc;

    public NutrientProgramMigrationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "NutrientMigTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        var projectRoot = FindProjectRoot();
        var defaultsSource = Path.Combine(projectRoot, "GrowDiary.Web", "wwwroot", "knowledge-defaults");
        CopyDefaults(defaultsSource, _tempRoot);

        var paths = new AppPaths(_tempRoot);
        _loader = new KnowledgeBaseLoader(paths, NullLogger<KnowledgeBaseLoader>.Instance);
        _loader.Initialize();

        _svc = new CultivationKnowledgeService(_loader);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private static string FindProjectRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.GetFiles(dir, "*.sln").Length > 0 ||
                Directory.Exists(Path.Combine(dir, "GrowDiary.Web")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Project root not found");
    }

    private static void CopyDefaults(string source, string tempRoot)
    {
        var dest = Path.Combine(tempRoot, "wwwroot", "knowledge-defaults");
        foreach (var file in Directory.EnumerateFiles(source, "*.json", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            var target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    [Fact]
    public void Loader_LoadsAllFourNutrientPrograms()
    {
        // Fork AI: Athena, Canna Aqua, Hydro Research VBX + SKX Canna Aqua.
        Assert.Equal(4, _loader.NutrientPrograms.Count);
    }

    [Fact]
    public void CultivationKnowledgeService_ExposesAllPrograms()
    {
        Assert.Equal(4, _svc.GetPrograms().Count);
    }

    [Fact]
    public void CultivationKnowledgeService_SkxCannaAqua_HasFourteenWeekColumns()
    {
        // Fork AI: Root, Vega 1–4, Flores 1–8, Flush — das SKX-Schema in ml je Liter.
        var skx = _svc.GetPrograms().Single(p => p.Key == "skx-canna-aqua");
        Assert.NotNull(skx.FeedChart);
        Assert.Equal(14, skx.FeedChart!.Columns.Count);
        var flores3 = skx.FeedChart.Columns.Single(c => c.Id == "flower-w3");
        Assert.Equal(1.2, flores3.EcTarget);
        Assert.Equal(2.2, flores3.Items.Single(i => i.Component == "Aqua Flores A").MinMlPerLiter);
    }

    [Fact]
    public void CultivationKnowledgeService_MatchProgram_ExactSkxNameWinsOverCannaSubstring()
    {
        // Fork AI: „SKX Canna Aqua" enthält „Canna Aqua" — der Freitext-Treffer darf
        // trotzdem nicht auf das Original zurückfallen, wenn der Name exakt passt.
        var match = _svc.MatchProgram("SKX Canna Aqua");
        Assert.NotNull(match);
        Assert.Equal("skx-canna-aqua", match.Key);
    }

    [Fact]
    public void CultivationKnowledgeService_AthenaProgram_HasSevenStages()
    {
        var athena = _svc.GetPrograms().Single(p => p.Key == "athena");
        Assert.Equal(7, athena.Stages.Count);
    }

    [Fact]
    public void CultivationKnowledgeService_VbxProgram_HasSixStages()
    {
        var vbx = _svc.GetPrograms().Single(p => p.Key == "hydro-research-vbx");
        Assert.Equal(6, vbx.Stages.Count);
    }

    [Fact]
    public void CultivationKnowledgeService_CannaAquaProgram_HasSixStages()
    {
        var canna = _svc.GetPrograms().Single(p => p.Key == "canna-aqua");
        Assert.Equal(6, canna.Stages.Count);
    }

    [Fact]
    public void CultivationKnowledgeService_MatchProgram_FindsAthenaByKeyword()
    {
        var match = _svc.MatchProgram("athena blended grow a");
        Assert.NotNull(match);
        Assert.Equal("athena", match.Key);
    }

    [Fact]
    public void CultivationKnowledgeService_MatchProgram_FindsCannaByKeyword()
    {
        var match = _svc.MatchProgram("canna aqua vega");
        Assert.NotNull(match);
        Assert.Equal("canna-aqua", match.Key);
    }
}
