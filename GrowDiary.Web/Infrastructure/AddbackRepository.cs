using System.Globalization;
using GrowDiary.Web.Models;
using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Infrastructure;

public sealed class AddbackRepository : RepositoryBase
{
    public AddbackRepository(AppPaths paths) : base(paths)
    {
    }

    public AddbackLogEntry CreateAddbackLog(AddbackLogEntry entry)
    {
        if (!GrowExists(entry.GrowId))
        {
            throw new InvalidOperationException($"Grow with id {entry.GrowId} does not exist.");
        }

        if (entry.HydroSetupId.HasValue && !HydroSetupExists(entry.HydroSetupId.Value))
        {
            throw new InvalidOperationException($"HydroSetup with id {entry.HydroSetupId.Value} does not exist.");
        }

        ValidateAddbackLog(entry);
        entry.PerformedAtUtc = entry.PerformedAtUtc == default ? DateTime.UtcNow : entry.PerformedAtUtc;
        entry.CreatedAtUtc = DateTime.UtcNow;
        entry.Notes = NormalizeOptional(entry.Notes);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AddbackLogs (
                GrowId, HydroSetupId, Kind, PerformedAtUtc, ReservoirLiters,
                EcBefore, EcTarget, EcStock, EcAfter, PhBefore, PhAfter,
                LitersAdded, NewReservoirVolumeLiters, UsedHydroSetupVolume,
                WaterUsed, WaterEcMsCm,
                Notes, CreatedAtUtc
            )
            VALUES (
                $growId, $hydroSetupId, $kind, $performedAtUtc, $reservoirLiters,
                $ecBefore, $ecTarget, $ecStock, $ecAfter, $phBefore, $phAfter,
                $litersAdded, $newReservoirVolumeLiters, $usedHydroSetupVolume,
                $waterUsed, $waterEcMsCm,
                $notes, $createdAtUtc
            );
            SELECT last_insert_rowid();
        """;
        AddAddbackLogParameters(command, entry);
        entry.Id = Convert.ToInt32((long)command.ExecuteScalar()!, CultureInfo.InvariantCulture);
        return entry;
    }

    public List<AddbackLogEntry> GetAddbackLogsForGrow(int growId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT *
            FROM AddbackLogs
            WHERE GrowId = $growId
            ORDER BY PerformedAtUtc DESC, Id DESC;
        """;
        command.Parameters.AddWithValue("$growId", growId);

        var items = new List<AddbackLogEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(MapAddbackLog(reader));
        }
        return items;
    }

    public ChangeoutEntry CreateChangeout(ChangeoutEntry entry)
    {
        if (!GrowExists(entry.GrowId))
        {
            throw new InvalidOperationException($"Grow with id {entry.GrowId} does not exist.");
        }

        if (entry.HydroSetupId.HasValue && !HydroSetupExists(entry.HydroSetupId.Value))
        {
            throw new InvalidOperationException($"HydroSetup with id {entry.HydroSetupId.Value} does not exist.");
        }

        ValidateChangeout(entry);
        entry.PerformedAtUtc = entry.PerformedAtUtc == default ? DateTime.UtcNow : entry.PerformedAtUtc;
        entry.CreatedAtUtc = DateTime.UtcNow;
        entry.Notes = NormalizeOptional(entry.Notes);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ChangeoutEntries (
                GrowId, HydroSetupId, Kind, PerformedAtUtc, VolumeChangedLiters,
                PercentChanged, EcBefore, EcAfter, PhBefore, PhAfter,
                WaterUsed, WaterEcMsCm,
                Notes, CreatedAtUtc
            )
            VALUES (
                $growId, $hydroSetupId, $kind, $performedAtUtc, $volumeChangedLiters,
                $percentChanged, $ecBefore, $ecAfter, $phBefore, $phAfter,
                $waterUsed, $waterEcMsCm,
                $notes, $createdAtUtc
            );
            SELECT last_insert_rowid();
        """;
        AddChangeoutParameters(command, entry);
        entry.Id = Convert.ToInt32((long)command.ExecuteScalar()!, CultureInfo.InvariantCulture);
        return entry;
    }

    public List<ChangeoutEntry> GetChangeoutsForGrow(int growId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT *
            FROM ChangeoutEntries
            WHERE GrowId = $growId
            ORDER BY PerformedAtUtc DESC, Id DESC;
        """;
        command.Parameters.AddWithValue("$growId", growId);

        var items = new List<ChangeoutEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(MapChangeout(reader));
        }
        return items;
    }

    /// <summary>
    /// Einen Wasserwechsel wieder entfernen.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Anlass (31.08.2026).</b> Es gab keinen Weg zurück: ein
    /// falsch eingetragener Wechsel blieb für immer stehen. Bis dahin war das
    /// harmlos — die Mahnung „Wöchentlicher Wasserwechsel" las diese Tabelle
    /// ohnehin nicht. Seit sie es tut, legt ein Fehlgriff die Mahnung für eine
    /// Woche still, und dann muss man ihn zurücknehmen können.</para>
    ///
    /// <para>Aufgefallen ist es an einer <b>Aufräumzeile in einem Test</b>, die
    /// ins Leere lief: sie rief <c>DELETE /api/changeouts/{id}</c> — eine
    /// Route, die es nicht gab. Der Testbestand wuchs mit jedem Lauf, ohne
    /// dass etwas gemeldet wurde.</para>
    /// </remarks>
    /// <returns><c>true</c>, wenn wirklich eine Zeile verschwunden ist.</returns>
    /// <summary>Einen Addback-/Nachfüll-Eintrag entfernen — nur in seinem Grow.</summary>
    public bool DeleteAddbackLog(int growId, int id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM AddbackLogs WHERE Id = $id AND GrowId = $growId;";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$growId", growId);
        return command.ExecuteNonQuery() > 0;
    }

    public bool DeleteChangeout(int growId, int id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        // Der Grow steht mit in der Bedingung: eine Id allein duerfte sonst
        // einen Eintrag eines FREMDEN Grows treffen.
        command.CommandText = """
            DELETE FROM ChangeoutEntries
            WHERE Id = $id AND GrowId = $growId;
        """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$growId", growId);
        return command.ExecuteNonQuery() > 0;
    }

    private bool GrowExists(int growId)
        => RowExists("Grows", growId);

    private bool HydroSetupExists(int hydroSetupId)
        => RowExists("GrowSystems", hydroSetupId);

    private bool RowExists(string tableName, int id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName} WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture) > 0;
    }

    private static AddbackLogEntry MapAddbackLog(SqliteDataReader reader)
    {
        return new AddbackLogEntry
        {
            Id = Convert.ToInt32(reader["Id"], CultureInfo.InvariantCulture),
            GrowId = Convert.ToInt32(reader["GrowId"], CultureInfo.InvariantCulture),
            HydroSetupId = reader["HydroSetupId"] is DBNull or null ? null : Convert.ToInt32(reader["HydroSetupId"], CultureInfo.InvariantCulture),
            Kind = ParseEnum(reader["Kind"]?.ToString(), AddbackLogKind.Addback),
            PerformedAtUtc = ParseStoredUtcDateTime(reader["PerformedAtUtc"]?.ToString()) ?? DateTime.UtcNow,
            ReservoirLiters = NullableDouble(reader["ReservoirLiters"]),
            EcBefore = NullableDouble(reader["EcBefore"]),
            EcTarget = NullableDouble(reader["EcTarget"]),
            EcStock = NullableDouble(reader["EcStock"]),
            EcAfter = NullableDouble(reader["EcAfter"]),
            PhBefore = NullableDouble(reader["PhBefore"]),
            PhAfter = NullableDouble(reader["PhAfter"]),
            LitersAdded = NullableDouble(reader["LitersAdded"]),
            NewReservoirVolumeLiters = NullableDouble(reader["NewReservoirVolumeLiters"]),
            UsedHydroSetupVolume = reader["UsedHydroSetupVolume"] is not DBNull and not null && Convert.ToInt32(reader["UsedHydroSetupVolume"], CultureInfo.InvariantCulture) == 1,
            WaterUsed = HasColumn(reader, "WaterUsed") && NullString(reader["WaterUsed"]) is { } wq
                ? Enum.TryParse<WaterSource>(wq, out var quelle) ? quelle : null
                : null,
            WaterEcMsCm = HasColumn(reader, "WaterEcMsCm") ? NullableDouble(reader["WaterEcMsCm"]) : null,
            Notes = NullString(reader["Notes"]),
            CreatedAtUtc = ParseStoredUtcDateTime(reader["CreatedAtUtc"]?.ToString()) ?? DateTime.UtcNow
        };
    }

    private static ChangeoutEntry MapChangeout(SqliteDataReader reader)
    {
        return new ChangeoutEntry
        {
            Id = Convert.ToInt32(reader["Id"], CultureInfo.InvariantCulture),
            GrowId = Convert.ToInt32(reader["GrowId"], CultureInfo.InvariantCulture),
            HydroSetupId = reader["HydroSetupId"] is DBNull or null ? null : Convert.ToInt32(reader["HydroSetupId"], CultureInfo.InvariantCulture),
            Kind = ParseEnum(reader["Kind"]?.ToString(), ChangeoutKind.Partial),
            PerformedAtUtc = ParseStoredUtcDateTime(reader["PerformedAtUtc"]?.ToString()) ?? DateTime.UtcNow,
            VolumeChangedLiters = NullableDouble(reader["VolumeChangedLiters"]),
            PercentChanged = NullableDouble(reader["PercentChanged"]),
            EcBefore = NullableDouble(reader["EcBefore"]),
            EcAfter = NullableDouble(reader["EcAfter"]),
            PhBefore = NullableDouble(reader["PhBefore"]),
            PhAfter = NullableDouble(reader["PhAfter"]),
            WaterUsed = HasColumn(reader, "WaterUsed") && NullString(reader["WaterUsed"]) is { } cq
                ? Enum.TryParse<WaterSource>(cq, out var cQuelle) ? cQuelle : null
                : null,
            WaterEcMsCm = HasColumn(reader, "WaterEcMsCm") ? NullableDouble(reader["WaterEcMsCm"]) : null,
            Notes = NullString(reader["Notes"]),
            CreatedAtUtc = ParseStoredUtcDateTime(reader["CreatedAtUtc"]?.ToString()) ?? DateTime.UtcNow
        };
    }

    private static void AddAddbackLogParameters(SqliteCommand command, AddbackLogEntry entry)
    {
        command.Parameters.AddWithValue("$growId", entry.GrowId);
        command.Parameters.AddWithValue("$hydroSetupId", (object?)entry.HydroSetupId ?? DBNull.Value);
        command.Parameters.AddWithValue("$kind", entry.Kind.ToString());
        command.Parameters.AddWithValue("$performedAtUtc", ToStorageUtc(entry.PerformedAtUtc));
        AddNullable(command, "$reservoirLiters", entry.ReservoirLiters);
        AddNullable(command, "$ecBefore", entry.EcBefore);
        AddNullable(command, "$ecTarget", entry.EcTarget);
        AddNullable(command, "$ecStock", entry.EcStock);
        AddNullable(command, "$ecAfter", entry.EcAfter);
        AddNullable(command, "$phBefore", entry.PhBefore);
        AddNullable(command, "$phAfter", entry.PhAfter);
        AddNullable(command, "$litersAdded", entry.LitersAdded);
        AddNullable(command, "$newReservoirVolumeLiters", entry.NewReservoirVolumeLiters);
        command.Parameters.AddWithValue("$usedHydroSetupVolume", entry.UsedHydroSetupVolume ? 1 : 0);
        command.Parameters.AddWithValue("$waterUsed", (object?)entry.WaterUsed?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$waterEcMsCm", (object?)entry.WaterEcMsCm ?? DBNull.Value);
        command.Parameters.AddWithValue("$notes", (object?)entry.Notes ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAtUtc", ToStorageUtc(entry.CreatedAtUtc));
    }

    private static void AddChangeoutParameters(SqliteCommand command, ChangeoutEntry entry)
    {
        command.Parameters.AddWithValue("$growId", entry.GrowId);
        command.Parameters.AddWithValue("$hydroSetupId", (object?)entry.HydroSetupId ?? DBNull.Value);
        command.Parameters.AddWithValue("$kind", entry.Kind.ToString());
        command.Parameters.AddWithValue("$performedAtUtc", ToStorageUtc(entry.PerformedAtUtc));
        AddNullable(command, "$volumeChangedLiters", entry.VolumeChangedLiters);
        AddNullable(command, "$percentChanged", entry.PercentChanged);
        AddNullable(command, "$ecBefore", entry.EcBefore);
        AddNullable(command, "$ecAfter", entry.EcAfter);
        AddNullable(command, "$phBefore", entry.PhBefore);
        AddNullable(command, "$phAfter", entry.PhAfter);
        command.Parameters.AddWithValue("$waterUsed", (object?)entry.WaterUsed?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$waterEcMsCm", (object?)entry.WaterEcMsCm ?? DBNull.Value);
        command.Parameters.AddWithValue("$notes", (object?)entry.Notes ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAtUtc", ToStorageUtc(entry.CreatedAtUtc));
    }

    private static void ValidateAddbackLog(AddbackLogEntry entry)
    {
        if (!Enum.IsDefined(entry.Kind))
        {
            throw new InvalidOperationException("Addback log kind is invalid.");
        }

        ValidateNonNegative(entry.ReservoirLiters, "Reservoir volume");
        ValidateNonNegative(entry.EcBefore, "EC before");
        ValidateNonNegative(entry.EcTarget, "EC target");
        ValidateNonNegative(entry.EcStock, "EC stock");
        ValidateNonNegative(entry.EcAfter, "EC after");
        ValidateNonNegative(entry.LitersAdded, "Addback liters");
        ValidateNonNegative(entry.NewReservoirVolumeLiters, "New reservoir volume");
        ValidatePh(entry.PhBefore, "pH before");
        ValidatePh(entry.PhAfter, "pH after");
    }

    private static void ValidateChangeout(ChangeoutEntry entry)
    {
        if (!Enum.IsDefined(entry.Kind))
        {
            throw new InvalidOperationException("Changeout kind is invalid.");
        }

        ValidateNonNegative(entry.VolumeChangedLiters, "Changeout volume");
        ValidateNonNegative(entry.PercentChanged, "Changeout percent");
        ValidateNonNegative(entry.EcBefore, "EC before");
        ValidateNonNegative(entry.EcAfter, "EC after");
        ValidatePh(entry.PhBefore, "pH before");
        ValidatePh(entry.PhAfter, "pH after");

        if (entry.PercentChanged is < 0 or > 100)
        {
            throw new InvalidOperationException("Changeout percent must be between 0 and 100.");
        }
    }

    private static void ValidateNonNegative(double? value, string label)
    {
        if (value is < 0)
        {
            throw new InvalidOperationException($"{label} must not be negative.");
        }
    }

    private static void ValidatePh(double? value, string label)
    {
        if (value is < 0 or > 14)
        {
            throw new InvalidOperationException($"{label} must be between 0 and 14.");
        }
    }
}
