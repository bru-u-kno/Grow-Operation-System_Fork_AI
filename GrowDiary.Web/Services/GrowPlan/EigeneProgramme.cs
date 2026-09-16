using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Services.Knowledge;
using GrowDiary.Web.Services.Knowledge.Schema;

namespace GrowDiary.Web.Services.GrowPlan;

/// <summary>
/// Fork AI (Grow-Plan, Schritt 3): eigene Düngeprogramme als Dateien im
/// Wissensordner.
/// </summary>
/// <remarks>
/// <para><b>Warum als Datei.</b> Der Wissens-Loader liest Programme ohnehin
/// aus <c>/data/knowledge/nutrient-programs</c> und lässt Dateien, die nicht
/// mitgeliefert wurden, ausdrücklich in Ruhe. Ein eigenes Programm ist damit
/// sofort überall wählbar (Grow-Formular, Wissen), wandert in jede Sicherung
/// mit — und es braucht keine zweite Programmliste.</para>
/// <para>Mitgelieferte Programme werden nie geändert; eigene tragen das
/// Präfix <see cref="Praefix"/>.</para>
/// </remarks>
public sealed class EigeneProgramme
{
    public const string Praefix = "eigen-";
    private const string Ordner = "nutrient-programs";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly AppPaths _pfade;
    private readonly KnowledgeBaseLoader _wissen;
    private readonly ILogger<EigeneProgramme> _logger;

    public EigeneProgramme(AppPaths pfade, KnowledgeBaseLoader wissen, ILogger<EigeneProgramme> logger)
    {
        _pfade = pfade;
        _wissen = wissen;
        _logger = logger;
    }

    public static bool IstEigen(string? id)
        => id is not null && id.StartsWith(Praefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Das geladene Programm mit dieser Id — oder null.</summary>
    public NutrientProgramDefinition? Finden(string id)
        => _wissen.NutrientPrograms.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Legt ein eigenes Programm als Kopie der Vorlage an und lädt die Wissensbasis neu.
    /// </summary>
    public NutrientProgramDefinition Anlegen(NutrientProgramDefinition vorlage, string name)
    {
        name = string.IsNullOrWhiteSpace(name) ? $"{vorlage.Name} (eigen)" : name.Trim();
        var id = FreieId(name);

        var kopie = JsonSerializer.Deserialize<NutrientProgramDefinition>(
            JsonSerializer.Serialize(vorlage, Json), Json)!;
        kopie.Id = id;
        kopie.Name = name;
        kopie.Summary = $"Eigenes Programm auf Basis von {vorlage.Name}. {vorlage.Summary}".Trim();
        kopie.SearchTerms = [.. kopie.SearchTerms, "eigen", "eigenes programm"];

        Schreiben(kopie);
        _logger.LogInformation("Eigenes Programm {Id} aus {Vorlage} angelegt.", id, vorlage.Id);
        return Finden(id) ?? kopie;
    }

    /// <summary>Schreibt ein eigenes Programm zurück und lädt die Wissensbasis neu.</summary>
    public void Speichern(NutrientProgramDefinition programm)
    {
        if (!IstEigen(programm.Id))
            throw new InvalidOperationException($"{programm.Id} ist ein mitgeliefertes Programm und wird nicht geändert.");
        Schreiben(programm);
    }

    private void Schreiben(NutrientProgramDefinition programm)
    {
        var ordner = Path.Combine(_pfade.KnowledgeDataPath, Ordner);
        Directory.CreateDirectory(ordner);
        var ziel = Path.Combine(ordner, $"{programm.Id}.json");
        var temp = ziel + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(programm, Json), new UTF8Encoding(false));
        File.Move(temp, ziel, overwrite: true);
        _wissen.Reload();
    }

    private string FreieId(string name)
    {
        var basis = Praefix + Slug(name);
        var id = basis;
        for (var n = 2; Finden(id) is not null || File.Exists(Path.Combine(_pfade.KnowledgeDataPath, Ordner, $"{id}.json")); n++)
        {
            id = $"{basis}-{n}";
        }
        return id;
    }

    public static string Slug(string text)
    {
        var ersetzt = text.ToLowerInvariant()
            .Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss");
        var normal = ersetzt.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normal)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "programm" : slug.Length > 48 ? slug[..48].Trim('-') : slug;
    }
}
