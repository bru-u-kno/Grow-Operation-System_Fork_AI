using System.Globalization;
using System.Text.Json;
using GrowDiary.Web.Models;
using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Infrastructure;

public sealed class GrowCoreRepository : RepositoryBase
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web);

    public GrowCoreRepository(AppPaths paths) : base(paths)
    {
    }

    public DashboardStats GetDashboardStats()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                COUNT(*) AS TotalGrows,
                COALESCE(SUM(CASE WHEN Status IN ('Planning','Running') THEN 1 ELSE 0 END), 0) AS ActiveGrows,
                COALESCE(SUM(CASE WHEN Status IN ('Completed','Aborted') THEN 1 ELSE 0 END), 0) AS ArchivedGrows,
                COALESCE(SUM(CASE WHEN Status = 'Aborted' THEN 1 ELSE 0 END), 0) AS AbortedGrows,
                (SELECT COUNT(*) FROM Measurements) AS Measurements,
                (SELECT COUNT(*) FROM Photos) AS Photos
            FROM Grows;
        """;
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return new DashboardStats();
        return new DashboardStats
        {
            TotalGrows = Convert.ToInt32(reader["TotalGrows"]),
            ActiveGrows = Convert.ToInt32(reader["ActiveGrows"]),
            ArchivedGrows = Convert.ToInt32(reader["ArchivedGrows"]),
            AbortedGrows = Convert.ToInt32(reader["AbortedGrows"]),
            Measurements = Convert.ToInt32(reader["Measurements"]),
            Photos = Convert.ToInt32(reader["Photos"])
        };
    }

    public List<GrowRun> GetActiveGrows(string? search = null)
        => GetGrows("WHERE g.Status IN ('Planning','Running')" + SearchClause(search), search);

    public List<GrowRun> GetArchivedGrows(string? search = null)
        => GetGrows("WHERE g.Status IN ('Completed','Aborted')" + SearchClause(search), search);

    public List<GrowRun> GetActiveGrowsForTent(int tentId)
        => GetGrows("WHERE g.TentId = $tentId AND g.Status IN ('Planning','Running')", null, tentId);

    /// <summary>
    /// Die laufenden Grows MEHRERER Zelte in einem Zug — nach Zelt sortiert.
    /// </summary>
    /// <remarks>
    /// Damit <c>GetTents()</c> die Listen fuellen kann, ohne je Zelt eine
    /// eigene Abfrage zu fahren. Die Zeltuebersicht ist eine der haeufigsten
    /// Seiten; N+1 waere dort teuer bezahlt.
    /// </remarks>
    public Dictionary<int, List<GrowRun>> GetActiveGrowsByTent(IReadOnlyCollection<int> tentIds)
    {
        var nach = new Dictionary<int, List<GrowRun>>();
        if (tentIds.Count == 0) return nach;

        // Die Ids kommen aus der Datenbank, nicht vom Nutzer — eine
        // Zahlenliste laesst sich hier gefahrlos einsetzen. Parameter waeren
        // sauberer, aber SQLite kennt kein Array-Binden.
        var liste = string.Join(",", tentIds.Select(id => id.ToString(CultureInfo.InvariantCulture)));
        foreach (var grow in GetGrows(
            $"WHERE g.TentId IN ({liste}) AND g.Status IN ('Planning','Running')", null))
        {
            if (grow.TentId is not { } zeltId) continue;
            if (!nach.TryGetValue(zeltId, out var fuerZelt)) nach[zeltId] = fuerZelt = [];
            fuerZelt.Add(grow);
        }

        return nach;
    }

    public List<GrowRun> GetArchivedGrowsForTent(int tentId)
        => GetGrows("WHERE g.TentId = $tentId AND g.Status IN ('Completed','Aborted')", null, tentId);

    public List<GrowRun> GetAllGrows()
        => GetGrows(string.Empty, null);

    public GrowRun? GetGrow(int id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT g.*, t.Name AS TentName, hs.Name AS HydroSetupName,
                   sw.FlowerWeeksMin AS SortenWochenMin, sw.FlowerWeeksMax AS SortenWochenMax,
                   (SELECT COUNT(*) FROM Measurements m WHERE m.GrowId = g.Id) AS MeasurementCount,
                   (SELECT RelativePath FROM Photos p WHERE p.GrowId = g.Id ORDER BY p.TakenAtUtc DESC LIMIT 1) AS LatestPhotoPath,
                   -- Die Sorten, die WIRKLICH in diesem Grow stehen: aus den
                   -- Pflanzen, nicht aus dem einen Feld Grows.Strain. Getrennt
                   -- mit char(31), weil ein Sortenname ein Komma enthalten darf
                   -- ("Gorilla Glue #4, Cut B").
                   (SELECT group_concat(x.Name, char(31)) FROM (
                        SELECT DISTINCT s.Name AS Name
                        FROM PlantInstances pi
                        JOIN Strains s ON s.Id = pi.StrainId
                        WHERE pi.GrowId = g.Id
                        ORDER BY s.Name
                    ) x) AS PflanzenSorten,
                   -- Tragen ALLE erfassten Pflanzen die Hauptsorte des Laufs?
                   --
                   -- Nur dann gehoert der Zuechter des Laufs zu dem, was
                   -- angezeigt wird. Die Oberflaeche hat das zuerst ueber den
                   -- NAMEN geraten (Teilzeichenkette) -- und lag bei
                   -- "Northern Lights" gegen "Northern Lights Auto" prompt
                   -- falsch: richtige Sorte, Zuechter der anderen. Hier steht
                   -- die Id gegen die Id, und die luegt nicht.
                   (SELECT CASE WHEN COUNT(*) = 0 THEN 0 ELSE
                        MIN(CASE WHEN pi2.StrainId IS NOT NULL AND pi2.StrainId = g.StrainId THEN 1 ELSE 0 END)
                    END
                    FROM PlantInstances pi2 WHERE pi2.GrowId = g.Id) AS NurHauptsorte
            FROM Grows g
            LEFT JOIN Tents t ON t.Id = g.TentId
            LEFT JOIN GrowSystems hs ON hs.Id = g.SystemId
            LEFT JOIN Strains sw ON sw.Id = g.StrainId
            WHERE g.Id = $id
            LIMIT 1;
        """;
        command.Parameters.AddWithValue("$id", id);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var grow = MapGrow(reader);
        grow.LatestMeasurement = GetLatestMeasurement(id);
        using (var reservoirConnection = OpenConnection())
        {
            var reservoir = GetLatestReservoirBatch(reservoirConnection, new[] { id }).GetValueOrDefault(id);
            grow.LatestReservoirPh = reservoir.Ph;
            grow.LatestReservoirEc = reservoir.Ec;
        }
        return grow;
    }

    public Tent? GetTentForGrow(int growId)
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
            INNER JOIN Grows g ON g.TentId = t.Id
            WHERE g.Id = $growId
            LIMIT 1;
        """;
        command.Parameters.AddWithValue("$growId", growId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapTent(reader) : null;
    }

    public int CreateGrow(GrowRun grow)
    {
        grow.CreatedAtUtc = DateTime.UtcNow;
        grow.UpdatedAtUtc = DateTime.UtcNow;
        CaptureGrowSnapshots(grow);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Grows
            (
                TentId, SystemId, SetupId, Name, StrainId, Strain, Breeder, Status, MediumType, FeedingStyle, HydroStyle, MediumDetail,
                Environment, Light, ContainerSize, ReservoirSize, IrrigationStyle, IrrigationType, WaterSource, FeedProgramId, UseFeedChartTargets, NightRampEnabled, NightRampFloorC,
                SeedType, StartMaterial, GerminationMethod, CloneSource, CloneIsRooted,
                BreederFlowerWeeksMin, BreederFlowerWeeksMax, BreederWochenEigen, PlannedVegDays, SetpointProfileId, PlantCount, PhenoNumber,
                PropagationMedium, HasChiller, EntryPoint, DaysAlreadyInPhase,
                AutoflowerDaysSinceGermination, FlipDate, GerminatedAt, RootedAt, VegStartedAt, FinishStartedAt,
                Nutrients, Notes, StartDate, EndDate, CreatedAtUtc, UpdatedAtUtc,
                TentSnapshotJson, HydroSetupSnapshotJson, SnapshotsCapturedAtUtc
            )
            VALUES
            (
                $tentId, $systemId, $setupId, $name, $strainId, $strain, $breeder, $status, $mediumType, $feedingStyle, $hydroStyle, $mediumDetail,
                $environment, $light, $containerSize, $reservoirSize, $irrigationStyle, $irrigationType, $waterSource, $feedProgramId, $useFeedChartTargets, $nightRampEnabled, $nightRampFloorC,
                $seedType, $startMaterial, $germinationMethod, $cloneSource, $cloneIsRooted,
                $breederFlowerWeeksMin, $breederFlowerWeeksMax, $breederWochenEigen, $plannedVegDays, $setpointProfileId, $plantCount, $phenoNumber,
                $propagationMedium, $hasChiller, $entryPoint, $daysAlreadyInPhase,
                $autoflowerDaysSinceGermination, $flipDate, $germinatedAt, $rootedAt, $vegStartedAt, $finishStartedAt,
                $nutrients, $notes, $startDate, $endDate, $createdAtUtc, $updatedAtUtc,
                $tentSnapshotJson, $hydroSetupSnapshotJson, $snapshotsCapturedAtUtc
            );
            SELECT last_insert_rowid();
        """;
        AddGrowParameters(command, grow);
        AddGrowSnapshotParameters(command, grow);
        return Convert.ToInt32((long)command.ExecuteScalar()!);
    }

    public void UpdateGrow(GrowRun grow)
    {
        grow.UpdatedAtUtc = DateTime.UtcNow;

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Grows
            SET
                TentId = $tentId,
                SystemId = $systemId,
                SetupId = $setupId,
                Name = $name,
                Strain = $strain,
                Breeder = $breeder,
                Status = $status,
                MediumType = $mediumType,
                FeedingStyle = $feedingStyle,
                HydroStyle = $hydroStyle,
                MediumDetail = $mediumDetail,
                Environment = $environment,
                Light = $light,
                ContainerSize = $containerSize,
                ReservoirSize = $reservoirSize,
                IrrigationStyle = $irrigationStyle,
                IrrigationType = $irrigationType,
                WaterSource = $waterSource,
                FeedProgramId = $feedProgramId,
                UseFeedChartTargets = $useFeedChartTargets,
                NightRampEnabled = $nightRampEnabled,
                NightRampFloorC = $nightRampFloorC,
                SeedType = $seedType,
                StartMaterial = $startMaterial,
                GerminationMethod = $germinationMethod,
                CloneSource = $cloneSource,
                CloneIsRooted = $cloneIsRooted,
                BreederFlowerWeeksMin = $breederFlowerWeeksMin,
                BreederFlowerWeeksMax = $breederFlowerWeeksMax,
                BreederWochenEigen = $breederWochenEigen,
                PlannedVegDays = $plannedVegDays,
                SetpointProfileId = $setpointProfileId,
                StrainId = $strainId,
                PlantCount = $plantCount,
                PhenoNumber = $phenoNumber,
                PropagationMedium = $propagationMedium,
                HasChiller = $hasChiller,
                EntryPoint = $entryPoint,
                DaysAlreadyInPhase = $daysAlreadyInPhase,
                AutoflowerDaysSinceGermination = $autoflowerDaysSinceGermination,
                FlipDate = $flipDate,
                GerminatedAt = $germinatedAt,
                RootedAt = $rootedAt,
                VegStartedAt = $vegStartedAt,
                FinishStartedAt = $finishStartedAt,
                Nutrients = $nutrients,
                Notes = $notes,
                StartDate = $startDate,
                EndDate = $endDate,
                UpdatedAtUtc = $updatedAtUtc
            WHERE Id = $id;
        """;
        AddGrowParameters(command, grow);
        command.Parameters.AddWithValue("$id", grow.Id);
        command.ExecuteNonQuery();
    }

    public void DeleteGrow(int id)
    {
        using var connection = OpenConnection();
        using var photoCommand = connection.CreateCommand();
        photoCommand.CommandText = "SELECT RelativePath FROM Photos WHERE GrowId = $id;";
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

        // Die Warnungen gehen mit.
        //
        // RiskEvents hat keinen Fremdschluessel auf Grows, und `PRAGMA
        // foreign_keys` ist per Vorgabe aus — beim Loeschen blieben die
        // Warnungen also stehen. Sie gehoerten dann zu einem Grow, den es nicht
        // mehr gibt: fuer immer offen auf der Aufgabenseite, von keinem Abgleich
        // je wieder angefasst, weil der nur ueber die aktiven Grows laeuft.
        // Gefunden am 18.08.2026 in der Testdatenbank — zwei solche Leichen.
        using var risks = connection.CreateCommand();
        risks.CommandText = "DELETE FROM RiskEvents WHERE GrowId = $id;";
        risks.Parameters.AddWithValue("$id", id);
        risks.ExecuteNonQuery();

        /* Die Pflanzen gehen mit — aber nur die, die es ohne den Lauf nicht gaebe.
         *
         * Genau dieselbe Luecke wie bei den Warnungen darueber, nur eine Tabelle
         * weiter: `DELETE FROM Grows` liess die Pflanzen stehen, und ihr GrowId
         * zeigte danach auf einen Lauf, den es nicht mehr gibt. Gefunden am
         * 01.09.2026 im Testbestand — 92 solche Leichen, und jeder volle
         * E2E-Lauf legte zwei weitere dazu.
         *
         * Unterschieden wird nach Rolle: eine Produktionspflanze existiert fuer
         * diesen Durchgang und endet mit ihm. Eine Mutter, ein Steckling oder
         * eine Pflanze in Quarantaene gehoert dem AUFBAU und ueberlebt den Lauf
         * — sie verliert nur ihren Bezug darauf. Wer alles loeschte, naehme dem
         * Nutzer seine Mutterpflanze mit dem Grow. */
        using var produktion = connection.CreateCommand();
        produktion.CommandText =
            "DELETE FROM PlantInstances WHERE GrowId = $id AND PlantRole = 'Production';";
        produktion.Parameters.AddWithValue("$id", id);
        produktion.ExecuteNonQuery();

        using var uebrige = connection.CreateCommand();
        uebrige.CommandText = "UPDATE PlantInstances SET GrowId = NULL WHERE GrowId = $id;";
        uebrige.Parameters.AddWithValue("$id", id);
        uebrige.ExecuteNonQuery();

        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Grows WHERE Id = $id;";
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

    private List<GrowRun> GetGrows(string whereClause, string? search, int? tentId = null)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT g.*, t.Name AS TentName, hs.Name AS HydroSetupName,
                   sw.FlowerWeeksMin AS SortenWochenMin, sw.FlowerWeeksMax AS SortenWochenMax,
                   (SELECT COUNT(*) FROM Measurements m WHERE m.GrowId = g.Id) AS MeasurementCount,
                   (SELECT RelativePath FROM Photos p WHERE p.GrowId = g.Id ORDER BY p.TakenAtUtc DESC LIMIT 1) AS LatestPhotoPath,
                   -- Die Sorten, die WIRKLICH in diesem Grow stehen: aus den
                   -- Pflanzen, nicht aus dem einen Feld Grows.Strain. Getrennt
                   -- mit char(31), weil ein Sortenname ein Komma enthalten darf
                   -- ("Gorilla Glue #4, Cut B").
                   (SELECT group_concat(x.Name, char(31)) FROM (
                        SELECT DISTINCT s.Name AS Name
                        FROM PlantInstances pi
                        JOIN Strains s ON s.Id = pi.StrainId
                        WHERE pi.GrowId = g.Id
                        ORDER BY s.Name
                    ) x) AS PflanzenSorten,
                   -- Tragen ALLE erfassten Pflanzen die Hauptsorte des Laufs?
                   --
                   -- Nur dann gehoert der Zuechter des Laufs zu dem, was
                   -- angezeigt wird. Die Oberflaeche hat das zuerst ueber den
                   -- NAMEN geraten (Teilzeichenkette) -- und lag bei
                   -- "Northern Lights" gegen "Northern Lights Auto" prompt
                   -- falsch: richtige Sorte, Zuechter der anderen. Hier steht
                   -- die Id gegen die Id, und die luegt nicht.
                   (SELECT CASE WHEN COUNT(*) = 0 THEN 0 ELSE
                        MIN(CASE WHEN pi2.StrainId IS NOT NULL AND pi2.StrainId = g.StrainId THEN 1 ELSE 0 END)
                    END
                    FROM PlantInstances pi2 WHERE pi2.GrowId = g.Id) AS NurHauptsorte
            FROM Grows g
            LEFT JOIN Tents t ON t.Id = g.TentId
            LEFT JOIN GrowSystems hs ON hs.Id = g.SystemId
            LEFT JOIN Strains sw ON sw.Id = g.StrainId
            {whereClause}
            ORDER BY g.StartDate DESC, g.Id DESC;
        """;

        if (!string.IsNullOrWhiteSpace(search))
        {
            command.Parameters.AddWithValue("$search", $"%{search.Trim()}%");
        }
        if (tentId.HasValue)
        {
            command.Parameters.AddWithValue("$tentId", tentId.Value);
        }

        var items = new List<GrowRun>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                items.Add(MapGrow(reader));
            }
        }

        if (items.Count > 0)
        {
            var latestMeasurements = GetLatestMeasurementsBatch(connection, items.Select(g => g.Id));
            var latestReservoir = GetLatestReservoirBatch(connection, items.Select(g => g.Id));
            foreach (var grow in items)
            {
                grow.LatestMeasurement = latestMeasurements.GetValueOrDefault(grow.Id);
                var reservoir = latestReservoir.GetValueOrDefault(grow.Id);
                grow.LatestReservoirPh = reservoir.Ph;
                grow.LatestReservoirEc = reservoir.Ec;
            }
        }

        return items;
    }

    private Measurement? GetLatestMeasurement(int growId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM Measurements WHERE GrowId = $growId ORDER BY TakenAt DESC, Id DESC LIMIT 1;";
        command.Parameters.AddWithValue("$growId", growId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MeasurementRepository.MapMeasurement(reader) : null;
    }

    private static Dictionary<int, Measurement> GetLatestMeasurementsBatch(SqliteConnection connection, IEnumerable<int> growIds)
    {
        var ids = growIds.ToList();
        if (ids.Count == 0) return [];

        var placeholders = string.Join(", ", ids.Select((_, i) => $"$p{i}"));
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            WITH ranked AS (
                SELECT *, ROW_NUMBER() OVER (PARTITION BY GrowId ORDER BY TakenAt DESC, Id DESC) AS rn
                FROM Measurements
                WHERE GrowId IN ({placeholders})
            )
            SELECT * FROM ranked WHERE rn = 1;
        """;
        for (var i = 0; i < ids.Count; i++)
        {
            command.Parameters.AddWithValue($"$p{i}", ids[i]);
        }

        var result = new Dictionary<int, Measurement>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var m = MeasurementRepository.MapMeasurement(reader);
            result[m.GrowId] = m;
        }
        return result;
    }

    // Per grow: the latest NON-NULL ReservoirPh / ReservoirEc across all measurements.
    private static Dictionary<int, (double? Ph, double? Ec)> GetLatestReservoirBatch(SqliteConnection connection, IEnumerable<int> growIds)
    {
        var ids = growIds.Distinct().ToList();
        var result = new Dictionary<int, (double? Ph, double? Ec)>();
        if (ids.Count == 0) return result;

        var placeholders = string.Join(", ", ids.Select((_, i) => $"$p{i}"));

        void LoadColumn(string column, bool isPh)
        {
            using var command = connection.CreateCommand();
            // column is a hardcoded literal ("ReservoirPh"/"ReservoirEc") — not user input.
            command.CommandText = $"""
                SELECT GrowId, value FROM (
                    SELECT GrowId, {column} AS value,
                           ROW_NUMBER() OVER (PARTITION BY GrowId ORDER BY TakenAt DESC, Id DESC) AS rn
                    FROM Measurements
                    WHERE GrowId IN ({placeholders}) AND {column} IS NOT NULL
                ) WHERE rn = 1;
            """;
            for (var i = 0; i < ids.Count; i++)
            {
                command.Parameters.AddWithValue($"$p{i}", ids[i]);
            }

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var growId = reader.GetInt32(0);
                double? value = reader.IsDBNull(1) ? null : reader.GetDouble(1);
                var existing = result.GetValueOrDefault(growId);
                result[growId] = isPh ? (value, existing.Ec) : (existing.Ph, value);
            }
        }

        LoadColumn("ReservoirPh", true);
        LoadColumn("ReservoirEc", false);
        return result;
    }

    private static string SearchClause(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return string.Empty;
        }
        // Auch die Sorten der PFLANZEN — ein Grow kann N Sorten fuehren, und
        // eine, die nur an einer Pflanze haengt, war ueber die Suche bisher
        // nicht zu finden.
        return " AND (g.Name LIKE $search OR g.Strain LIKE $search OR g.Breeder LIKE $search"
            + " OR g.Nutrients LIKE $search OR t.Name LIKE $search"
            + " OR EXISTS (SELECT 1 FROM PlantInstances pi JOIN Strains s ON s.Id = pi.StrainId"
            + "            WHERE pi.GrowId = g.Id AND s.Name LIKE $search))";
    }

    private void CaptureGrowSnapshots(GrowRun grow)
    {
        var capturedAtUtc = DateTime.UtcNow;
        var capturedAny = !string.IsNullOrWhiteSpace(grow.TentSnapshotJson)
                          || !string.IsNullOrWhiteSpace(grow.HydroSetupSnapshotJson);

        if (grow.TentId.HasValue && string.IsNullOrWhiteSpace(grow.TentSnapshotJson))
        {
            var tent = GetTentForSnapshot(grow.TentId.Value);
            if (tent is not null)
            {
                grow.TentSnapshotJson = JsonSerializer.Serialize(ToGrowTentSnapshot(tent), SnapshotJsonOptions);
                capturedAny = true;
            }
        }

        if (grow.SystemId.HasValue && string.IsNullOrWhiteSpace(grow.HydroSetupSnapshotJson))
        {
            var hydroSetup = GetHydroSetupForSnapshot(grow.SystemId.Value);
            if (hydroSetup is not null)
            {
                grow.HydroSetupSnapshotJson = JsonSerializer.Serialize(ToGrowHydroSetupSnapshot(hydroSetup), SnapshotJsonOptions);
                capturedAny = true;
            }
        }

        grow.SnapshotsCapturedAtUtc = capturedAny
            ? grow.SnapshotsCapturedAtUtc ?? capturedAtUtc
            : null;
    }

    private Tent? GetTentForSnapshot(int id)
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

    private List<TentSensor> GetTentSensors(int tentId)
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

    private GrowSystem? GetHydroSetupForSnapshot(int id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.*, t.Name AS TentName,
                   (SELECT COUNT(*) FROM Grows g WHERE g.SystemId = s.Id AND g.Status IN ('Planning','Running')) AS ActiveGrowCount
            FROM GrowSystems s
            LEFT JOIN Tents t ON t.Id = s.TentId
            WHERE s.Id = $id
            LIMIT 1;
        """;
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapGrowSystem(reader) : null;
    }

    private static GrowTentSnapshot ToGrowTentSnapshot(Tent tent)
        => new(
            Id: tent.Id,
            Name: tent.Name,
            Kind: tent.Kind,
            TentType: tent.TentType,
            Status: tent.Status,
            Notes: tent.Notes,
            DisplayOrder: tent.DisplayOrder,
            AccentColor: tent.AccentColor,
            WidthCm: tent.WidthCm,
            DepthCm: tent.DepthCm,
            TentHeightCm: tent.TentHeightCm,
            LightType: tent.LightType,
            LightWatt: tent.LightWatt,
            LightController: tent.LightController,
            LightControllerEntityId: tent.LightControllerEntityId,
            ExhaustFanCount: tent.ExhaustFanCount,
            ExhaustM3h: tent.ExhaustM3h,
            CirculationFanCount: tent.CirculationFanCount,
            HvacController: tent.HvacController,
            HvacControllerEntityId: tent.HvacControllerEntityId,
            Co2Available: tent.Co2Available,
            CameraEntityId: tent.CameraEntityId,
            Sensors: tent.Sensors.Select(sensor => new GrowTentSensorSnapshot(
                Id: sensor.Id,
                MetricType: sensor.MetricType,
                HaEntityId: sensor.HaEntityId,
                DisplayLabel: sensor.DisplayLabel,
                IsActive: sensor.IsActive)).ToList());

    private static GrowHydroSetupSnapshot ToGrowHydroSetupSnapshot(GrowSystem hydroSetup)
        => new(
            Id: hydroSetup.Id,
            TentId: hydroSetup.TentId,
            TentName: hydroSetup.TentName,
            Name: hydroSetup.Name,
            HydroStyle: hydroSetup.HydroStyle,
            PotCount: hydroSetup.PotCount,
            PotSizeLiters: hydroSetup.PotSizeLiters,
            ReservoirLiters: hydroSetup.ReservoirLiters,
            TotalVolumeLiters: CalculateHydroSetupTotalVolumeLiters(hydroSetup.PotCount, hydroSetup.PotSizeLiters, hydroSetup.ReservoirLiters),
            Status: hydroSetup.Status,
            LayoutType: hydroSetup.LayoutType,
            ReservoirPosition: hydroSetup.ReservoirPosition,
            HasCirculationPump: hydroSetup.HasCirculationPump,
            CirculationPumpNotes: hydroSetup.CirculationPumpNotes,
            HasAirPump: hydroSetup.HasAirPump,
            AirPumpNotes: hydroSetup.AirPumpNotes,
            AirStoneCount: hydroSetup.AirStoneCount,
            HasChiller: hydroSetup.HasChiller,
            HasUvSterilizer: hydroSetup.HasUvSterilizer,
            Notes: hydroSetup.Notes,
            DisplayOrder: hydroSetup.DisplayOrder,
            CreatedAtUtc: hydroSetup.CreatedAtUtc,
            UpdatedAtUtc: hydroSetup.UpdatedAtUtc);

    private static double? CalculateHydroSetupTotalVolumeLiters(int? potCount, double? potSizeLiters, double? reservoirLiters)
    {
        var total = (potCount ?? 0) * (potSizeLiters ?? 0) + (reservoirLiters ?? 0);
        return total > 0 ? Math.Round(total, 2) : null;
    }

    private static GrowRun MapGrow(SqliteDataReader reader)
    {
        var grow = new GrowRun
        {
            Id = Convert.ToInt32((long)reader["Id"]),
            TentId = reader["TentId"] is DBNull ? null : Convert.ToInt32((long)reader["TentId"]),
            SystemId = reader["SystemId"] is DBNull or null ? null : Convert.ToInt32((long)reader["SystemId"]),
            SetupId = reader["SetupId"] is DBNull or null ? null : Convert.ToInt32((long)reader["SetupId"]),
            TentName = NullString(reader["TentName"]),
            PflanzenSorten = HasColumn(reader, "PflanzenSorten") && NullString(reader["PflanzenSorten"]) is { } roh
                ? roh.Split('', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : [],
            NurHauptsorte = HasColumn(reader, "NurHauptsorte")
                && reader["NurHauptsorte"] is not DBNull
                && Convert.ToInt32(reader["NurHauptsorte"], CultureInfo.InvariantCulture) == 1,
            HydroSetupName = HasColumn(reader, "HydroSetupName") ? NullString(reader["HydroSetupName"]) : null,
            Name = reader["Name"]?.ToString() ?? string.Empty,
            Strain = NullString(reader["Strain"]),
            Breeder = NullString(reader["Breeder"]),
            Status = ParseEnum(reader["Status"]?.ToString(), GrowStatus.Planning),
            MediumType = ParseEnum(reader["MediumType"]?.ToString(), MediumType.Hydro),
            FeedingStyle = ParseEnum(reader["FeedingStyle"]?.ToString(), FeedingStyle.None),
            HydroStyle = ParseEnum(reader["HydroStyle"]?.ToString(), HydroStyle.None),
            MediumDetail = NullString(reader["MediumDetail"]),
            Environment = ParseEnum(reader["Environment"]?.ToString(), GrowEnvironment.Indoor),
            Light = NullString(reader["Light"]),
            ContainerSize = NullString(reader["ContainerSize"]),
            ReservoirSize = NullString(reader["ReservoirSize"]),
            IrrigationStyle = NullString(reader["IrrigationStyle"]),
            IrrigationType = ParseEnum(reader["IrrigationType"]?.ToString(), IrrigationType.ActiveHydro),
            WaterSource = ParseEnum(reader["WaterSource"]?.ToString(), WaterSource.Tap),
            FeedProgramId = !HasColumn(reader, "FeedProgramId") || reader["FeedProgramId"] is DBNull ? null : reader["FeedProgramId"]?.ToString(),
            UseFeedChartTargets = HasColumn(reader, "UseFeedChartTargets") && reader["UseFeedChartTargets"] is not DBNull && Convert.ToInt64(reader["UseFeedChartTargets"]) != 0,
            NightRampEnabled = HasColumn(reader, "NightRampEnabled") && reader["NightRampEnabled"] is not DBNull && Convert.ToInt64(reader["NightRampEnabled"]) != 0,
            NightRampFloorC = !HasColumn(reader, "NightRampFloorC") || reader["NightRampFloorC"] is DBNull ? null : Convert.ToDouble(reader["NightRampFloorC"]),
            SeedType = ParseEnum(reader["SeedType"]?.ToString(), SeedType.Feminized),
            StartMaterial = ParseEnum(reader["StartMaterial"]?.ToString(), StartMaterial.Seed),
            GerminationMethod = reader["GerminationMethod"] is DBNull or null ? null : Enum.TryParse<GerminationMethod>(reader["GerminationMethod"]?.ToString(), out var gm) ? gm : null,
            CloneSource = NullString(reader["CloneSource"]),
            CloneIsRooted = reader["CloneIsRooted"] is long cr && cr == 1,
            BreederFlowerWeeksMin = reader["BreederFlowerWeeksMin"] is DBNull or null ? null : Convert.ToInt32(reader["BreederFlowerWeeksMin"], CultureInfo.InvariantCulture),
            BreederFlowerWeeksMax = reader["BreederFlowerWeeksMax"] is DBNull or null ? null : Convert.ToInt32(reader["BreederFlowerWeeksMax"], CultureInfo.InvariantCulture),
            BreederWochenEigen = HasColumn(reader, "BreederWochenEigen") && reader["BreederWochenEigen"] is not (DBNull or null)
                && Convert.ToInt32(reader["BreederWochenEigen"], CultureInfo.InvariantCulture) == 1,
            SortenWochenMin = HasColumn(reader, "SortenWochenMin") && reader["SortenWochenMin"] is not (DBNull or null) ? Convert.ToInt32(reader["SortenWochenMin"], CultureInfo.InvariantCulture) : null,
            SortenWochenMax = HasColumn(reader, "SortenWochenMax") && reader["SortenWochenMax"] is not (DBNull or null) ? Convert.ToInt32(reader["SortenWochenMax"], CultureInfo.InvariantCulture) : null,
            StrainId = HasColumn(reader, "StrainId") && reader["StrainId"] is not (DBNull or null) ? Convert.ToInt32(reader["StrainId"], CultureInfo.InvariantCulture) : null,
            PlannedVegDays = HasColumn(reader, "PlannedVegDays") && reader["PlannedVegDays"] is not (DBNull or null) ? Convert.ToInt32(reader["PlannedVegDays"], CultureInfo.InvariantCulture) : null,
            SetpointProfileId = HasColumn(reader, "SetpointProfileId") ? NullString(reader["SetpointProfileId"]) : null,
            PlantCount = reader["PlantCount"] is DBNull or null ? null : Convert.ToInt32(reader["PlantCount"], CultureInfo.InvariantCulture),
            PhenoNumber = reader["PhenoNumber"] is DBNull or null ? null : Convert.ToInt32(reader["PhenoNumber"], CultureInfo.InvariantCulture),
            PropagationMedium = reader["PropagationMedium"] is DBNull or null ? null : Enum.TryParse<PropagationMedium>(reader["PropagationMedium"]?.ToString(), out var pm) ? pm : null,
            HasChiller = reader["HasChiller"] is long hc && hc == 1,
            EntryPoint = ParseEnum(reader["EntryPoint"]?.ToString(), GrowEntryPoint.Germination),
            DaysAlreadyInPhase = reader["DaysAlreadyInPhase"] is DBNull or null ? null : Convert.ToInt32(reader["DaysAlreadyInPhase"], CultureInfo.InvariantCulture),
            AutoflowerDaysSinceGermination = reader["AutoflowerDaysSinceGermination"] is DBNull or null ? null : Convert.ToInt32(reader["AutoflowerDaysSinceGermination"], CultureInfo.InvariantCulture),
            FlipDate = ParseStoredDate(reader["FlipDate"]?.ToString()),
            GerminatedAt = reader["GerminatedAt"] is DBNull or null ? null : (DateTime.TryParse(reader["GerminatedAt"]?.ToString(), out var ga) ? ga : (DateTime?)null),
            RootedAt = reader["RootedAt"] is DBNull or null ? null : (DateTime.TryParse(reader["RootedAt"]?.ToString(), out var ra) ? ra : (DateTime?)null),
            VegStartedAt = !HasColumn(reader, "VegStartedAt") || reader["VegStartedAt"] is DBNull or null
                ? null
                : (DateTime.TryParse(reader["VegStartedAt"]?.ToString(), out var vs) ? vs : (DateTime?)null),
            FinishStartedAt = !HasColumn(reader, "FinishStartedAt") || reader["FinishStartedAt"] is DBNull or null
                ? null
                : (DateTime.TryParse(reader["FinishStartedAt"]?.ToString(), out var fs) ? fs : (DateTime?)null),
            Nutrients = NullString(reader["Nutrients"]),
            Notes = NullString(reader["Notes"]),
            StartDate = ParseStoredDate(reader["StartDate"]?.ToString()) ?? DateTime.Today,
            EndDate = ParseStoredDate(reader["EndDate"]?.ToString()),
            CreatedAtUtc = ParseStoredUtcDateTime(reader["CreatedAtUtc"]?.ToString()) ?? DateTime.UtcNow,
            UpdatedAtUtc = ParseStoredUtcDateTime(reader["UpdatedAtUtc"]?.ToString()) ?? DateTime.UtcNow,
            TentSnapshotJson = HasColumn(reader, "TentSnapshotJson") ? NullString(reader["TentSnapshotJson"]) : null,
            HydroSetupSnapshotJson = HasColumn(reader, "HydroSetupSnapshotJson") ? NullString(reader["HydroSetupSnapshotJson"]) : null,
            SnapshotsCapturedAtUtc = ParseStoredDateTimeIfColumn(reader, "SnapshotsCapturedAtUtc"),
            MeasurementCount = reader["MeasurementCount"] is DBNull ? 0 : Convert.ToInt32(reader["MeasurementCount"], CultureInfo.InvariantCulture),
            LatestPhotoPath = NullString(reader["LatestPhotoPath"])
        };
        return WirksameZuechterwochen(grow);
    }

    /// <summary>
    /// Die Zuechterwochen, mit denen gerechnet wird: der eigene Wert des Grows,
    /// sonst der der Sorte. Hier und nur hier eingesetzt, damit Finish-Beginn,
    /// Plan-Woche, Kosten und Oberflaeche nie auseinanderlaufen.
    /// </summary>
    internal static GrowRun WirksameZuechterwochen(GrowRun grow)
    {
        // Ohne Sorte kann es nichts zu folgen geben.
        grow.BreederWochenEigen |= grow.StrainId is null || grow.SeedType == SeedType.Autoflower;
        var sorteHatAngabe = grow.SortenWochenMin is not null || grow.SortenWochenMax is not null;
        if (!grow.BreederWochenEigen && sorteHatAngabe)
        {
            grow.BreederFlowerWeeksMin = grow.SortenWochenMin;
            grow.BreederFlowerWeeksMax = grow.SortenWochenMax;
        }
        return grow;
    }

    private static Tent MapTent(SqliteDataReader reader)
    {
        return new Tent
        {
            Id = Convert.ToInt32((long)reader["Id"]),
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
            LightController = Enum.TryParse<LightControllerType>(NullString(reader["LightController"]), out var lc) ? lc : (LightControllerType?)null,
            LightControllerEntityId = NullString(reader["LightControllerEntityId"]),
            ExhaustFanCount = reader["ExhaustFanCount"] is DBNull or null ? null : Convert.ToInt32(reader["ExhaustFanCount"], CultureInfo.InvariantCulture),
            ExhaustM3h = reader["ExhaustM3h"] is DBNull or null ? null : Convert.ToInt32(reader["ExhaustM3h"], CultureInfo.InvariantCulture),
            CirculationFanCount = reader["CirculationFanCount"] is DBNull or null ? null : Convert.ToInt32(reader["CirculationFanCount"], CultureInfo.InvariantCulture),
            HvacController = Enum.TryParse<HvacControllerType>(NullString(reader["HvacController"]), out var hc) ? hc : (HvacControllerType?)null,
            HvacControllerEntityId = NullString(reader["HvacControllerEntityId"]),
            Co2Available = reader["Co2Available"] is not DBNull and not null && Convert.ToInt32(reader["Co2Available"], CultureInfo.InvariantCulture) == 1,
            CameraEntityId = NullString(reader["CameraEntityId"]),
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

    private static GrowSystem MapGrowSystem(SqliteDataReader reader)
    {
        return new GrowSystem
        {
            Id = Convert.ToInt32((long)reader["Id"], CultureInfo.InvariantCulture),
            TentId = reader["TentId"] is DBNull or null ? null : Convert.ToInt32(reader["TentId"], CultureInfo.InvariantCulture),
            TentName = HasColumn(reader, "TentName") ? NullString(reader["TentName"]) : null,
            Name = reader["Name"]?.ToString() ?? string.Empty,
            HydroStyle = reader["HydroStyle"]?.ToString() ?? string.Empty,
            PotCount = reader["PotCount"] is DBNull or null ? null : Convert.ToInt32(reader["PotCount"], CultureInfo.InvariantCulture),
            PotSizeLiters = reader["PotSizeLiters"] is DBNull or null ? null : Convert.ToDouble(reader["PotSizeLiters"], CultureInfo.InvariantCulture),
            ReservoirLiters = reader["ReservoirLiters"] is DBNull or null ? null : Convert.ToDouble(reader["ReservoirLiters"], CultureInfo.InvariantCulture),
            Status = ParseEnum(NullString(reader["Status"]), HydroSetupStatus.Active),
            LayoutType = ParseEnum(NullString(reader["LayoutType"]), HydroSetupLayoutType.SingleBucket),
            ReservoirPosition = ParseEnum(NullString(reader["ReservoirPosition"]), ReservoirPosition.None),
            HasCirculationPump = Convert.ToInt32(reader["HasCirculationPump"], CultureInfo.InvariantCulture) != 0,
            CirculationPumpNotes = NullString(reader["CirculationPumpNotes"]),
            HasAirPump = Convert.ToInt32(reader["HasAirPump"], CultureInfo.InvariantCulture) != 0,
            AirPumpNotes = NullString(reader["AirPumpNotes"]),
            AirStoneCount = reader["AirStoneCount"] is DBNull or null ? null : Convert.ToInt32(reader["AirStoneCount"], CultureInfo.InvariantCulture),
            HasChiller = Convert.ToInt32(reader["HasChiller"], CultureInfo.InvariantCulture) != 0,
            HasUvSterilizer = Convert.ToInt32(reader["HasUvSterilizer"], CultureInfo.InvariantCulture) != 0,
            Notes = NullString(reader["Notes"]),
            DisplayOrder = Convert.ToInt32(reader["DisplayOrder"], CultureInfo.InvariantCulture),
            CreatedAtUtc = ParseStoredUtcDateTime(reader["CreatedAtUtc"]?.ToString()) ?? DateTime.UtcNow,
            UpdatedAtUtc = ParseStoredUtcDateTime(NullString(reader["UpdatedAtUtc"])) ?? ParseStoredUtcDateTime(reader["CreatedAtUtc"]?.ToString()) ?? DateTime.UtcNow,
            ActiveGrowCount = reader["ActiveGrowCount"] is DBNull ? 0 : Convert.ToInt32(reader["ActiveGrowCount"], CultureInfo.InvariantCulture)
        };
    }

    private static void AddGrowParameters(SqliteCommand command, GrowRun grow)
    {
        command.Parameters.AddWithValue("$tentId", (object?)grow.TentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$systemId", (object?)grow.SystemId ?? DBNull.Value);
        command.Parameters.AddWithValue("$setupId", (object?)grow.SetupId ?? DBNull.Value);
        command.Parameters.AddWithValue("$name", grow.Name);
        command.Parameters.AddWithValue("$strain", (object?)grow.Strain ?? DBNull.Value);
        command.Parameters.AddWithValue("$breeder", (object?)grow.Breeder ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", grow.Status.ToString());
        command.Parameters.AddWithValue("$mediumType", grow.MediumType.ToString());
        command.Parameters.AddWithValue("$feedingStyle", grow.FeedingStyle.ToString());
        command.Parameters.AddWithValue("$hydroStyle", grow.HydroStyle.ToString());
        command.Parameters.AddWithValue("$mediumDetail", (object?)grow.MediumDetail ?? DBNull.Value);
        command.Parameters.AddWithValue("$environment", grow.Environment.ToString());
        command.Parameters.AddWithValue("$light", (object?)grow.Light ?? DBNull.Value);
        command.Parameters.AddWithValue("$containerSize", (object?)grow.ContainerSize ?? DBNull.Value);
        command.Parameters.AddWithValue("$reservoirSize", (object?)grow.ReservoirSize ?? DBNull.Value);
        command.Parameters.AddWithValue("$irrigationStyle", (object?)grow.IrrigationStyle ?? DBNull.Value);
        command.Parameters.AddWithValue("$irrigationType", grow.IrrigationType.ToString());
        command.Parameters.AddWithValue("$waterSource", grow.WaterSource.ToString());
        command.Parameters.AddWithValue("$feedProgramId", (object?)grow.FeedProgramId ?? DBNull.Value);
        command.Parameters.AddWithValue("$useFeedChartTargets", grow.UseFeedChartTargets ? 1 : 0);
        command.Parameters.AddWithValue("$nightRampEnabled", grow.NightRampEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$nightRampFloorC", (object?)grow.NightRampFloorC ?? DBNull.Value);
        command.Parameters.AddWithValue("$seedType", grow.SeedType.ToString());
        command.Parameters.AddWithValue("$startMaterial", grow.StartMaterial.ToString());
        command.Parameters.AddWithValue("$germinationMethod", (object?)grow.GerminationMethod?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$cloneSource", (object?)grow.CloneSource ?? DBNull.Value);
        command.Parameters.AddWithValue("$cloneIsRooted", grow.CloneIsRooted ? 1 : 0);
        // Folgt das Grow der Sorte, steht im Grow nichts: gespeichert wird nur,
        // was bewusst gesetzt ist. Sonst wuerde jedes Speichern (auch eines
        // anderen Feldes) die Wochen der Sorte wieder ins Grow kopieren.
        var eigeneWochen = grow.BreederWochenEigen || grow.StrainId is null;
        command.Parameters.AddWithValue("$breederFlowerWeeksMin", eigeneWochen ? (object?)grow.BreederFlowerWeeksMin ?? DBNull.Value : DBNull.Value);
        command.Parameters.AddWithValue("$breederFlowerWeeksMax", eigeneWochen ? (object?)grow.BreederFlowerWeeksMax ?? DBNull.Value : DBNull.Value);
        command.Parameters.AddWithValue("$breederWochenEigen", eigeneWochen ? 1 : 0);
        command.Parameters.AddWithValue("$plannedVegDays", (object?)grow.PlannedVegDays ?? DBNull.Value);
        command.Parameters.AddWithValue("$setpointProfileId", (object?)grow.SetpointProfileId ?? DBNull.Value);
        command.Parameters.AddWithValue("$strainId", (object?)grow.StrainId ?? DBNull.Value);
        command.Parameters.AddWithValue("$plantCount", (object?)grow.PlantCount ?? DBNull.Value);
        command.Parameters.AddWithValue("$phenoNumber", (object?)grow.PhenoNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$propagationMedium", (object?)grow.PropagationMedium?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$hasChiller", grow.HasChiller ? 1 : 0);
        command.Parameters.AddWithValue("$entryPoint", grow.EntryPoint.ToString());
        command.Parameters.AddWithValue("$daysAlreadyInPhase", (object?)grow.DaysAlreadyInPhase ?? DBNull.Value);
        command.Parameters.AddWithValue("$autoflowerDaysSinceGermination", (object?)grow.AutoflowerDaysSinceGermination ?? DBNull.Value);
        command.Parameters.AddWithValue("$flipDate", grow.FlipDate.HasValue ? ToStorage(grow.FlipDate.Value.Date) : DBNull.Value);
        command.Parameters.AddWithValue("$germinatedAt", grow.GerminatedAt.HasValue ? ToStorageUtc(grow.GerminatedAt.Value) : DBNull.Value);
        command.Parameters.AddWithValue("$rootedAt", grow.RootedAt.HasValue ? ToStorageUtc(grow.RootedAt.Value) : DBNull.Value);
        command.Parameters.AddWithValue("$vegStartedAt", grow.VegStartedAt.HasValue ? ToStorageUtc(grow.VegStartedAt.Value) : DBNull.Value);
        command.Parameters.AddWithValue("$finishStartedAt", grow.FinishStartedAt.HasValue ? ToStorageUtc(grow.FinishStartedAt.Value) : DBNull.Value);
        command.Parameters.AddWithValue("$nutrients", (object?)grow.Nutrients ?? DBNull.Value);
        command.Parameters.AddWithValue("$notes", (object?)grow.Notes ?? DBNull.Value);
        command.Parameters.AddWithValue("$startDate", ToStorage(grow.StartDate.Date));
        command.Parameters.AddWithValue("$endDate", grow.EndDate.HasValue ? ToStorage(grow.EndDate.Value.Date) : DBNull.Value);
        command.Parameters.AddWithValue("$createdAtUtc", ToStorageUtc(grow.CreatedAtUtc));
        command.Parameters.AddWithValue("$updatedAtUtc", ToStorageUtc(grow.UpdatedAtUtc));
    }

    private static void AddGrowSnapshotParameters(SqliteCommand command, GrowRun grow)
    {
        command.Parameters.AddWithValue("$tentSnapshotJson", (object?)grow.TentSnapshotJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$hydroSetupSnapshotJson", (object?)grow.HydroSetupSnapshotJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$snapshotsCapturedAtUtc", grow.SnapshotsCapturedAtUtc.HasValue ? ToStorageUtc(grow.SnapshotsCapturedAtUtc.Value) : DBNull.Value);
    }

}
