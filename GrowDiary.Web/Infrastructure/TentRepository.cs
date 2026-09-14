using System.Globalization;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Infrastructure;

public sealed class TentRepository : RepositoryBase
{
    public TentRepository(AppPaths paths) : base(paths)
    {
    }

    public List<Tent> GetTents(bool includeArchived = false)
    {
        using var connection = OpenConnection();
        using var tentCommand = connection.CreateCommand();
        tentCommand.CommandText = """
            SELECT t.*,
                   (SELECT COUNT(*) FROM Grows g WHERE g.TentId = t.Id AND g.Status IN ('Planning','Running')) AS ActiveGrowCount,
                   (SELECT COUNT(*) FROM Grows g WHERE g.TentId = t.Id AND g.Status IN ('Completed','Aborted')) AS ArchivedGrowCount,
                   (SELECT COUNT(*) FROM Setups s WHERE s.TentId = t.Id AND s.Status IN ('Planning','Active')) AS ActiveSetupCount,
                   (SELECT COUNT(*) FROM Setups s WHERE s.TentId = t.Id AND s.Status = 'Archived') AS ArchivedSetupCount
            FROM Tents t
            WHERE ($includeArchived = 1 OR t.Status != 'Archived')
            ORDER BY t.DisplayOrder, t.Name;
        """;
        tentCommand.Parameters.AddWithValue("$includeArchived", includeArchived ? 1 : 0);

        var tents = new List<Tent>();
        using (var reader = tentCommand.ExecuteReader())
        {
            while (reader.Read())
            {
                tents.Add(MapTent(reader));
            }
        }

        if (tents.Count == 0)
        {
            return tents;
        }

        var sensorsByTentId = LoadSensorsByTentIds(connection, tents.Select(t => t.Id).ToList());
        foreach (var tent in tents)
        {
            tent.Sensors = sensorsByTentId.TryGetValue(tent.Id, out var sensors) ? sensors : new();
        }

        return tents;
    }

    public Tent? GetTent(int id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT t.*,
                   (SELECT COUNT(*) FROM Grows g WHERE g.TentId = t.Id AND g.Status IN ('Planning','Running')) AS ActiveGrowCount,
                   (SELECT COUNT(*) FROM Grows g WHERE g.TentId = t.Id AND g.Status IN ('Completed','Aborted')) AS ArchivedGrowCount,
                   (SELECT COUNT(*) FROM Setups s WHERE s.TentId = t.Id AND s.Status IN ('Planning','Active')) AS ActiveSetupCount,
                   (SELECT COUNT(*) FROM Setups s WHERE s.TentId = t.Id AND s.Status = 'Archived') AS ArchivedSetupCount
            FROM Tents t
            WHERE t.Id = $id
            LIMIT 1;
        """;
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var tent = MapTent(reader);
        tent.Sensors = GetTentSensors(id);
        return tent;
    }

    public Tent CreateTent(string name)
        => CreateTent(new Tent { Name = name });

    public Tent CreateTent(Tent tent)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Tents (
                Name, Kind, TentType, Status, Notes, DisplayOrder, AccentColor,
                WidthCm, DepthCm, TentHeightCm, LightType, LightWatt,
                LightController, LightControllerEntityId, ExhaustFanCount, ExhaustM3h,
                CirculationFanCount, HvacController, HvacControllerEntityId,
                Co2Available, HasCo2Enrichment, CameraEntityId, CameraEntityIds, WaterTargetEntityId, ChillerSwitchEntityId, ChillerControlEnabled, ChillerHysteresisC, ChillerMinRunMinutes, ChillerMinPauseMinutes, ChillerMaxReadingAgeMinutes, LeafTempOffsetC, LeafOffsetSyncService, LeafOffsetSyncPort, CreatedAtUtc, UpdatedAtUtc
            )
            VALUES (
                $name, $kind, $tentType, $status, $notes, $displayOrder, $accentColor,
                $widthCm, $depthCm, $tentHeightCm, $lightType, $lightWatt,
                $lightController, $lightControllerEntityId, $exhaustFanCount, $exhaustM3h,
                $circulationFanCount, $hvacController, $hvacControllerEntityId,
                $co2Available, $hasCo2Enrichment, $cameraEntityId, $cameraEntityIds, $waterTargetEntityId, $chillerSwitchEntityId, $chillerControlEnabled, $chillerHysteresisC, $chillerMinRunMinutes, $chillerMinPauseMinutes, $chillerMaxReadingAgeMinutes, $leafTempOffsetC, $leafOffsetSyncService, $leafOffsetSyncPort, datetime('now'), datetime('now')
            );
            SELECT last_insert_rowid();
        """;
        if (string.IsNullOrWhiteSpace(tent.Name))
        {
            throw new InvalidOperationException("Tent name must not be empty.");
        }

        tent.Name = tent.Name.Trim();
        tent.Kind = string.IsNullOrWhiteSpace(tent.Kind) ? "Grow Tent" : tent.Kind.Trim();
        tent.AccentColor = string.IsNullOrWhiteSpace(tent.AccentColor) ? "#69b578" : tent.AccentColor.Trim();
        AddTentParameters(command, tent);
        var id = Convert.ToInt32((long)(command.ExecuteScalar() ?? 0L));
        return GetTent(id) ?? new Tent { Id = id, Name = tent.Name, Kind = tent.Kind, TentType = tent.TentType };
    }

