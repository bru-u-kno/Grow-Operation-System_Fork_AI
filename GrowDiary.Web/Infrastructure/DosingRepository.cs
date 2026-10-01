using GrowDiary.Web.Models;
using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Infrastructure;

/// <summary>Pumpen und ihr Dosier-Protokoll.</summary>
public sealed class DosingRepository : RepositoryBase
{
    public DosingRepository(AppPaths paths) : base(paths)
    {
    }

    // ---------- Pumpen ----------

    public List<DosingPump> GetPumps(int? tentId = null)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = tentId is null
            ? "SELECT * FROM DosingPumps ORDER BY TentId, Name;"
            : "SELECT * FROM DosingPumps WHERE TentId = $tentId ORDER BY Name;";
        if (tentId is not null) command.Parameters.AddWithValue("$tentId", tentId.Value);

        var pumps = new List<DosingPump>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) pumps.Add(MapPump(reader));
        return pumps;
    }

    public DosingPump? GetPump(int id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM DosingPumps WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapPump(reader) : null;
    }

    public int InsertPump(DosingPump pump)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DosingPumps
                (TentId, Name, Purpose, Agent, ConcentrationPercent, HaEntityId, MlPerMinute,
                 CalibratedAtUtc, TubeChangedAtUtc, CalibrationIntervalDays, TubeIntervalDays,
                 MaxSingleDoseMl, MinIntervalMinutes, MaxDosesPerDay, MaxMlPerDay, MaxReadingAgeMinutes,
                 AutomationEnabled, HasHomeAssistantAutoOff, SimulationMode,
                 PartnerPumpId, PartnerRatio, PartnerDelayMinutes, CostPerLiterEur, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                ($tentId, $name, $purpose, $agent, $concentration, $entity, $mlPerMinute,
                 $calibratedAt, $tubeChangedAt, $calInterval, $tubeInterval,
                 $maxSingle, $minInterval, $maxDoses, $maxMl, $maxAge,
                 $automation, $autoOff, $simulation,
                 $partnerId, $partnerRatio, $partnerDelay, $costPerLiter, $now, $now);
            SELECT last_insert_rowid();
        """;
        BindPump(command, pump);
        command.Parameters.AddWithValue("$now", ToStorageUtc(DateTime.UtcNow));
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public void UpdatePump(DosingPump pump)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE DosingPumps
               SET TentId = $tentId, Name = $name, Purpose = $purpose, Agent = $agent,
                   ConcentrationPercent = $concentration, HaEntityId = $entity, MlPerMinute = $mlPerMinute,
                   CalibratedAtUtc = $calibratedAt, TubeChangedAtUtc = $tubeChangedAt,
                   CalibrationIntervalDays = $calInterval, TubeIntervalDays = $tubeInterval,
                   MaxSingleDoseMl = $maxSingle, MinIntervalMinutes = $minInterval,
                   MaxDosesPerDay = $maxDoses, MaxMlPerDay = $maxMl, MaxReadingAgeMinutes = $maxAge,
                   AutomationEnabled = $automation, HasHomeAssistantAutoOff = $autoOff,
                   SimulationMode = $simulation,
                   PartnerPumpId = $partnerId, PartnerRatio = $partnerRatio,
                   PartnerDelayMinutes = $partnerDelay,
                   CostPerLiterEur = $costPerLiter,
                   UpdatedAtUtc = $now
             WHERE Id = $id;
        """;
        BindPump(command, pump);
        command.Parameters.AddWithValue("$now", ToStorageUtc(DateTime.UtcNow));
        command.Parameters.AddWithValue("$id", pump.Id);
        command.ExecuteNonQuery();
    }

    public void DeletePump(int id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM DosingPumps WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>Nur die Fördermenge — der Kalibrier-Ablauf ändert sonst nichts.</summary>
    public void SaveCalibration(int pumpId, double mlPerMinute, DateTime whenUtc)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE DosingPumps
               SET MlPerMinute = $mlPerMinute, CalibratedAtUtc = $when, UpdatedAtUtc = $when
             WHERE Id = $id;
        """;
        command.Parameters.AddWithValue("$mlPerMinute", mlPerMinute);
        command.Parameters.AddWithValue("$when", ToStorageUtc(whenUtc));
        command.Parameters.AddWithValue("$id", pumpId);
        command.ExecuteNonQuery();
    }

    // ---------- Protokoll ----------

    public int InsertEvent(DoseEvent dose)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DoseEvents
                (PumpId, TentId, GrowId, OccurredAtUtc, Trigger, Outcome, RequestedMl, DosedMl,
                 SecondsRun, ValueBefore, ValueAfter, TargetValue, Reason, Simulated)
            VALUES
                ($pumpId, $tentId, $growId, $occurredAt, $trigger, $outcome, $requestedMl, $dosedMl,
                 $seconds, $before, $after, $target, $reason, $simulated);
            SELECT last_insert_rowid();
        """;
        command.Parameters.AddWithValue("$pumpId", dose.PumpId);
        command.Parameters.AddWithValue("$tentId", dose.TentId);
        AddNullable(command, "$growId", (double?)dose.GrowId);
        command.Parameters.AddWithValue("$occurredAt", ToStorageUtc(dose.OccurredAtUtc));
        command.Parameters.AddWithValue("$trigger", dose.Trigger.ToString());
        command.Parameters.AddWithValue("$outcome", dose.Outcome.ToString());
        command.Parameters.AddWithValue("$requestedMl", dose.RequestedMl);
        command.Parameters.AddWithValue("$dosedMl", dose.DosedMl);
        command.Parameters.AddWithValue("$seconds", dose.SecondsRun);
        AddNullable(command, "$before", dose.ValueBefore);
        AddNullable(command, "$after", dose.ValueAfter);
        AddNullable(command, "$target", dose.TargetValue);
        command.Parameters.AddWithValue("$reason", (object?)dose.Reason ?? DBNull.Value);
        command.Parameters.AddWithValue("$simulated", dose.Simulated ? 1 : 0);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public List<DoseEvent> GetEvents(int? pumpId = null, int? tentId = null, int limit = 100)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var where = pumpId is not null ? "WHERE PumpId = $pumpId"
            : tentId is not null ? "WHERE TentId = $tentId"
            : string.Empty;
        command.CommandText = $"SELECT * FROM DoseEvents {where} ORDER BY OccurredAtUtc DESC, Id DESC LIMIT $limit;";
        if (pumpId is not null) command.Parameters.AddWithValue("$pumpId", pumpId.Value);
        if (tentId is not null) command.Parameters.AddWithValue("$tentId", tentId.Value);
        command.Parameters.AddWithValue("$limit", limit);

        var events = new List<DoseEvent>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) events.Add(MapEvent(reader));
        return events;
    }

    /// <summary>
    /// Die gelaufenen Dosen einer Pumpe seit einem Zeitpunkt — und die, die
    /// gelaufen sein KÖNNEN (<c>Failed</c>, siehe
    /// <c>DosingService.KannGelaufenSein</c>). Was davon auf Tagesgrenze und
    /// Sperrfrist zählt, entscheidet <c>DosingGuard</c> — hier wird nur geholt.
    /// </summary>
    public List<DoseEvent> GetDosesSince(int pumpId, DateTime sinceUtc)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT * FROM DoseEvents
             WHERE PumpId = $pumpId AND Outcome IN ('Done', 'Failed') AND OccurredAtUtc >= $since
             ORDER BY OccurredAtUtc DESC;
        """;
        command.Parameters.AddWithValue("$pumpId", pumpId);
        command.Parameters.AddWithValue("$since", ToStorageUtc(sinceUtc));

        var events = new List<DoseEvent>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) events.Add(MapEvent(reader));
        return events;
    }

    /// <summary>Trägt den Messwert nach, sobald nach der Mischzeit wieder gemessen wurde.</summary>
    public void SetValueAfter(int doseEventId, double value)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE DoseEvents SET ValueAfter = $value WHERE Id = $id;";
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$id", doseEventId);
        command.ExecuteNonQuery();
    }

    // ---------- Ausstehende zweite Haelfte ----------

    public int InsertPending(PendingDose pending)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO PendingDoses (PumpId, Ml, DueAtUtc, SourceDoseEventId, Reason, CreatedAtUtc, Fehlversuche)
            VALUES ($pumpId, $ml, $due, $source, $reason, $created, $fehlversuche);
            SELECT last_insert_rowid();
        """;
        command.Parameters.AddWithValue("$pumpId", pending.PumpId);
        command.Parameters.AddWithValue("$ml", pending.Ml);
        command.Parameters.AddWithValue("$due", ToStorageUtc(pending.DueAtUtc));
        AddNullable(command, "$source", (double?)pending.SourceDoseEventId);
        command.Parameters.AddWithValue("$reason", (object?)pending.Reason ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", ToStorageUtc(pending.CreatedAtUtc));
        command.Parameters.AddWithValue("$fehlversuche", pending.Fehlversuche);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>Alles, was jetzt faellig ist — aelteste zuerst.</summary>
    public List<PendingDose> GetDuePending(DateTime nowUtc)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, PumpId, Ml, DueAtUtc, SourceDoseEventId, Reason, CreatedAtUtc, Fehlversuche
            FROM PendingDoses
            WHERE DueAtUtc <= $now
            ORDER BY DueAtUtc ASC;
        """;
        command.Parameters.AddWithValue("$now", ToStorageUtc(nowUtc));

        var result = new List<PendingDose>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(MapPending(reader));
        return result;
    }

    public List<PendingDose> GetPendingForPump(int pumpId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, PumpId, Ml, DueAtUtc, SourceDoseEventId, Reason, CreatedAtUtc, Fehlversuche
            FROM PendingDoses WHERE PumpId = $pumpId ORDER BY DueAtUtc ASC;
        """;
        command.Parameters.AddWithValue("$pumpId", pumpId);

        var result = new List<PendingDose>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(MapPending(reader));
        return result;
    }

    public void DeletePending(int id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM PendingDoses WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static PendingDose MapPending(SqliteDataReader reader) => new()
    {
        Id = Convert.ToInt32(reader["Id"]),
        PumpId = Convert.ToInt32(reader["PumpId"]),
        Ml = Convert.ToDouble(reader["Ml"]),
        DueAtUtc = ParseStoredUtcDateTime(NullString(reader["DueAtUtc"])) ?? DateTime.UtcNow,
        SourceDoseEventId = (int?)NullableDouble(reader["SourceDoseEventId"]),
        Reason = NullString(reader["Reason"]),
        CreatedAtUtc = ParseStoredUtcDateTime(NullString(reader["CreatedAtUtc"])) ?? DateTime.UtcNow,
        Fehlversuche = Convert.ToInt32(reader["Fehlversuche"]),
    };

    // ---------- Abbildung ----------

    private static void BindPump(SqliteCommand command, DosingPump pump)
    {
        command.Parameters.AddWithValue("$tentId", pump.TentId);
        command.Parameters.AddWithValue("$name", pump.Name);
        command.Parameters.AddWithValue("$purpose", pump.Purpose.ToString());
        command.Parameters.AddWithValue("$agent", (object?)pump.Agent ?? DBNull.Value);
        AddNullable(command, "$concentration", pump.ConcentrationPercent);
        command.Parameters.AddWithValue("$entity", pump.HaEntityId);
        AddNullable(command, "$mlPerMinute", pump.MlPerMinute);
        AddNullable(command, "$costPerLiter", pump.CostPerLiterEur);
        command.Parameters.AddWithValue("$calibratedAt", pump.CalibratedAtUtc.HasValue ? ToStorageUtc(pump.CalibratedAtUtc.Value) : (object)DBNull.Value);
        command.Parameters.AddWithValue("$tubeChangedAt", pump.TubeChangedAtUtc.HasValue ? ToStorageUtc(pump.TubeChangedAtUtc.Value) : (object)DBNull.Value);
        AddNullable(command, "$calInterval", (double?)pump.CalibrationIntervalDays);
        AddNullable(command, "$tubeInterval", (double?)pump.TubeIntervalDays);
        command.Parameters.AddWithValue("$maxSingle", pump.MaxSingleDoseMl);
        command.Parameters.AddWithValue("$minInterval", pump.MinIntervalMinutes);
        command.Parameters.AddWithValue("$maxDoses", pump.MaxDosesPerDay);
        command.Parameters.AddWithValue("$maxMl", pump.MaxMlPerDay);
        command.Parameters.AddWithValue("$maxAge", pump.MaxReadingAgeMinutes);
        command.Parameters.AddWithValue("$automation", pump.AutomationEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$autoOff", pump.HasHomeAssistantAutoOff ? 1 : 0);
        command.Parameters.AddWithValue("$simulation", pump.SimulationMode ? 1 : 0);
        AddNullable(command, "$partnerId", (double?)pump.PartnerPumpId);
        command.Parameters.AddWithValue("$partnerRatio", pump.PartnerRatio);
        command.Parameters.AddWithValue("$partnerDelay", pump.PartnerDelayMinutes);
    }

    private static DosingPump MapPump(SqliteDataReader reader) => new()
    {
        Id = Convert.ToInt32(reader["Id"]),
        TentId = Convert.ToInt32(reader["TentId"]),
        Name = reader["Name"].ToString() ?? string.Empty,
        Purpose = Enum.TryParse<DosingPurpose>(reader["Purpose"]?.ToString(), out var purpose) ? purpose : DosingPurpose.Custom,
        Agent = NullString(reader["Agent"]),
        ConcentrationPercent = NullableDouble(reader["ConcentrationPercent"]),
        HaEntityId = reader["HaEntityId"].ToString() ?? string.Empty,
        MlPerMinute = NullableDouble(reader["MlPerMinute"]),
        CostPerLiterEur = NullableDouble(reader["CostPerLiterEur"]),
        CalibratedAtUtc = ParseStoredUtcDateTime(NullString(reader["CalibratedAtUtc"])),
        TubeChangedAtUtc = ParseStoredUtcDateTime(NullString(reader["TubeChangedAtUtc"])),
        CalibrationIntervalDays = (int?)NullableDouble(reader["CalibrationIntervalDays"]),
        TubeIntervalDays = (int?)NullableDouble(reader["TubeIntervalDays"]),
        MaxSingleDoseMl = Convert.ToDouble(reader["MaxSingleDoseMl"]),
        MinIntervalMinutes = Convert.ToInt32(reader["MinIntervalMinutes"]),
        MaxDosesPerDay = Convert.ToInt32(reader["MaxDosesPerDay"]),
        MaxMlPerDay = Convert.ToDouble(reader["MaxMlPerDay"]),
        MaxReadingAgeMinutes = Convert.ToInt32(reader["MaxReadingAgeMinutes"]),
        AutomationEnabled = Convert.ToInt32(reader["AutomationEnabled"]) == 1,
        HasHomeAssistantAutoOff = Convert.ToInt32(reader["HasHomeAssistantAutoOff"]) == 1,
        SimulationMode = HasColumn(reader, "SimulationMode") && Convert.ToInt32(reader["SimulationMode"]) == 1,
        PartnerPumpId = HasColumn(reader, "PartnerPumpId") ? (int?)NullableDouble(reader["PartnerPumpId"]) : null,
        PartnerRatio = HasColumn(reader, "PartnerRatio") ? Convert.ToDouble(reader["PartnerRatio"]) : 1,
        PartnerDelayMinutes = HasColumn(reader, "PartnerDelayMinutes") ? Convert.ToInt32(reader["PartnerDelayMinutes"]) : 5,
        CreatedAtUtc = ParseStoredUtcDateTime(NullString(reader["CreatedAtUtc"])) ?? DateTime.UtcNow,
        UpdatedAtUtc = ParseStoredUtcDateTime(NullString(reader["UpdatedAtUtc"])) ?? DateTime.UtcNow,
    };

    private static DoseEvent MapEvent(SqliteDataReader reader) => new()
    {
        Id = Convert.ToInt32(reader["Id"]),
        PumpId = Convert.ToInt32(reader["PumpId"]),
        TentId = Convert.ToInt32(reader["TentId"]),
        GrowId = (int?)NullableDouble(reader["GrowId"]),
        OccurredAtUtc = ParseStoredUtcDateTime(NullString(reader["OccurredAtUtc"])) ?? DateTime.UtcNow,
        Trigger = Enum.TryParse<DoseTrigger>(reader["Trigger"]?.ToString(), out var trigger) ? trigger : DoseTrigger.Manual,
        Outcome = Enum.TryParse<DoseOutcome>(reader["Outcome"]?.ToString(), out var outcome) ? outcome : DoseOutcome.Done,
        RequestedMl = Convert.ToDouble(reader["RequestedMl"]),
        DosedMl = Convert.ToDouble(reader["DosedMl"]),
        SecondsRun = Convert.ToDouble(reader["SecondsRun"]),
        ValueBefore = NullableDouble(reader["ValueBefore"]),
        ValueAfter = NullableDouble(reader["ValueAfter"]),
        TargetValue = NullableDouble(reader["TargetValue"]),
        Reason = NullString(reader["Reason"]),
        Simulated = HasColumn(reader, "Simulated") && Convert.ToInt32(reader["Simulated"]) == 1,
    };
}
