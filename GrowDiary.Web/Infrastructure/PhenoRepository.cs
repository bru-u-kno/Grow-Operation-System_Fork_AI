using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Infrastructure;

/// <summary>Stores the pheno-hunt score sheets and the user's trait weighting.</summary>
public sealed class PhenoRepository : RepositoryBase
{
    private const string WeightsKeyPrefix = "pheno:weight:";

    public PhenoRepository(AppPaths paths) : base(paths)
    {
    }

    private const string Columns = """
        Id, PlantInstanceId, VigorScore, InternodeSpacing, BranchingScore, LeafToBudScore, HeightAtFlipCm,
        TrainingMethods, TrainingResponseScore, StressToleranceScore, PestResistanceScore,
        FloweringDays, HeightAtHarvestCm, WetYieldG, DryYieldG, BudDensityScore, ResinScore, TrimEaseScore,
        AromaScore, AromaNotes, FlavorScore, EffectScore, EffectNotes, ThcPercent, CbdPercent, TerpeneNotes,
        ManualOverallScore, IsKeeper, ConfirmedInSecondRun, Notes, CreatedAtUtc, UpdatedAtUtc
        """;

    /// <summary>All score sheets for the plants of one grow.</summary>
    public IReadOnlyList<PhenoEvaluation> GetForGrow(int growId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {Columns} FROM PhenoEvaluations
            WHERE PlantInstanceId IN (SELECT Id FROM PlantInstances WHERE GrowId = $growId);
            """;
        command.Parameters.AddWithValue("$growId", growId);
        return Read(command);
    }

    public PhenoEvaluation? GetForPlant(int plantInstanceId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM PhenoEvaluations WHERE PlantInstanceId = $plantId;";
        command.Parameters.AddWithValue("$plantId", plantInstanceId);
        return Read(command).FirstOrDefault();
    }

    /// <summary>Creates or replaces the sheet for a plant (one sheet per plant).</summary>
    public PhenoEvaluation Save(PhenoEvaluation evaluation)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO PhenoEvaluations (
                PlantInstanceId, VigorScore, InternodeSpacing, BranchingScore, LeafToBudScore, HeightAtFlipCm,
                TrainingMethods, TrainingResponseScore, StressToleranceScore, PestResistanceScore,
                FloweringDays, HeightAtHarvestCm, WetYieldG, DryYieldG, BudDensityScore, ResinScore, TrimEaseScore,
                AromaScore, AromaNotes, FlavorScore, EffectScore, EffectNotes, ThcPercent, CbdPercent, TerpeneNotes,
                ManualOverallScore, IsKeeper, ConfirmedInSecondRun, Notes, CreatedAtUtc, UpdatedAtUtc
            ) VALUES (
                $plantId, $vigor, $internode, $branching, $leafToBud, $heightFlip,
                $training, $trainingResponse, $stress, $pest,
                $flowerDays, $heightHarvest, $wet, $dry, $density, $resin, $trim,
                $aroma, $aromaNotes, $flavor, $effect, $effectNotes, $thc, $cbd, $terpenes,
                $manual, $keeper, $confirmed, $notes, datetime('now'), datetime('now')
            )
            ON CONFLICT(PlantInstanceId) DO UPDATE SET
                VigorScore = excluded.VigorScore,
                InternodeSpacing = excluded.InternodeSpacing,
                BranchingScore = excluded.BranchingScore,
                LeafToBudScore = excluded.LeafToBudScore,
                HeightAtFlipCm = excluded.HeightAtFlipCm,
                TrainingMethods = excluded.TrainingMethods,
                TrainingResponseScore = excluded.TrainingResponseScore,
                StressToleranceScore = excluded.StressToleranceScore,
                PestResistanceScore = excluded.PestResistanceScore,
                FloweringDays = excluded.FloweringDays,
                HeightAtHarvestCm = excluded.HeightAtHarvestCm,
                WetYieldG = excluded.WetYieldG,
                DryYieldG = excluded.DryYieldG,
                BudDensityScore = excluded.BudDensityScore,
                ResinScore = excluded.ResinScore,
                TrimEaseScore = excluded.TrimEaseScore,
                AromaScore = excluded.AromaScore,
                AromaNotes = excluded.AromaNotes,
                FlavorScore = excluded.FlavorScore,
                EffectScore = excluded.EffectScore,
                EffectNotes = excluded.EffectNotes,
                ThcPercent = excluded.ThcPercent,
                CbdPercent = excluded.CbdPercent,
                TerpeneNotes = excluded.TerpeneNotes,
                ManualOverallScore = excluded.ManualOverallScore,
                IsKeeper = excluded.IsKeeper,
                ConfirmedInSecondRun = excluded.ConfirmedInSecondRun,
                Notes = excluded.Notes,
                UpdatedAtUtc = datetime('now');
            """;
        Bind(command, evaluation);
        command.ExecuteNonQuery();
        return GetForPlant(evaluation.PlantInstanceId)!;
    }

