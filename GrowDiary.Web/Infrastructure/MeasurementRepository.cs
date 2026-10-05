using System.Globalization;
using GrowDiary.Web.Models;
using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Infrastructure;

public sealed class MeasurementRepository : RepositoryBase
{
    public MeasurementRepository(AppPaths paths) : base(paths)
    {
    }

    public List<Measurement> GetMeasurementsForGrow(int growId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT *
            FROM Measurements
            WHERE GrowId = $growId
            ORDER BY TakenAt DESC, Id DESC;
        """;
        command.Parameters.AddWithValue("$growId", growId);

        var items = new List<Measurement>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(MapMeasurement(reader));
        }

        return items;
    }

    public List<Measurement> GetMeasurementsForTent(int tentId, int limit = 200)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.*
            FROM Measurements m
            INNER JOIN Grows g ON g.Id = m.GrowId
            WHERE g.TentId = $tentId
            ORDER BY m.TakenAt DESC, m.Id DESC
            LIMIT $limit;
        """;
        command.Parameters.AddWithValue("$tentId", tentId);
        command.Parameters.AddWithValue("$limit", limit);

        var items = new List<Measurement>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(MapMeasurement(reader));
        }
        return items;
    }

    public Measurement? GetMeasurement(int id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM Measurements WHERE Id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapMeasurement(reader) : null;
    }

    public Measurement? GetLatestMeasurement(int growId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM Measurements WHERE GrowId = $growId ORDER BY TakenAt DESC, Id DESC LIMIT 1;";
        command.Parameters.AddWithValue("$growId", growId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapMeasurement(reader) : null;
    }

    public Measurement? GetPreviousMeasurement(int growId, DateTime beforeTakenAt, int currentMeasurementId = 0)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT * FROM Measurements
            WHERE GrowId = $growId
              AND TakenAt < $beforeTakenAt
              AND Id <> $currentMeasurementId
            ORDER BY TakenAt DESC, Id DESC
            LIMIT 1;
        """;
        command.Parameters.AddWithValue("$growId", growId);
        command.Parameters.AddWithValue("$beforeTakenAt", ToStorage(beforeTakenAt));
        command.Parameters.AddWithValue("$currentMeasurementId", currentMeasurementId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapMeasurement(reader) : null;
    }

    public int CreateMeasurement(Measurement measurement)
    {
        using var connection = OpenConnection();
        return CreateMeasurement(measurement, connection, null);
    }

    /// <summary>
    /// Dieselbe Anlage auf einer fremden Verbindung — für Vorgänge, die mehrere
    /// Tabellen in EINER Transaktion schreiben (Wasserwechsel-Vorgang, A-006).
    /// </summary>
    /// <remarks>
    /// Bewusst dieselbe Methode wie oben und keine zweite INSERT-Zeile: zwei
    /// Spaltenlisten für dieselbe Tabelle laufen auseinander, sobald jemand ein
    /// Messfeld ergänzt.
    /// </remarks>
    internal static int CreateMeasurement(Measurement measurement, SqliteConnection connection, SqliteTransaction? transaction)
    {
        measurement.CreatedAtUtc = DateTime.UtcNow;
        measurement.UpdatedAtUtc = DateTime.UtcNow;

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Measurements
            (
                GrowId, TakenAt, Stage, Source, Notes,
                AirTemperatureC, HumidityPercent, HeightCm,
                WaterAmountMl, RunoffAmountMl, IrrigationPh, IrrigationEc, DrainPh, DrainEc,
                ReservoirPh, ReservoirEc, ReservoirWaterTempC, ReservoirLevelCm, ReservoirLevelLiters,
                DissolvedOxygenMgL, AirflowAtLeafMPerMin, WaterFlow, OrpMv, TopOffLiters, AddbackEc, SolutionChange,
                PpfdMol, Co2Ppm, CreatedAtUtc, UpdatedAtUtc
            )
            VALUES
            (
                $growId, $takenAt, $stage, $source, $notes,
                $airTemperatureC, $humidityPercent, $heightCm,
                $waterAmountMl, $runoffAmountMl, $irrigationPh, $irrigationEc, $drainPh, $drainEc,
                $reservoirPh, $reservoirEc, $reservoirWaterTempC, $reservoirLevelCm, $reservoirLevelLiters,
                $dissolvedOxygenMgL, $airflow, $waterFlow, $orpMv, $topOffLiters, $addbackEc, $solutionChange,
                $ppfdMol, $co2Ppm, $createdAtUtc, $updatedAtUtc
            );
            SELECT last_insert_rowid();
        """;
        AddMeasurementParameters(command, measurement);
        return Convert.ToInt32((long)command.ExecuteScalar()!);
    }

    public void UpdateMeasurement(Measurement measurement)
    {
        measurement.UpdatedAtUtc = DateTime.UtcNow;
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Measurements SET
                TakenAt = $takenAt,
                Stage = $stage,
                Source = $source,
                Notes = $notes,
                AirTemperatureC = $airTemperatureC,
                HumidityPercent = $humidityPercent,
                HeightCm = $heightCm,
                WaterAmountMl = $waterAmountMl,
                RunoffAmountMl = $runoffAmountMl,
                IrrigationPh = $irrigationPh,
                IrrigationEc = $irrigationEc,
                DrainPh = $drainPh,
                DrainEc = $drainEc,
                ReservoirPh = $reservoirPh,
                ReservoirEc = $reservoirEc,
                ReservoirWaterTempC = $reservoirWaterTempC,
                ReservoirLevelCm = $reservoirLevelCm,
                ReservoirLevelLiters = $reservoirLevelLiters,
                DissolvedOxygenMgL = $dissolvedOxygenMgL,
                AirflowAtLeafMPerMin = $airflow,
                WaterFlow = $waterFlow,
                OrpMv = $orpMv,
                TopOffLiters = $topOffLiters,
                AddbackEc = $addbackEc,
                SolutionChange = $solutionChange,
                PpfdMol = $ppfdMol,
                Co2Ppm = $co2Ppm,
                UpdatedAtUtc = $updatedAtUtc
            WHERE Id = $id;
        """;
        AddMeasurementParameters(command, measurement);
        command.Parameters.AddWithValue("$id", measurement.Id);
        command.ExecuteNonQuery();
    }

    public void DeleteMeasurement(int id)
    {
        using var connection = OpenConnection();
        using var photoCommand = connection.CreateCommand();
        photoCommand.CommandText = "SELECT RelativePath FROM Photos WHERE MeasurementId = $id;";
        photoCommand.Parameters.AddWithValue("$id", id);

        var filesToDelete = new List<string>();
        using (var reader = photoCommand.ExecuteReader())
        {
            while (reader.Read())
            {
                var relativePath = reader["RelativePath"]?.ToString();
                if (!string.IsNullOrWhiteSpace(relativePath))
                {
                    filesToDelete.Add(relativePath);
                }
            }
        }

        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Measurements WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();

        foreach (var relativePath in filesToDelete)
        {
            if (TryResolveUploadPath(relativePath, out var physicalPath) && File.Exists(physicalPath))
            {
                File.Delete(physicalPath);
            }
        }
    }

    internal static Measurement MapMeasurement(SqliteDataReader reader)
    {
        return new Measurement
        {
            Id = Convert.ToInt32((long)reader["Id"]),
            GrowId = Convert.ToInt32((long)reader["GrowId"]),
            TakenAt = ParseStoredDateTime(reader["TakenAt"]?.ToString()) ?? DateTime.Now,
            Stage = ParseEnum(reader["Stage"]?.ToString(), GrowStage.Veg),
            Source = ParseEnum(reader["Source"]?.ToString(), ValueOrigin.Manual),
            Notes = NullString(reader["Notes"]),
            AirTemperatureC = NullableDouble(reader["AirTemperatureC"]),
            HumidityPercent = NullableDouble(reader["HumidityPercent"]),
            HeightCm = NullableDouble(reader["HeightCm"]),
            WaterAmountMl = NullableDouble(reader["WaterAmountMl"]),
            RunoffAmountMl = NullableDouble(reader["RunoffAmountMl"]),
            IrrigationPh = NullableDouble(reader["IrrigationPh"]),
            IrrigationEc = NullableDouble(reader["IrrigationEc"]),
            DrainPh = NullableDouble(reader["DrainPh"]),
            DrainEc = NullableDouble(reader["DrainEc"]),
            ReservoirPh = NullableDouble(reader["ReservoirPh"]),
            ReservoirEc = NullableDouble(reader["ReservoirEc"]),
            ReservoirWaterTempC = NullableDouble(reader["ReservoirWaterTempC"]),
            ReservoirLevelCm = NullableDouble(reader["ReservoirLevelCm"]),
            ReservoirLevelLiters = NullableDouble(reader["ReservoirLevelLiters"]),
            DissolvedOxygenMgL = NullableDouble(reader["DissolvedOxygenMgL"]),
            AirflowAtLeafMPerMin = HasColumn(reader, "AirflowAtLeafMPerMin") ? NullableDouble(reader["AirflowAtLeafMPerMin"]) : null,
            WaterFlow = HasColumn(reader, "WaterFlow") && Enum.TryParse<WaterFlowLevel>(NullString(reader["WaterFlow"]), out var flow) ? flow : null,
            OrpMv = NullableDouble(reader["OrpMv"]),
            TopOffLiters = NullableDouble(reader["TopOffLiters"]),
            AddbackEc = NullableDouble(reader["AddbackEc"]),
            SolutionChange = reader["SolutionChange"] is not DBNull && Convert.ToInt32(reader["SolutionChange"], CultureInfo.InvariantCulture) == 1,
            PpfdMol = NullableDouble(reader["PpfdMol"]),
            Co2Ppm = NullableDouble(reader["Co2Ppm"]),
            CreatedAtUtc = ParseStoredUtcDateTime(reader["CreatedAtUtc"]?.ToString()) ?? DateTime.UtcNow,
            UpdatedAtUtc = ParseStoredUtcDateTime(reader["UpdatedAtUtc"]?.ToString()) ?? DateTime.UtcNow
        };
    }

    private static void AddMeasurementParameters(SqliteCommand command, Measurement measurement)
    {
        command.Parameters.AddWithValue("$growId", measurement.GrowId);
        command.Parameters.AddWithValue("$takenAt", ToStorage(measurement.TakenAt));
        command.Parameters.AddWithValue("$stage", measurement.Stage.ToString());
        command.Parameters.AddWithValue("$source", measurement.Source.ToString());
        command.Parameters.AddWithValue("$notes", (object?)measurement.Notes ?? DBNull.Value);
        AddNullable(command, "$airTemperatureC", measurement.AirTemperatureC);
        AddNullable(command, "$humidityPercent", measurement.HumidityPercent);
        AddNullable(command, "$heightCm", measurement.HeightCm);
        AddNullable(command, "$waterAmountMl", measurement.WaterAmountMl);
        AddNullable(command, "$runoffAmountMl", measurement.RunoffAmountMl);
        AddNullable(command, "$irrigationPh", measurement.IrrigationPh);
        AddNullable(command, "$irrigationEc", measurement.IrrigationEc);
        AddNullable(command, "$drainPh", measurement.DrainPh);
        AddNullable(command, "$drainEc", measurement.DrainEc);
        AddNullable(command, "$reservoirPh", measurement.ReservoirPh);
        AddNullable(command, "$reservoirEc", measurement.ReservoirEc);
        AddNullable(command, "$reservoirWaterTempC", measurement.ReservoirWaterTempC);
        AddNullable(command, "$reservoirLevelCm", measurement.ReservoirLevelCm);
        AddNullable(command, "$reservoirLevelLiters", measurement.ReservoirLevelLiters);
        AddNullable(command, "$dissolvedOxygenMgL", measurement.DissolvedOxygenMgL);
        AddNullable(command, "$airflow", measurement.AirflowAtLeafMPerMin);
        command.Parameters.AddWithValue("$waterFlow", (object?)measurement.WaterFlow?.ToString() ?? DBNull.Value);
        AddNullable(command, "$orpMv", measurement.OrpMv);
        AddNullable(command, "$topOffLiters", measurement.TopOffLiters);
        AddNullable(command, "$addbackEc", measurement.AddbackEc);
        command.Parameters.AddWithValue("$solutionChange", measurement.SolutionChange ? 1 : 0);
        AddNullable(command, "$ppfdMol", measurement.PpfdMol);
        AddNullable(command, "$co2Ppm", measurement.Co2Ppm);
        command.Parameters.AddWithValue("$createdAtUtc", ToStorageUtc(measurement.CreatedAtUtc));
        command.Parameters.AddWithValue("$updatedAtUtc", ToStorageUtc(measurement.UpdatedAtUtc));
    }

}
