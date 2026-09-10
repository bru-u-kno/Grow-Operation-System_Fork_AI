using GrowDiary.Web.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace GrowDiary.Web.Infrastructure;

public sealed partial class DatabaseInitializer
{
    private void EnsureSchema()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL;";
        command.ExecuteNonQuery();

        command.CommandText = CoreSchemaSql;
        command.ExecuteNonQuery();

        EnsureSchemaMigrationMetadataColumns(connection);

        // Mehrere Messpunkte je Kalibrierung (01.09.2026) — eine pH-Sonde wird
        // gegen 4 UND 7 abgeglichen, oft auch 10. Bestehende Datenbanken
        // bekommen die Spalte hier nachtraeglich.
        EnsureColumn(connection, "CalibrationEvents", "PointsJson", "TEXT NULL");

        EnsureColumn(connection, "Setups", "CloneCounterTotal", "INTEGER NULL");
        EnsureColumn(connection, "Setups", "LastCloneCutAt", "TEXT NULL");
        EnsureColumn(connection, "Setups", "MotherHealthStatus", "TEXT NULL");
        EnsureColumn(connection, "Setups", "QuarantineStartedAt", "TEXT NULL");
        EnsureColumn(connection, "Setups", "QuarantinePlannedEndAt", "TEXT NULL");
        EnsureColumn(connection, "Setups", "QuarantineResult", "TEXT NULL");
        // Der Topf der Pflanze (ab 1) — Bestandsdatenbanken bekommen die
        // Spalte nachgeruestet, frische haben sie im CREATE TABLE.
        EnsureColumn(connection, "PlantInstances", "SiteIndex", "INTEGER NULL");
        EnsureColumn(connection, "Grows", "TentId", "INTEGER NULL");
        EnsureColumn(connection, "Grows", "SetupId", "INTEGER NULL");
        command.CommandText = GrowIndexSql;
        command.ExecuteNonQuery();
        EnsureColumn(connection, "Grows", "MediumDetail", "TEXT NULL");
        EnsureColumn(connection, "Grows", "ReservoirSize", "TEXT NULL");
        EnsureColumn(connection, "Measurements", "Source", "TEXT NOT NULL DEFAULT 'Manual'");
        EnsureColumn(connection, "Measurements", "PpfdMol", "REAL NULL");
        EnsureColumn(connection, "Measurements", "Co2Ppm", "REAL NULL");
        EnsureColumn(connection, "Photos", "Tag", "TEXT NOT NULL DEFAULT 'Overview'");
        EnsureColumn(connection, "Photos", "Source", "TEXT NOT NULL DEFAULT 'Manual'");
        EnsureColumn(connection, "Photos", "IsReferenceShot", "INTEGER NOT NULL DEFAULT 0");
        // Welches Symptom auf dem Bild zu sehen ist — der Schluessel aus der
        // Wissensbasis (z. B. „brown-roots-slimy"). Damit wird aus einer
        // Sammlung eigener Aufnahmen ein Nachschlagewerk: beim naechsten Mal
        // sieht man, wie es beim letzten Mal aussah.
        // Bewusst der EIGENE Bestand und keine fremden Beispielbilder — die
        // waeren urheberrechtlich nicht zu haben, und ein Bild aus dem eigenen
        // Zelt sagt ohnehin mehr als eines aus einem fremden.
        EnsureColumn(connection, "Photos", "SymptomId", "TEXT NULL");
        // Der Index MUSS hier stehen, nicht im Kern-Schema: dort laeuft er,
        // bevor EnsureColumn die Spalte in eine BESTEHENDE Datenbank
        // eingefuegt hat — „no such column: SymptomId", und die Installation
        // startet nicht mehr. Bei einer frischen Datenbank faellt das nie auf,
        // weil die Spalte dann schon im CREATE TABLE steht. Genau dafuer gibt
        // es GrowIndexSql ein paar Zeilen weiter oben.
        command.CommandText = "CREATE INDEX IF NOT EXISTS IX_Photos_SymptomId ON Photos(SymptomId) WHERE SymptomId IS NOT NULL;";
        command.ExecuteNonQuery();
        EnsureColumn(connection, "Grows", "IrrigationType", "TEXT NOT NULL DEFAULT 'Manual'");
        EnsureColumn(connection, "Grows", "WaterSource", "TEXT NOT NULL DEFAULT 'Tap'");
        // Welches Wasser der EINZELNE Vorgang benutzt hat. Der Grow traegt eine
        // Quelle fuer den ganzen Lauf; wer einmal mit Leitungswasser nachfuellt,
        // weil der Osmose-Tank leer war, kann den EC-Sprung sonst nie erklaeren.
        EnsureColumn(connection, "AddbackLogs", "WaterUsed", "TEXT NULL");
        EnsureColumn(connection, "AddbackLogs", "WaterEcMsCm", "REAL NULL");
        EnsureColumn(connection, "ChangeoutEntries", "WaterUsed", "TEXT NULL");
        EnsureColumn(connection, "ChangeoutEntries", "WaterEcMsCm", "REAL NULL");
        EnsureColumn(connection, "Grows", "FeedProgramId", "TEXT NULL");
        EnsureColumn(connection, "Grows", "UseFeedChartTargets", "INTEGER NOT NULL DEFAULT 0");
        // Alarmgrenzen duerfen dem Wochenplan folgen (10.09.2026). Der Vorgabewert
        // 'Fest' ist der Zustand vor der Aenderung: bestehende Regeln melden
        // weiter gegen ihre eingetragenen Zahlen, bis jemand umschaltet.
        EnsureColumn(connection, "TentAlertRules", "Quelle", "TEXT NOT NULL DEFAULT 'Fest'");
        EnsureColumn(connection, "TentAlertRules", "Toleranz", "REAL NULL");
        EnsureColumn(connection, "Grows", "NightRampEnabled", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Grows", "NightRampFloorC", "REAL NULL");

        EnsureColumn(connection, "Strains", "SeedKind", "TEXT NULL");
        EnsureColumn(connection, "Strains", "ThcPercent", "REAL NULL");
        EnsureColumn(connection, "Strains", "CbdPercent", "REAL NULL");
        EnsureColumn(connection, "Strains", "SativaPercent", "INTEGER NULL");
        EnsureColumn(connection, "Strains", "Taste", "TEXT NULL");
        EnsureColumn(connection, "Strains", "Effect", "TEXT NULL");
        EnsureColumn(connection, "Strains", "Aroma", "TEXT NULL");
        EnsureColumn(connection, "Strains", "YieldIndoorGm2", "INTEGER NULL");
        EnsureColumn(connection, "Strains", "HeightIndoorCm", "INTEGER NULL");
        EnsureColumn(connection, "Grows", "SeedType",                       "TEXT NOT NULL DEFAULT 'Feminized'");
        EnsureColumn(connection, "Grows", "StartMaterial",                  "TEXT NOT NULL DEFAULT 'Seed'");
        EnsureColumn(connection, "Grows", "GerminationMethod",              "TEXT NULL");
        EnsureColumn(connection, "Grows", "CloneSource",                    "TEXT NULL");
        EnsureColumn(connection, "Grows", "CloneIsRooted",                  "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Grows", "BreederFlowerWeeksMin",          "INTEGER NULL");
        EnsureColumn(connection, "Grows", "BreederFlowerWeeksMax",          "INTEGER NULL");
        EnsureColumn(connection, "Grows", "PlannedVegDays",                 "INTEGER NULL");
        EnsureColumn(connection, "Grows", "StrainId",                       "INTEGER NULL");
        EnsureColumn(connection, "Grows", "PlantCount",                     "INTEGER NULL");
        EnsureColumn(connection, "Grows", "PhenoNumber",                    "INTEGER NULL");
        EnsureColumn(connection, "Grows", "PropagationMedium",              "TEXT NULL");
        EnsureColumn(connection, "Grows", "HasChiller",                     "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Grows", "EntryPoint",                     "TEXT NOT NULL DEFAULT 'Germination'");
        EnsureColumn(connection, "Grows", "DaysAlreadyInPhase",             "INTEGER NULL");
        EnsureColumn(connection, "Grows", "AutoflowerDaysSinceGermination", "INTEGER NULL");
        EnsureColumn(connection, "Grows", "FlipDate",                       "TEXT NULL");
        // Sprint 10
        EnsureColumn(connection, "Grows", "GerminatedAt", "TEXT NULL");
        EnsureColumn(connection, "Grows", "RootedAt",     "TEXT NULL");
        EnsureColumn(connection, "Grows", "TentSnapshotJson", "TEXT NULL");
        EnsureColumn(connection, "Grows", "HydroSetupSnapshotJson", "TEXT NULL");
        EnsureColumn(connection, "Grows", "SnapshotsCapturedAtUtc", "TEXT NULL");
        // Group D — GrowSystems table first, then Grows FK column
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS GrowSystems (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                TentId          INTEGER NULL,
                Name            TEXT    NOT NULL,
                HydroStyle      TEXT    NOT NULL,
                PotCount        INTEGER NULL,
                PotSizeLiters   REAL    NULL,
                ReservoirLiters REAL    NULL,
                Status          TEXT    NOT NULL DEFAULT 'Active',
                LayoutType      TEXT    NOT NULL DEFAULT 'SingleBucket',
                ReservoirPosition TEXT  NOT NULL DEFAULT 'None',
                HasCirculationPump INTEGER NOT NULL DEFAULT 0,
                CirculationPumpNotes TEXT NULL,
                HasAirPump      INTEGER NOT NULL DEFAULT 0,
                AirPumpNotes    TEXT NULL,
                AirStoneCount   INTEGER NULL,
                HasChiller      INTEGER NOT NULL DEFAULT 0,
                HasUvSterilizer INTEGER NOT NULL DEFAULT 0,
                Notes           TEXT    NULL,
                DisplayOrder    INTEGER NOT NULL DEFAULT 99,
                CreatedAtUtc    TEXT    NOT NULL,
                UpdatedAtUtc    TEXT    NULL
            );
        """;
        command.ExecuteNonQuery();
        EnsureColumn(connection, "HardwareItems", "HydroSetupId", "INTEGER NULL");
        EnsureColumn(connection, "HardwareItems", "CalibrationIntervalDays", "INTEGER NULL");
        EnsureColumn(connection, "HardwareItems", "SensorMetricType", "TEXT NULL");
        EnsureColumn(connection, "HardwareItems", "DeviceKind", "TEXT NULL");
        EnsureColumn(connection, "AutoMeasurementConfigs", "CaptureSnapshot", "INTEGER NOT NULL DEFAULT 0");
        command.CommandText = """
            CREATE INDEX IF NOT EXISTS IX_HardwareItems_HydroSetupId ON HardwareItems(HydroSetupId);
        """;
        command.ExecuteNonQuery();
        EnsureColumn(connection, "Tents", "Status", "TEXT NOT NULL DEFAULT 'Active'");
        EnsureColumn(connection, "Tents", "CameraEntityIds", "TEXT NULL");
        EnsureColumn(connection, "Tents", "WaterTargetEntityId", "TEXT NULL");

        // Kuehler ueber eine smarte Steckdose. Die Standardwerte stehen hier
        // UND in Tent.cs — das ist die einzige Doppelung, die SQLite verlangt:
        // eine bestehende Datenbank bekommt sie beim Nachziehen, ein neues
        // Objekt im Speicher aus dem Modell.
        EnsureColumn(connection, "Tents", "ChillerSwitchEntityId", "TEXT NULL");
        EnsureColumn(connection, "Tents", "ChillerControlEnabled", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Tents", "ChillerHysteresisC", "REAL NOT NULL DEFAULT 0.4");
        EnsureColumn(connection, "Tents", "ChillerMinRunMinutes", "INTEGER NOT NULL DEFAULT 5");
        EnsureColumn(connection, "Tents", "ChillerMinPauseMinutes", "INTEGER NOT NULL DEFAULT 5");
        EnsureColumn(connection, "Tents", "ChillerMaxReadingAgeMinutes", "INTEGER NOT NULL DEFAULT 10");
        EnsureColumn(connection, "Tents", "LeafTempOffsetC", "REAL NOT NULL DEFAULT 0");
        EnsureColumn(connection, "GrowSystems", "TentId", "INTEGER NULL");
        EnsureColumn(connection, "GrowSystems", "Status", "TEXT NOT NULL DEFAULT 'Active'");
        EnsureColumn(connection, "GrowSystems", "LayoutType", "TEXT NOT NULL DEFAULT 'SingleBucket'");
        EnsureColumn(connection, "GrowSystems", "ReservoirPosition", "TEXT NOT NULL DEFAULT 'None'");
        EnsureColumn(connection, "GrowSystems", "HasCirculationPump", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "GrowSystems", "CirculationPumpNotes", "TEXT NULL");
        EnsureColumn(connection, "GrowSystems", "HasAirPump", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "GrowSystems", "AirPumpNotes", "TEXT NULL");
        EnsureColumn(connection, "GrowSystems", "AirStoneCount", "INTEGER NULL");
        EnsureColumn(connection, "GrowSystems", "AirPumpLitersPerHour", "REAL NULL");
        EnsureColumn(connection, "GrowSystems", "HasChiller", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "GrowSystems", "HasUvSterilizer", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "GrowSystems", "UpdatedAtUtc", "TEXT NULL");
        command.CommandText = """
            CREATE INDEX IF NOT EXISTS IX_GrowSystems_TentId ON GrowSystems(TentId);
            CREATE INDEX IF NOT EXISTS IX_GrowSystems_Status ON GrowSystems(Status);
            CREATE INDEX IF NOT EXISTS IX_GrowSystems_HydroStyle ON GrowSystems(HydroStyle);
        """;
        command.ExecuteNonQuery();
        EnsureColumn(connection, "Grows", "SystemId", "INTEGER NULL");

        // Ernte pro Pflanze. Am Trockenregal wiegt man Pflanze fuer Pflanze, nicht
        // den Grow am Stueck — die Summe steht weiterhin in WetWeightG/DryWeightG,
        // die Aufschluesselung hier. Als JSON statt eigener Tabelle, weil sie nur
        // gemeinsam mit ihrem Ernteeintrag gelesen und geschrieben wird.
        EnsureColumn(connection, "HarvestEntries", "PlantWeightsJson", "TEXT NULL");
        // Sprint E4 — SOP Scheduling
        EnsureColumn(connection, "SopInstances",     "DueAtUtc",               "TEXT NULL");
        EnsureColumn(connection, "SopInstances",     "NextStepDueAtUtc",       "TEXT NULL");
        EnsureColumn(connection, "SopInstances",     "RecurrenceIntervalDays", "INTEGER NULL");
        EnsureColumn(connection, "SopInstances",     "IsRecurring",            "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "SopStepInstances", "DueAtUtc",               "TEXT NULL");
        EnsureColumn(connection, "SopStepInstances", "AvailableAtUtc",         "TEXT NULL");
        EnsureColumn(connection, "SopStepInstances", "ReminderTaskId",         "INTEGER NULL");

        // Sollwert-Profil je Grow und je Hydro-System. Null heisst „geerbt":
        // der Grow folgt seinem System, das System dem Anbaustil.
        // Muss NACH dem Anlegen von GrowSystems stehen — sonst scheitert ALTER
        // TABLE an einer Tabelle, die es noch nicht gibt.
        EnsureColumn(connection, "Grows", "SetpointProfileId", "TEXT NULL");
        EnsureColumn(connection, "GrowSystems", "SetpointProfileId", "TEXT NULL");

        EnsureFeatureColumns(connection);

        RecordSchemaVersion(connection);
    }


    private static void RecordSchemaVersion(SqliteConnection connection)
    {
        UpsertAppSetting(connection, CurrentSchemaAppSettingKey, CurrentSchemaVersion);
        UpsertAppSetting(connection, LastMigrationUtcAppSettingKey, DateTime.UtcNow.ToString("O"));
        RecordAppliedSchemaMigrations(connection);
    }


    private static void RecordAppliedSchemaMigrations(SqliteConnection connection)
    {
        if (!TableExists(connection, "AppliedSchemaMigrations"))
        {
            return;
        }

        EnsureSchemaMigrationMetadataColumns(connection);
        var now = DateTime.UtcNow.ToString("O");

        foreach (var migration in RequiredMigrations)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO AppliedSchemaMigrations (
                    Id, Name, RequiredForSchemaVersion, AppliedAtUtc,
                    Status, StartedAtUtc, CompletedAtUtc, Error,
                    RequiresBackup, IsDestructive, Checksum, EngineVersion)
                VALUES (
                    $id, $name, $requiredForSchemaVersion, $appliedAtUtc,
                    'Applied', $startedAtUtc, $completedAtUtc, NULL,
                    $requiresBackup, $isDestructive, $checksum, 'migration-engine.v1')
                ON CONFLICT(Id) DO UPDATE SET
                    Name = excluded.Name,
                    RequiredForSchemaVersion = excluded.RequiredForSchemaVersion,
                    Status = 'Applied',
                    CompletedAtUtc = COALESCE(AppliedSchemaMigrations.CompletedAtUtc, excluded.CompletedAtUtc),
                    RequiresBackup = excluded.RequiresBackup,
                    IsDestructive = excluded.IsDestructive,
                    Checksum = excluded.Checksum,
                    EngineVersion = excluded.EngineVersion;
            """;
            command.Parameters.AddWithValue("$id", migration.Id);
            command.Parameters.AddWithValue("$name", migration.Name);
            command.Parameters.AddWithValue("$requiredForSchemaVersion", migration.RequiredForSchemaVersion);
            command.Parameters.AddWithValue("$appliedAtUtc", now);
            command.Parameters.AddWithValue("$startedAtUtc", now);
            command.Parameters.AddWithValue("$completedAtUtc", now);
            command.Parameters.AddWithValue("$requiresBackup", migration.RequiresBackup ? 1 : 0);
            command.Parameters.AddWithValue("$isDestructive", migration.IsDestructive ? 1 : 0);
            command.Parameters.AddWithValue("$checksum", migration.Checksum ?? string.Empty);
            command.ExecuteNonQuery();
        }
    }


    private static void EnsureSchemaMigrationMetadataColumns(SqliteConnection connection)
    {
        if (!TableExists(connection, "AppliedSchemaMigrations"))
        {
            return;
        }

        EnsureColumn(connection, "AppliedSchemaMigrations", "Status", "TEXT NOT NULL DEFAULT 'Applied'");
        EnsureColumn(connection, "AppliedSchemaMigrations", "StartedAtUtc", "TEXT NULL");
        EnsureColumn(connection, "AppliedSchemaMigrations", "CompletedAtUtc", "TEXT NULL");
        EnsureColumn(connection, "AppliedSchemaMigrations", "Error", "TEXT NULL");
        EnsureColumn(connection, "AppliedSchemaMigrations", "RequiresBackup", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "AppliedSchemaMigrations", "IsDestructive", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "AppliedSchemaMigrations", "Checksum", "TEXT NULL");
        EnsureColumn(connection, "AppliedSchemaMigrations", "EngineVersion", "TEXT NOT NULL DEFAULT 'migration-engine.v1'");
    }


    /// <summary>
    /// Spalten, die nach dem ersten Ausliefern dazukamen.
    /// </summary>
    /// <remarks>
    /// Ein CREATE TABLE erreicht nur frische Datenbanken; bestehende
    /// Installationen brauchen den Nachtrag hier. Diese Methode laeuft am ENDE
    /// von <c>EnsureSchema</c>, wenn jede Tabelle existiert — ALTER TABLE auf
    /// eine Tabelle, die es noch nicht gibt, bricht den ganzen Start ab.
    /// </remarks>
    private static void EnsureFeatureColumns(SqliteConnection connection)
    {
        // Testbetrieb der Dosierpumpen. Die Tabellen entstanden eine Version
        // frueher — im CREATE TABLE nachzutragen erreicht nur frische
        // Datenbanken, bestehende brauchen den Zusatz hier.
        EnsureColumn(connection, "DosingPumps", "SimulationMode", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "DoseEvents", "Simulated", "INTEGER NOT NULL DEFAULT 0");

        // Zweikomponenten-Duenger: A und B als Paar mit Verhaeltnis und Trennzeit.
        EnsureColumn(connection, "DosingPumps", "PartnerPumpId", "INTEGER NULL");
        EnsureColumn(connection, "DosingPumps", "PartnerRatio", "REAL NOT NULL DEFAULT 1");
        EnsureColumn(connection, "DosingPumps", "PartnerDelayMinutes", "INTEGER NOT NULL DEFAULT 5");
        EnsureColumn(connection, "DosingPumps", "CostPerLiterEur", "REAL NULL");

        // Stroemung: Luftstrom am Blatt als Zahl, Wasserfluss als Stufe.
        EnsureColumn(connection, "Measurements", "AirflowAtLeafMPerMin", "REAL NULL");
        EnsureColumn(connection, "Measurements", "WaterFlow", "TEXT NULL");

        // Der beobachtete Uebergang Saemling -> Veg. Vorher wurde er nur
        // gerechnet, und die Anzeige widersprach den Zielwerten.
        EnsureColumn(connection, "Grows", "VegStartedAt", "TEXT NULL");
        EnsureColumn(connection, "Grows", "FinishStartedAt", "TEXT NULL");

        // Anreicherung ist nicht Sensor: nur mit Brenner/Flasche gibt es ein
        // CO2-Ziel. Default 0 — wer anreichert, schaltet es bewusst ein.
        EnsureColumn(connection, "Tents", "HasCo2Enrichment", "INTEGER NOT NULL DEFAULT 0");

        // Pegelsensor in Liter umrechnen: zwei gemessene Punkte plus die
        // Litermenge, die beim Fuellen wirklich hineinging.
        EnsureColumn(connection, "GrowSystems", "LevelSensorEmptyRaw", "REAL NULL");
        EnsureColumn(connection, "GrowSystems", "LevelSensorFullRaw", "REAL NULL");
        EnsureColumn(connection, "GrowSystems", "LevelSensorFullLiters", "REAL NULL");
        EnsureColumn(connection, "GrowSystems", "LevelCalibratedAtUtc", "TEXT NULL");

        SchliesseVerwaisteWarnungen(connection);
    }

    /// <summary>
    /// Warnungen, deren Grow es nicht mehr gibt, auf erledigt setzen.
    /// </summary>
    /// <remarks>
    /// <para><c>RiskEvents</c> hat keinen Fremdschlüssel auf <c>Grows</c>, und
    /// <c>PRAGMA foreign_keys</c> ist per Vorgabe aus. Wer vor dieser Fassung
    /// einen Grow gelöscht hat, behielt dessen Warnungen: für immer offen auf
    /// der Aufgabenseite, von keinem Abgleich je wieder angefasst — der läuft
    /// nur über die aktiven Grows. Gefunden am 18.08.2026 in der
    /// Testdatenbank.</para>
    ///
    /// <para>Diese Nachpflege löscht bewusst <b>nichts</b>: sie schließt nur,
    /// was ohnehin niemandem mehr zuzuordnen ist. Die Einträge verschwinden
    /// damit von der Aufgabenseite — das war das eigentliche Problem — und
    /// bleiben im Verlauf nachlesbar.</para>
    ///
    /// <para>Beim <b>Löschen eines Grows</b> gilt das Gegenteil, und das ist
    /// Absicht: dort geht sein Verlauf mit, so wie bei den Prüfspuren auch. Wer
    /// einen Grow entfernt, will ihn samt allem entfernen. Neue verwaiste
    /// Einträge kann es dadurch nicht mehr geben; diese Methode räumt nur auf,
    /// was vor 2.0.0-beta.48 entstanden ist.</para>
    /// </remarks>
    private static void SchliesseVerwaisteWarnungen(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE RiskEvents " +
            "SET Status = 'Resolved', " +
            "    ResolvedAtUtc = COALESCE(ResolvedAtUtc, $jetzt), " +
            "    UpdatedAtUtc = $jetzt, " +
            "    Notes = TRIM(COALESCE(Notes || char(10), '') || 'Der zugehoerige Grow wurde geloescht.') " +
            "WHERE GrowId > 0 " +
            "  AND Status IN ('Open', 'Acknowledged') " +
            "  AND GrowId NOT IN (SELECT Id FROM Grows);";
        command.Parameters.AddWithValue("$jetzt", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }


    private static void UpsertAppSetting(SqliteConnection connection, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AppSettings (Key, Value)
            VALUES ($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
        """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }


    private static void EnsureColumn(SqliteConnection connection, string table, string column, string definition)
    {
        using var pragma = connection.CreateCommand();
        pragma.CommandText = $"PRAGMA table_info({table});";
        using var reader = pragma.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader["name"]?.ToString(), column, StringComparison.OrdinalIgnoreCase))
            {
                reader.Close();
                return;
            }
        }
        reader.Close();

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        alter.ExecuteNonQuery();
    }


    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name;";
        cmd.Parameters.AddWithValue("@name", tableName);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }


    private SqliteConnection OpenConnection()
    {
        var builder = new SqliteConnectionStringBuilder { DataSource = _paths.DatabasePath };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }
}