    public PhenoWeights GetWeights()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Key, Value FROM AppSettings WHERE Key LIKE $prefix;";
        command.Parameters.AddWithValue("$prefix", WeightsKeyPrefix + "%");
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var key = reader["Key"].ToString()![WeightsKeyPrefix.Length..];
                if (Zahlenlesen.Maschine(reader["Value"].ToString()) is { } value)
                {
                    values[key] = value;
                }
            }
        }

        var fallback = PhenoWeights.Default;
        return new PhenoWeights(
            values.TryGetValue("yield", out var y) ? y : fallback.Yield,
            values.TryGetValue("quality", out var q) ? q : fallback.Quality,
            values.TryGetValue("potency", out var p) ? p : fallback.Potency,
            values.TryGetValue("resilience", out var r) ? r : fallback.Resilience,
            values.TryGetValue("structure", out var s) ? s : fallback.Structure);
    }

    public void SaveWeights(PhenoWeights weights)
    {
        using var connection = OpenConnection();
        foreach (var (name, value) in new (string, double)[]
                 {
                     ("yield", weights.Yield), ("quality", weights.Quality), ("potency", weights.Potency),
                     ("resilience", weights.Resilience), ("structure", weights.Structure),
                 })
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO AppSettings (Key, Value) VALUES ($key, $value)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
                """;
            command.Parameters.AddWithValue("$key", WeightsKeyPrefix + name);
            command.Parameters.AddWithValue("$value", Math.Clamp(value, 0, 100).ToString(System.Globalization.CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }
    }

    private static void Bind(SqliteCommand command, PhenoEvaluation e)
    {
        command.Parameters.AddWithValue("$plantId", e.PlantInstanceId);
        command.Parameters.AddWithValue("$vigor", (object?)e.VigorScore ?? DBNull.Value);
        command.Parameters.AddWithValue("$internode", e.InternodeSpacing.ToString());
        command.Parameters.AddWithValue("$branching", (object?)e.BranchingScore ?? DBNull.Value);
        command.Parameters.AddWithValue("$leafToBud", (object?)e.LeafToBudScore ?? DBNull.Value);
        command.Parameters.AddWithValue("$heightFlip", (object?)e.HeightAtFlipCm ?? DBNull.Value);
        command.Parameters.AddWithValue("$training", (object?)e.TrainingMethods ?? DBNull.Value);
        command.Parameters.AddWithValue("$trainingResponse", (object?)e.TrainingResponseScore ?? DBNull.Value);
        command.Parameters.AddWithValue("$stress", (object?)e.StressToleranceScore ?? DBNull.Value);
        command.Parameters.AddWithValue("$pest", (object?)e.PestResistanceScore ?? DBNull.Value);
        command.Parameters.AddWithValue("$flowerDays", (object?)e.FloweringDays ?? DBNull.Value);
        command.Parameters.AddWithValue("$heightHarvest", (object?)e.HeightAtHarvestCm ?? DBNull.Value);
        command.Parameters.AddWithValue("$wet", (object?)e.WetYieldG ?? DBNull.Value);
        command.Parameters.AddWithValue("$dry", (object?)e.DryYieldG ?? DBNull.Value);
        command.Parameters.AddWithValue("$density", (object?)e.BudDensityScore ?? DBNull.Value);
        command.Parameters.AddWithValue("$resin", (object?)e.ResinScore ?? DBNull.Value);
        command.Parameters.AddWithValue("$trim", (object?)e.TrimEaseScore ?? DBNull.Value);
        command.Parameters.AddWithValue("$aroma", (object?)e.AromaScore ?? DBNull.Value);
        command.Parameters.AddWithValue("$aromaNotes", (object?)e.AromaNotes ?? DBNull.Value);
        command.Parameters.AddWithValue("$flavor", (object?)e.FlavorScore ?? DBNull.Value);
        command.Parameters.AddWithValue("$effect", (object?)e.EffectScore ?? DBNull.Value);
        command.Parameters.AddWithValue("$effectNotes", (object?)e.EffectNotes ?? DBNull.Value);
        command.Parameters.AddWithValue("$thc", (object?)e.ThcPercent ?? DBNull.Value);
        command.Parameters.AddWithValue("$cbd", (object?)e.CbdPercent ?? DBNull.Value);
        command.Parameters.AddWithValue("$terpenes", (object?)e.TerpeneNotes ?? DBNull.Value);
        command.Parameters.AddWithValue("$manual", (object?)e.ManualOverallScore ?? DBNull.Value);
        command.Parameters.AddWithValue("$keeper", e.IsKeeper ? 1 : 0);
        command.Parameters.AddWithValue("$confirmed", e.ConfirmedInSecondRun ? 1 : 0);
        command.Parameters.AddWithValue("$notes", (object?)e.Notes ?? DBNull.Value);
    }

    private static List<PhenoEvaluation> Read(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var list = new List<PhenoEvaluation>();
        while (reader.Read())
        {
            list.Add(new PhenoEvaluation
            {
                Id = Convert.ToInt32(reader["Id"]),
                PlantInstanceId = Convert.ToInt32(reader["PlantInstanceId"]),
                VigorScore = NullInt(reader["VigorScore"]),
                InternodeSpacing = Enum.TryParse<InternodeSpacing>(reader["InternodeSpacing"].ToString(), out var spacing) ? spacing : InternodeSpacing.Unknown,
                BranchingScore = NullInt(reader["BranchingScore"]),
                LeafToBudScore = NullInt(reader["LeafToBudScore"]),
                HeightAtFlipCm = NullDouble(reader["HeightAtFlipCm"]),
                TrainingMethods = NullText(reader["TrainingMethods"]),
                TrainingResponseScore = NullInt(reader["TrainingResponseScore"]),
                StressToleranceScore = NullInt(reader["StressToleranceScore"]),
                PestResistanceScore = NullInt(reader["PestResistanceScore"]),
                FloweringDays = NullInt(reader["FloweringDays"]),
                HeightAtHarvestCm = NullDouble(reader["HeightAtHarvestCm"]),
                WetYieldG = NullDouble(reader["WetYieldG"]),
                DryYieldG = NullDouble(reader["DryYieldG"]),
                BudDensityScore = NullInt(reader["BudDensityScore"]),
                ResinScore = NullInt(reader["ResinScore"]),
                TrimEaseScore = NullInt(reader["TrimEaseScore"]),
                AromaScore = NullInt(reader["AromaScore"]),
                AromaNotes = NullText(reader["AromaNotes"]),
                FlavorScore = NullInt(reader["FlavorScore"]),
                EffectScore = NullInt(reader["EffectScore"]),
                EffectNotes = NullText(reader["EffectNotes"]),
                ThcPercent = NullDouble(reader["ThcPercent"]),
                CbdPercent = NullDouble(reader["CbdPercent"]),
                TerpeneNotes = NullText(reader["TerpeneNotes"]),
                ManualOverallScore = NullDouble(reader["ManualOverallScore"]),
                IsKeeper = Convert.ToInt32(reader["IsKeeper"]) == 1,
                ConfirmedInSecondRun = Convert.ToInt32(reader["ConfirmedInSecondRun"]) == 1,
                Notes = NullText(reader["Notes"]),
                CreatedAtUtc = DateTime.Parse(reader["CreatedAtUtc"].ToString()!, null, System.Globalization.DateTimeStyles.RoundtripKind),
                UpdatedAtUtc = DateTime.Parse(reader["UpdatedAtUtc"].ToString()!, null, System.Globalization.DateTimeStyles.RoundtripKind),
            });
        }

        return list;
    }

    private static int? NullInt(object value) => value is DBNull ? null : Convert.ToInt32(value);
    private static double? NullDouble(object value) => value is DBNull ? null : Convert.ToDouble(value);
    private static string? NullText(object value) => value is DBNull ? null : value.ToString();
}
