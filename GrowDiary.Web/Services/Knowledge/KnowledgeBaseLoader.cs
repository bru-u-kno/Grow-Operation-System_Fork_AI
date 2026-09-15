using System.Text.Json;
using System.Text.Json.Serialization;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Services.Knowledge.Schema;

namespace GrowDiary.Web.Services.Knowledge;

public sealed class KnowledgeBaseLoader
{
    private readonly AppPaths _paths;
    private readonly ILogger<KnowledgeBaseLoader> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private IReadOnlyList<TreatmentDefinition> _treatments = Array.Empty<TreatmentDefinition>();
    private IReadOnlyList<SopDefinition> _sops = Array.Empty<SopDefinition>();
    private IReadOnlyList<NutrientProgramDefinition> _nutrientPrograms = Array.Empty<NutrientProgramDefinition>();
    private IReadOnlyList<SetpointDefinition> _setpoints = Array.Empty<SetpointDefinition>();
    private IReadOnlyList<PathogenDefinition> _pathogens = Array.Empty<PathogenDefinition>();
    private IReadOnlyList<SymptomDefinition> _symptoms = Array.Empty<SymptomDefinition>();
    private IReadOnlyList<WearTemplateDefinition> _wearTemplates = Array.Empty<WearTemplateDefinition>();
    private IReadOnlyList<GuidanceDefinition> _guidance = Array.Empty<GuidanceDefinition>();

    public IReadOnlyList<TreatmentDefinition> Treatments => _treatments;
    public IReadOnlyList<SopDefinition> Sops => _sops;
    public IReadOnlyList<NutrientProgramDefinition> NutrientPrograms => _nutrientPrograms;
    public IReadOnlyList<SetpointDefinition> Setpoints => _setpoints;
    public IReadOnlyList<PathogenDefinition> Pathogens => _pathogens;
    public IReadOnlyList<SymptomDefinition> Symptoms => _symptoms;
    public IReadOnlyList<WearTemplateDefinition> WearTemplates => _wearTemplates;
    public IReadOnlyList<GuidanceDefinition> Guidance => _guidance;

    /// <summary>
    /// Fork AI (F-004, forkai.112): läuft nach jedem Laden und darf die
    /// Düngeprogramme überlagern (vom Nutzer gesetzte Wochenwerte).
    /// </summary>
    /// <remarks>
    /// Ein Haken statt einer Abhängigkeit im Konstruktor: der Loader wird in
    /// vielen Tests von Hand gebaut, und das Original bleibt so unberührt.
    /// </remarks>
    public Action<IReadOnlyList<NutrientProgramDefinition>>? NachDemLaden { get; set; }