    public void UpdateTent(Tent tent)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Tents SET
                Name = $name,
                Kind = $kind,
                TentType = $tentType,
                Status = $status,
                Notes = $notes,
                DisplayOrder = $displayOrder,
                AccentColor = $accentColor,
                WidthCm = $widthCm,
                DepthCm = $depthCm,
                TentHeightCm = $tentHeightCm,
                LightType = $lightType,
                LightWatt = $lightWatt,
                LightController = $lightController,
                LightControllerEntityId = $lightControllerEntityId,
                ExhaustFanCount = $exhaustFanCount,
                ExhaustM3h = $exhaustM3h,
                CirculationFanCount = $circulationFanCount,
                HvacController = $hvacController,
                HvacControllerEntityId = $hvacControllerEntityId,
                Co2Available = $co2Available,
                HasCo2Enrichment = $hasCo2Enrichment,
                CameraEntityId = $cameraEntityId,
                CameraEntityIds = $cameraEntityIds,
                WaterTargetEntityId = $waterTargetEntityId,
                ChillerSwitchEntityId = $chillerSwitchEntityId,
                ChillerControlEnabled = $chillerControlEnabled,
                ChillerHysteresisC = $chillerHysteresisC,
                ChillerMinRunMinutes = $chillerMinRunMinutes,
                ChillerMinPauseMinutes = $chillerMinPauseMinutes,
                ChillerMaxReadingAgeMinutes = $chillerMaxReadingAgeMinutes,
                LeafTempOffsetC = $leafTempOffsetC,
                LeafOffsetSyncService = $leafOffsetSyncService,
                LeafOffsetSyncPort = $leafOffsetSyncPort,
                UpdatedAtUtc = datetime('now')
            WHERE Id = $id;
        """;
        AddTentParameters(command, tent);
        command.Parameters.AddWithValue("$id", tent.Id);
        command.ExecuteNonQuery();
    }

    public void DeleteTent(int id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Tents WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void DeleteTentWithCleanup(int id)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        Execute(commandText: """
            UPDATE HardwareItems
               SET TentId = NULL,
                   HydroSetupId = CASE
                       WHEN HydroSetupId IN (SELECT Id FROM GrowSystems WHERE TentId = $id) THEN NULL
                       ELSE HydroSetupId
                   END,
                   UpdatedAtUtc = datetime('now')
             WHERE TentId = $id
                OR HydroSetupId IN (SELECT Id FROM GrowSystems WHERE TentId = $id);
            """);

        Execute(commandText: """
            UPDATE RiskEvents
               SET TentId = NULL,
                   TentSensorId = NULL,
                   HardwareItemId = CASE
                       WHEN HardwareItemId IN (SELECT Id FROM HardwareItems WHERE TentId = $id) THEN NULL
                       ELSE HardwareItemId
                   END,
                   UpdatedAtUtc = datetime('now')
             WHERE TentId = $id
                OR TentSensorId IN (SELECT Id FROM TentSensors WHERE TentId = $id);
            """);

        Execute(commandText: "UPDATE Grows SET TentId = NULL, UpdatedAtUtc = datetime('now') WHERE TentId = $id;");
        Execute(commandText: "UPDATE Setups SET TentId = NULL, UpdatedAtUtc = datetime('now') WHERE TentId = $id;");
        Execute(commandText: "UPDATE GrowSystems SET TentId = NULL, UpdatedAtUtc = datetime('now') WHERE TentId = $id;");
        Execute(commandText: "UPDATE AutoMeasurementConfigs SET TentId = NULL WHERE TentId = $id;");
        Execute(commandText: "DELETE FROM Tents WHERE Id = $id;");

        transaction.Commit();

        void Execute(string commandText)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = commandText;
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }
    }

    public bool HasTentDependencies(int id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM Grows WHERE TentId = $id) +
                (SELECT COUNT(*) FROM Setups WHERE TentId = $id) +
                (SELECT COUNT(*) FROM GrowSystems WHERE TentId = $id) +
                (SELECT COUNT(*) FROM TentSensors WHERE TentId = $id) +
                (SELECT COUNT(*) FROM LightSchedules WHERE TentId = $id) +
                (SELECT COUNT(*) FROM LightTransitionEvents WHERE TentId = $id) +
                (SELECT COUNT(*) FROM AutoMeasurementConfigs WHERE TentId = $id) +
                (SELECT COUNT(*) FROM TentSensorReadings WHERE TentId = $id) +
                (SELECT COUNT(*) FROM TentSensorSnapshots WHERE TentId = $id) +
                (SELECT COUNT(*) FROM TentSensorDailyStats WHERE TentId = $id);
            """;
        command.Parameters.AddWithValue("$id", id);
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture) > 0;
    }

    public void ArchiveTent(int id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Tents SET Status = 'Archived', UpdatedAtUtc = datetime('now') WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public List<TentSensor> GetTentSensors(int tentId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM TentSensors WHERE TentId = $tentId ORDER BY Id;";
        command.Parameters.AddWithValue("$tentId", tentId);
        var list = new List<TentSensor>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(MapTentSensor(reader));
        }
        return list;
    }

    public TentSensor? GetTentSensor(int id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM TentSensors WHERE Id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapTentSensor(reader) : null;
    }

    public TentSensor? GetTentSensorByMetric(int tentId, SensorMetricType metricType)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT * FROM TentSensors
            WHERE TentId = $tentId AND MetricType = $metricType
            ORDER BY Id LIMIT 1;
            """;
        command.Parameters.AddWithValue("$tentId", tentId);
        command.Parameters.AddWithValue("$metricType", metricType.ToString());
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapTentSensor(reader) : null;
    }

    public TentSensor AddTentSensor(TentSensor sensor)
    {
        sensor.CreatedAtUtc = DateTime.UtcNow;
        sensor.UpdatedAtUtc = DateTime.UtcNow;
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO TentSensors (TentId, MetricType, HaEntityId, DisplayLabel, IsActive, CreatedAtUtc, UpdatedAtUtc)
            VALUES ($tentId, $metricType, $haEntityId, $displayLabel, $isActive, $createdAtUtc, $updatedAtUtc);
            SELECT last_insert_rowid();
            """;
        AddTentSensorParameters(command, sensor);
        sensor.Id = Convert.ToInt32((long)command.ExecuteScalar()!, CultureInfo.InvariantCulture);
        return sensor;
    }

    public void UpdateTentSensor(TentSensor sensor)
    {
        sensor.UpdatedAtUtc = DateTime.UtcNow;
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE TentSensors SET
                MetricType = $metricType,
                HaEntityId = $haEntityId,
                DisplayLabel = $displayLabel,
                IsActive = $isActive,
                UpdatedAtUtc = $updatedAtUtc
            WHERE Id = $id;
            """;
        AddTentSensorParameters(command, sensor);
        command.Parameters.AddWithValue("$id", sensor.Id);
        command.ExecuteNonQuery();
    }

    public void ReplaceTentSensors(int tentId, IReadOnlyCollection<TentSensor> sensors)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        // Die Sensor-Zeilen bekommen gleich neue Ids. HardwareItems heilt der
        // TentSensorHardwareSyncService nach dem Speichern; RiskEvents heilt
        // niemand — ein stehen gebliebener Verweis liesse spaeter jedes
        // Bestaetigen/Loesen des Ereignisses an der Existenzpruefung platzen.
        using (var unlinkCommand = connection.CreateCommand())
        {
            unlinkCommand.Transaction = transaction;
            unlinkCommand.CommandText = """
                UPDATE RiskEvents
                   SET TentSensorId = NULL,
                       UpdatedAtUtc = datetime('now')
                 WHERE TentSensorId IN (SELECT Id FROM TentSensors WHERE TentId = $tentId);
                """;
            unlinkCommand.Parameters.AddWithValue("$tentId", tentId);
            unlinkCommand.ExecuteNonQuery();
        }

        using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM TentSensors WHERE TentId = $tentId;";
            deleteCommand.Parameters.AddWithValue("$tentId", tentId);
            deleteCommand.ExecuteNonQuery();
        }

        foreach (var sensor in sensors)
        {
            var stored = new TentSensor
            {
                TentId = tentId,
                MetricType = sensor.MetricType,
                HaEntityId = sensor.HaEntityId,
                DisplayLabel = sensor.DisplayLabel,
                IsActive = sensor.IsActive,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = """
                INSERT INTO TentSensors (TentId, MetricType, HaEntityId, DisplayLabel, IsActive, CreatedAtUtc, UpdatedAtUtc)
                VALUES ($tentId, $metricType, $haEntityId, $displayLabel, $isActive, $createdAtUtc, $updatedAtUtc);
                """;
            AddTentSensorParameters(insertCommand, stored);
            insertCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void DeleteTentSensor(int sensorId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM TentSensors WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", sensorId);
        command.ExecuteNonQuery();
    }

    public void AddTentSensorSnapshot(TentSensorSnapshot snapshot, TimeSpan? dedupeWindow = null)
    {
        var window = dedupeWindow ?? TimeSpan.FromMinutes(4);
        using var connection = OpenConnection();
        using var dedupe = connection.CreateCommand();
        dedupe.CommandText = """
            SELECT COUNT(*) FROM TentSensorSnapshots
            WHERE TentId = $tentId AND MetricKey = $metricKey AND CapturedAtUtc >= $threshold;
        """;
        dedupe.Parameters.AddWithValue("$tentId", snapshot.TentId);
        dedupe.Parameters.AddWithValue("$metricKey", snapshot.MetricKey);
        dedupe.Parameters.AddWithValue("$threshold", ToStorageUtc(snapshot.CapturedAtUtc.Subtract(window)));
        var recentCount = Convert.ToInt32(dedupe.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
        if (recentCount > 0)
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO TentSensorSnapshots (TentId, MetricKey, Value, Unit, CapturedAtUtc)
            VALUES ($tentId, $metricKey, $value, $unit, $capturedAtUtc);
        """;
        command.Parameters.AddWithValue("$tentId", snapshot.TentId);
        command.Parameters.AddWithValue("$metricKey", snapshot.MetricKey);
        command.Parameters.AddWithValue("$value", snapshot.Value);
        command.Parameters.AddWithValue("$unit", (object?)snapshot.Unit ?? DBNull.Value);
        command.Parameters.AddWithValue("$capturedAtUtc", ToStorageUtc(snapshot.CapturedAtUtc));
        command.ExecuteNonQuery();
    }

    public List<TentSensorSnapshot> GetTentSensorSnapshots(int tentId, IEnumerable<string>? metricKeys = null, int limitPerMetric = 48)
    {
        var keys = metricKeys?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                   ?? ["temperature", "humidity", "vpd", "reservoir-ph", "reservoir-ec", "reservoir-level", "reservoir-temp"];
        if (keys.Count == 0)
        {
            return [];
        }

        using var connection = OpenConnection();
        var placeholders = string.Join(", ", keys.Select((_, i) => $"$k{i}"));
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            WITH ranked AS (
                SELECT *, ROW_NUMBER() OVER (PARTITION BY MetricKey ORDER BY CapturedAtUtc DESC) AS rn
                FROM TentSensorSnapshots
                WHERE TentId = $tentId AND MetricKey IN ({placeholders})
            )
            SELECT * FROM ranked WHERE rn <= $limit;
        """;
        command.Parameters.AddWithValue("$tentId", tentId);
        command.Parameters.AddWithValue("$limit", limitPerMetric);
        for (var i = 0; i < keys.Count; i++)
        {
            command.Parameters.AddWithValue($"$k{i}", keys[i]);
        }

        var items = new List<TentSensorSnapshot>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(MapTentSensorSnapshot(reader));
        }
        return items;
    }

    private static Dictionary<int, List<TentSensor>> LoadSensorsByTentIds(SqliteConnection connection, List<int> tentIds)
    {
        var placeholders = string.Join(", ", tentIds.Select((_, i) => $"$s{i}"));
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT * FROM TentSensors WHERE TentId IN ({placeholders}) ORDER BY TentId, Id;";
        for (var i = 0; i < tentIds.Count; i++)
        {
            cmd.Parameters.AddWithValue($"$s{i}", tentIds[i]);
        }

        var result = new Dictionary<int, List<TentSensor>>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var sensor = MapTentSensor(reader);
            if (!result.ContainsKey(sensor.TentId))
            {
                result[sensor.TentId] = new();
            }
            result[sensor.TentId].Add(sensor);
        }
        return result;
    }

    private static Tent MapTent(SqliteDataReader reader)
    {
        return new Tent
        {
            Id = Convert.ToInt32((long)reader["Id"], CultureInfo.InvariantCulture),
            Name = reader["Name"]?.ToString() ?? string.Empty,
            Kind = reader["Kind"]?.ToString() ?? "Grow Tent",
            TentType = ParseEnum(NullString(reader["TentType"]), TentType.MultiPurpose),
            Status = ParseEnum(NullString(reader["Status"]), TentStatus.Active),
            Notes = NullString(reader["Notes"]),
            DisplayOrder = Convert.ToInt32(reader["DisplayOrder"], CultureInfo.InvariantCulture),
            AccentColor = reader["AccentColor"]?.ToString() ?? "#69b578",
            WidthCm = reader["WidthCm"] is DBNull or null ? null : Convert.ToInt32(reader["WidthCm"], CultureInfo.InvariantCulture),
            DepthCm = reader["DepthCm"] is DBNull or null ? null : Convert.ToInt32(reader["DepthCm"], CultureInfo.InvariantCulture),
            TentHeightCm = reader["TentHeightCm"] is DBNull or null ? null : Convert.ToInt32(reader["TentHeightCm"], CultureInfo.InvariantCulture),
            LightType = NullString(reader["LightType"]),
            LightWatt = reader["LightWatt"] is DBNull or null ? null : Convert.ToInt32(reader["LightWatt"], CultureInfo.InvariantCulture),
            LightController = Enum.TryParse<LightControllerType>(NullString(reader["LightController"]), out var lc) ? lc : null,
            LightControllerEntityId = NullString(reader["LightControllerEntityId"]),
            ExhaustFanCount = reader["ExhaustFanCount"] is DBNull or null ? null : Convert.ToInt32(reader["ExhaustFanCount"], CultureInfo.InvariantCulture),
            ExhaustM3h = reader["ExhaustM3h"] is DBNull or null ? null : Convert.ToInt32(reader["ExhaustM3h"], CultureInfo.InvariantCulture),
            CirculationFanCount = reader["CirculationFanCount"] is DBNull or null ? null : Convert.ToInt32(reader["CirculationFanCount"], CultureInfo.InvariantCulture),
            HvacController = Enum.TryParse<HvacControllerType>(NullString(reader["HvacController"]), out var hc) ? hc : null,
            HvacControllerEntityId = NullString(reader["HvacControllerEntityId"]),
            Co2Available = reader["Co2Available"] is not DBNull and not null && Convert.ToInt32(reader["Co2Available"], CultureInfo.InvariantCulture) == 1,
            HasCo2Enrichment = HasColumn(reader, "HasCo2Enrichment") && reader["HasCo2Enrichment"] is not DBNull and not null && Convert.ToInt32(reader["HasCo2Enrichment"], CultureInfo.InvariantCulture) == 1,
            CameraEntityId = NullString(reader["CameraEntityId"]),
            CameraEntityIds = HasColumn(reader, "CameraEntityIds") ? NullString(reader["CameraEntityIds"]) : null,
            WaterTargetEntityId = HasColumn(reader, "WaterTargetEntityId") ? NullString(reader["WaterTargetEntityId"]) : null,
            ChillerSwitchEntityId = HasColumn(reader, "ChillerSwitchEntityId") ? NullString(reader["ChillerSwitchEntityId"]) : null,
            ChillerControlEnabled = HasColumn(reader, "ChillerControlEnabled") && Convert.ToInt64(reader["ChillerControlEnabled"]) == 1,
            ChillerHysteresisC = HasColumn(reader, "ChillerHysteresisC") ? Convert.ToDouble(reader["ChillerHysteresisC"]) : KuehlerService.StandardHystereseC,
            ChillerMinRunMinutes = HasColumn(reader, "ChillerMinRunMinutes") ? Convert.ToInt32(reader["ChillerMinRunMinutes"]) : KuehlerService.StandardMindestlaufMinuten,
            ChillerMinPauseMinutes = HasColumn(reader, "ChillerMinPauseMinutes") ? Convert.ToInt32(reader["ChillerMinPauseMinutes"]) : KuehlerService.StandardMindestpauseMinuten,
            ChillerMaxReadingAgeMinutes = HasColumn(reader, "ChillerMaxReadingAgeMinutes") ? Convert.ToInt32(reader["ChillerMaxReadingAgeMinutes"]) : KuehlerService.StandardHoechstalterMinuten,
            LeafTempOffsetC = HasColumn(reader, "LeafTempOffsetC") ? Convert.ToDouble(reader["LeafTempOffsetC"] is DBNull ? 0d : reader["LeafTempOffsetC"]) : 0d,
            LeafOffsetSyncService = HasColumn(reader, "LeafOffsetSyncService") ? NullString(reader["LeafOffsetSyncService"]) : null,
            LeafOffsetSyncPort = HasColumn(reader, "LeafOffsetSyncPort") && reader["LeafOffsetSyncPort"] is not DBNull
                ? Convert.ToInt32(reader["LeafOffsetSyncPort"])
                : Tent.DefaultLeafOffsetSyncPort,
            ActiveGrowCount = reader["ActiveGrowCount"] is DBNull ? 0 : Convert.ToInt32(reader["ActiveGrowCount"], CultureInfo.InvariantCulture),
            ArchivedGrowCount = reader["ArchivedGrowCount"] is DBNull ? 0 : Convert.ToInt32(reader["ArchivedGrowCount"], CultureInfo.InvariantCulture),
            ActiveSetupCount = reader["ActiveSetupCount"] is DBNull ? 0 : Convert.ToInt32(reader["ActiveSetupCount"], CultureInfo.InvariantCulture),
            ArchivedSetupCount = reader["ArchivedSetupCount"] is DBNull ? 0 : Convert.ToInt32(reader["ArchivedSetupCount"], CultureInfo.InvariantCulture)
        };
    }

    private static TentSensor MapTentSensor(SqliteDataReader reader)
    {
        return new TentSensor
        {
            Id = Convert.ToInt32((long)reader["Id"], CultureInfo.InvariantCulture),
            TentId = Convert.ToInt32((long)reader["TentId"], CultureInfo.InvariantCulture),
            MetricType = ParseEnum(reader["MetricType"]?.ToString(), SensorMetricType.AirTemperature),
            HaEntityId = reader["HaEntityId"]?.ToString() ?? string.Empty,
            DisplayLabel = NullString(reader["DisplayLabel"]),
            IsActive = reader["IsActive"] is not DBNull and not null && Convert.ToInt32(reader["IsActive"], CultureInfo.InvariantCulture) == 1,
            CreatedAtUtc = ParseStoredUtcDateTime(reader["CreatedAtUtc"]?.ToString()) ?? DateTime.UtcNow,
            UpdatedAtUtc = ParseStoredUtcDateTime(reader["UpdatedAtUtc"]?.ToString()) ?? DateTime.UtcNow
        };
    }

    private static TentSensorSnapshot MapTentSensorSnapshot(SqliteDataReader reader)
    {
        return new TentSensorSnapshot
        {
            Id = Convert.ToInt32((long)reader["Id"], CultureInfo.InvariantCulture),
            TentId = Convert.ToInt32((long)reader["TentId"], CultureInfo.InvariantCulture),
            MetricKey = reader["MetricKey"]?.ToString() ?? string.Empty,
            Value = Convert.ToDouble(reader["Value"], CultureInfo.InvariantCulture),
            Unit = NullString(reader["Unit"]),
            CapturedAtUtc = ParseStoredUtcDateTime(reader["CapturedAtUtc"]?.ToString()) ?? DateTime.UtcNow
        };
    }

    private static void AddTentParameters(SqliteCommand command, Tent tent)
    {
        command.Parameters.AddWithValue("$name", tent.Name);
        command.Parameters.AddWithValue("$kind", tent.Kind);
        command.Parameters.AddWithValue("$tentType", tent.TentType.ToString());
        command.Parameters.AddWithValue("$status", tent.Status.ToString());
        command.Parameters.AddWithValue("$notes", (object?)tent.Notes ?? DBNull.Value);
        command.Parameters.AddWithValue("$displayOrder", tent.DisplayOrder);
        command.Parameters.AddWithValue("$accentColor", tent.AccentColor);
        command.Parameters.AddWithValue("$widthCm", (object?)tent.WidthCm ?? DBNull.Value);
        command.Parameters.AddWithValue("$depthCm", (object?)tent.DepthCm ?? DBNull.Value);
        command.Parameters.AddWithValue("$tentHeightCm", (object?)tent.TentHeightCm ?? DBNull.Value);
        command.Parameters.AddWithValue("$lightType", (object?)tent.LightType ?? DBNull.Value);
        command.Parameters.AddWithValue("$lightWatt", (object?)tent.LightWatt ?? DBNull.Value);
        command.Parameters.AddWithValue("$lightController", (object?)tent.LightController?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$lightControllerEntityId", (object?)tent.LightControllerEntityId ?? DBNull.Value);
        command.Parameters.AddWithValue("$exhaustFanCount", (object?)tent.ExhaustFanCount ?? DBNull.Value);
        command.Parameters.AddWithValue("$exhaustM3h", (object?)tent.ExhaustM3h ?? DBNull.Value);
        command.Parameters.AddWithValue("$circulationFanCount", (object?)tent.CirculationFanCount ?? DBNull.Value);
        command.Parameters.AddWithValue("$hvacController", (object?)tent.HvacController?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$hvacControllerEntityId", (object?)tent.HvacControllerEntityId ?? DBNull.Value);
        command.Parameters.AddWithValue("$co2Available", tent.Co2Available ? 1 : 0);
        command.Parameters.AddWithValue("$hasCo2Enrichment", tent.HasCo2Enrichment ? 1 : 0);
        command.Parameters.AddWithValue("$cameraEntityId", (object?)tent.CameraEntityId ?? DBNull.Value);
        command.Parameters.AddWithValue("$cameraEntityIds", (object?)tent.CameraEntityIds ?? DBNull.Value);
        command.Parameters.AddWithValue("$waterTargetEntityId", (object?)tent.WaterTargetEntityId ?? DBNull.Value);
        command.Parameters.AddWithValue("$chillerSwitchEntityId", (object?)tent.ChillerSwitchEntityId ?? DBNull.Value);
        command.Parameters.AddWithValue("$chillerControlEnabled", tent.ChillerControlEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$chillerHysteresisC", tent.ChillerHysteresisC);
        command.Parameters.AddWithValue("$chillerMinRunMinutes", tent.ChillerMinRunMinutes);
        command.Parameters.AddWithValue("$chillerMinPauseMinutes", tent.ChillerMinPauseMinutes);
        command.Parameters.AddWithValue("$chillerMaxReadingAgeMinutes", tent.ChillerMaxReadingAgeMinutes);
        command.Parameters.AddWithValue("$leafTempOffsetC", tent.LeafTempOffsetC);
        command.Parameters.AddWithValue("$leafOffsetSyncService", (object?)tent.LeafOffsetSyncService ?? DBNull.Value);
        command.Parameters.AddWithValue("$leafOffsetSyncPort", tent.LeafOffsetSyncPort);
    }

    private static void AddTentSensorParameters(SqliteCommand command, TentSensor sensor)
    {
        command.Parameters.AddWithValue("$tentId", sensor.TentId);
        command.Parameters.AddWithValue("$metricType", sensor.MetricType.ToString());
        command.Parameters.AddWithValue("$haEntityId", sensor.HaEntityId);
        command.Parameters.AddWithValue("$displayLabel", (object?)sensor.DisplayLabel ?? DBNull.Value);
        command.Parameters.AddWithValue("$isActive", sensor.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("$createdAtUtc", ToStorageUtc(sensor.CreatedAtUtc));
        command.Parameters.AddWithValue("$updatedAtUtc", ToStorageUtc(sensor.UpdatedAtUtc));
    }
}
