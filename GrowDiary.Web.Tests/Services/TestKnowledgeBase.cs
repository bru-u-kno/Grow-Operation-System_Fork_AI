using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Eine echte Wissensbasis für Tests, gespiegelt in ein Temp-Verzeichnis.
/// </summary>
/// <remarks>
/// Gelesen wird aus <c>GrowDiary.Web/wwwroot/knowledge-defaults</c> — die im
/// Repository eingecheckte Quelle. Nicht aus <c>App_Data</c>: das entsteht erst
/// beim ersten Start und fehlt im frischen CI-Checkout. Genau daran ist der
/// erste Anlauf gescheitert, lokal grün und in CI rot.
///
/// Steht hier einmal, statt in jeder Testklasse noch einmal: dieselbe Pfadsuche
/// lag vorher in zwei Dateien, und die dritte Kopie war die kaputte.
/// </remarks>
public static class TestKnowledgeBase
{
    // Jeder Aufruf legt einen Ordner an (274 Dateien). Früher blieben sie liegen: auf der Entwicklungsmaschine
    // über 3.600 Ordner, bis /tmp keine Inodes mehr hatte. Jetzt räumt der Testprozess am Ende selbst auf.
    private static readonly System.Collections.Concurrent.ConcurrentBag<string> Angelegt = new();

    static TestKnowledgeBase()
        => AppDomain.CurrentDomain.ProcessExit += (_, _) => Aufraeumen();

    private static void Aufraeumen()
    {
        foreach (var ordner in Angelegt)
        {
            try { if (Directory.Exists(ordner)) Directory.Delete(ordner, recursive: true); }
            catch (Exception) { /* beim Beenden unwichtig: der nächste Lauf legt neue an */ }
        }
    }

    public static TargetValueService TargetValues()
        => new(Loader());

    public static KnowledgeBaseLoader Loader()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "growos-knowledge-" + Guid.NewGuid().ToString("N"));
        Angelegt.Add(tempRoot);
        CopyDefaults(Path.Combine(ProjectRoot(), "GrowDiary.Web", "wwwroot", "knowledge-defaults"), tempRoot);

        var loader = new KnowledgeBaseLoader(new AppPaths(tempRoot), NullLogger<KnowledgeBaseLoader>.Instance);
        loader.Initialize();
        return loader;
    }

    private static void CopyDefaults(string source, string tempRoot)
    {
        var destination = Path.Combine(tempRoot, "wwwroot", "knowledge-defaults");
        foreach (var file in Directory.EnumerateFiles(source, "*.json", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    /// <summary>
    /// Der Ordner, der <c>GrowDiary.Web</c> enthält. Die Projektmappe heisst
    /// <c>GrowDiary.slnx</c> — auf <c>*.sln</c> zu prüfen findet hier nichts.
    /// </summary>
    private static string ProjectRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory, "GrowDiary.Web")))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new InvalidOperationException("Projektwurzel nicht gefunden.");
    }
}