    public KnowledgeBaseLoader(AppPaths paths, ILogger<KnowledgeBaseLoader> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public void Initialize()
    {
        EnsureKnowledgeDirectory();
        Reload();
    }

    public void Reload()
    {
        _treatments = LoadCategory<TreatmentDefinition>("treatments");
        _sops = LoadCategory<SopDefinition>("sops");
        _nutrientPrograms = LoadCategory<NutrientProgramDefinition>("nutrient-programs");
        _setpoints = LoadCategory<SetpointDefinition>("setpoints");
        _pathogens = LoadCategory<PathogenDefinition>("pathogens");
        _symptoms = LoadCategory<SymptomDefinition>("symptoms");
        _wearTemplates = LoadCategory<WearTemplateDefinition>("wear");
        _guidance = LoadCategory<GuidanceDefinition>("guidance");
        NachDemLaden?.Invoke(_nutrientPrograms); // Fork AI (F-004)

        _logger.LogInformation(
            "Knowledge-Base geladen: {TC} Treatments, {SC} SOPs, {NC} Programme, {SetC} Setpoints, {PC} Pathogens, {SymC} Symptoms, {WC} Wear-Templates, {GC} Regeln",
            _treatments.Count, _sops.Count, _nutrientPrograms.Count,
            _setpoints.Count, _pathogens.Count, _symptoms.Count, _wearTemplates.Count, _guidance.Count);
    }

    /// <summary>
    /// Keeps the on-disk knowledge base in step with the shipped defaults. Without this a
    /// correction to a setpoint or a new SOP would only ever reach fresh installs, because
    /// the defaults used to be copied only when the folder was completely empty.
    /// <para>
    /// A manifest records the hash of what we last shipped for each file, so a file the user
    /// edited themselves is recognised and left alone. Files the user added are never removed.
    /// </para>
    /// </summary>
    private void EnsureKnowledgeDirectory()
    {
        var knowledgeDataPath = _paths.KnowledgeDataPath;
        Directory.CreateDirectory(knowledgeDataPath);

        var defaultsPath = _paths.KnowledgeDefaultsPath;
        if (!Directory.Exists(defaultsPath))
        {
            _logger.LogWarning("Knowledge-Defaults-Verzeichnis nicht gefunden: {Path}", defaultsPath);
            return;
        }

        var manifestPath = Path.Combine(knowledgeDataPath, ".shipped-defaults.json");
        var manifest = LoadShippedManifest(manifestPath);
        int added = 0, updated = 0, keptUserEdit = 0, backedUp = 0;

        foreach (var sourceFile in Directory.EnumerateFiles(defaultsPath, "*.json", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(defaultsPath, sourceFile);
            var destFile = Path.Combine(knowledgeDataPath, relativePath);
            var shippedHash = HashFile(sourceFile);

            if (!File.Exists(destFile))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
                File.Copy(sourceFile, destFile);
                manifest[relativePath] = shippedHash;
                added++;
                continue;
            }

            var currentHash = HashFile(destFile);
            if (currentHash == shippedHash)
            {
                manifest[relativePath] = shippedHash;
                continue;
            }

            // Known to be our previous version → safe to update. Anything else is the user's.
            if (manifest.TryGetValue(relativePath, out var lastShipped) && lastShipped != currentHash)
            {
                keptUserEdit++;
                continue;
            }

            if (!manifest.ContainsKey(relativePath))
            {
                // First sync on an older install: we cannot prove the file is untouched, so
                // keep a copy of it next to the original before refreshing.
                File.Copy(destFile, destFile + ".user-backup", overwrite: true);
                backedUp++;
            }

            File.Copy(sourceFile, destFile, overwrite: true);
            manifest[relativePath] = shippedHash;
            updated++;
        }

        SaveShippedManifest(manifestPath, manifest);

        if (added > 0 || updated > 0 || keptUserEdit > 0)
        {
            _logger.LogInformation(
                "Knowledge-Base abgeglichen: {Added} neu, {Updated} aktualisiert, {Kept} eigene Anpassungen behalten.",
                added, updated, keptUserEdit);
        }

        if (backedUp > 0)
        {
            _logger.LogInformation(
                "{Count} Datei(en) konnten nicht als unveraendert nachgewiesen werden; der bisherige Stand liegt daneben als *.user-backup.",
                backedUp);
        }
    }

    /// <summary>
    /// Hashes a knowledge file by its <em>content</em>, not its bytes: the JSON is re-serialised
    /// canonically first, so a difference in indentation or line endings never counts as a change.
    /// </summary>
    private static readonly string NewLine = ((char)10).ToString();

    private static string HashFile(string path)
    {
        var raw = File.ReadAllText(path);
        string canonical;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(raw);
            canonical = System.Text.Json.JsonSerializer.Serialize(document.RootElement);
        }
        catch (System.Text.Json.JsonException)
        {
            // Not valid JSON — fall back to comparing the text with normalised line endings.
            canonical = raw.ReplaceLineEndings(NewLine);
        }

        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical)));
    }

    private Dictionary<string, string> LoadShippedManifest(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                   ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Knowledge-Manifest unlesbar — wird neu aufgebaut.");
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void SaveShippedManifest(string path, Dictionary<string, string> manifest)
    {
        try
        {
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(manifest));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Knowledge-Manifest konnte nicht geschrieben werden.");
        }
    }

    private IReadOnlyList<T> LoadCategory<T>(string folder) where T : KnowledgeFileMetadata
    {
        var categoryPath = Path.Combine(_paths.KnowledgeDataPath, folder);
        if (!Directory.Exists(categoryPath))
        {
            _logger.LogWarning("Knowledge-Kategorie-Ordner nicht gefunden: {Path}", categoryPath);
            return Array.Empty<T>();
        }

        var results = new List<T>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /* SORTIERT einlesen.
           `Directory.EnumerateFiles` sagt ueber die Reihenfolge nichts zu:
           unter Windows/NTFS ist sie praktisch alphabetisch, unter Linux/ext4
           eine Hash-Reihenfolge — und das Add-on laeuft im Linux-Container.
           Die Liste hier erbt jeder Verbraucher: die Wissensseite zeigt sie so
           an, und die Suche schneidet mit `.Take(5)` ab. Bei 13 Regeln, die
           "wasser" enthalten, entscheidet die Dateireihenfolge also, WELCHE
           fuenf der Nutzer sieht — und damit, ob er einen Eintrag ueberhaupt
           findet. Auf meinem Rechner andere als auf seinem.
           Am 01.09.2026 hat dieselbe Ursache zwei Tests im Tor rot gemacht:
           dort kam ein Duengerprogramm ohne Bluete-Chart zuerst. */
        var dateien = Directory.EnumerateFiles(categoryPath, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal);

        foreach (var file in dateien)
        {
            try
            {
                var json = File.ReadAllText(file);
                var item = JsonSerializer.Deserialize<T>(json, JsonOptions);

                if (item == null)
                {
                    _logger.LogError("Konnte {File} nicht deserialisieren (null).", file);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(item.SchemaVersion))
                {
                    _logger.LogError("Fehlende schemaVersion in {File} — Datei übersprungen.", file);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(item.Id))
                {
                    _logger.LogError("Fehlende id in {File} — Datei übersprungen.", file);
                    continue;
                }

                if (!seenIds.Add(item.Id))
                {
                    _logger.LogError("Doppelte ID '{Id}' in {File} — Datei übersprungen.", item.Id, file);
                    continue;
                }

                if (item.Id.StartsWith("example-"))
                {
                    _logger.LogWarning(
                        "Beispiel-Datei {Id} in {Category} gefunden. Bitte App_Data/knowledge/ leeren für Migration.",
                        item.Id, folder);
                }

                results.Add(item);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fehler beim Laden von {File} — übersprungen.", file);
            }
        }

        // Nach der Kennung, nicht nach dem Dateinamen: die Kennung steht IM
        // Inhalt und ist das, was Oberflaeche und Suche zeigen.
        return results.OrderBy(item => item.Id, StringComparer.Ordinal).ToList().AsReadOnly();
    }
}
