using System.Globalization;
using Microsoft.Data.Sqlite;

namespace LogitechBatteryDisplay;

internal sealed class BatteryHistoryStore
{
    private readonly string _databasePath;

    public BatteryHistoryStore(string databasePath)
    {
        _databasePath = databasePath;
        AppPaths.EnsureHistoryDatabaseMigrated();
        Initialize();
    }

    public void Record(BatterySnapshot snapshot)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO battery_history
                (recorded_at, percent, charge_state, state_group, is_success, device_name, receiver_path, source, message)
            VALUES
                ($recorded_at, $percent, $charge_state, $state_group, $is_success, $device_name, $receiver_path, $source, $message);
            """;
        command.Parameters.AddWithValue("$recorded_at", DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$percent", snapshot.Percent is int percent ? percent : DBNull.Value);
        command.Parameters.AddWithValue("$charge_state", snapshot.ChargeState.ToString());
        command.Parameters.AddWithValue("$state_group", Classify(snapshot));
        command.Parameters.AddWithValue("$is_success", snapshot.IsSuccess ? 1 : 0);
        command.Parameters.AddWithValue("$device_name", snapshot.DeviceName);
        command.Parameters.AddWithValue("$receiver_path", snapshot.ReceiverPath);
        command.Parameters.AddWithValue("$source", snapshot.Source);
        command.Parameters.AddWithValue("$message", snapshot.Message);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<BatteryHistoryEntry> GetEntries(DateTimeOffset since)
    {
        var entries = new List<BatteryHistoryEntry>();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT recorded_at, percent, charge_state, state_group, is_success, device_name, message
            FROM battery_history
            WHERE recorded_at >= $since
            ORDER BY recorded_at ASC;
            """;
        command.Parameters.AddWithValue("$since", since.ToString("O", CultureInfo.InvariantCulture));

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var recordedAt = DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            int? percent = reader.IsDBNull(1) ? null : reader.GetInt32(1);
            var chargeState = Enum.TryParse<BatteryChargeState>(reader.GetString(2), out var parsedState)
                ? parsedState
                : BatteryChargeState.Unknown;
            entries.Add(new BatteryHistoryEntry(
                recordedAt,
                percent,
                chargeState,
                reader.GetString(3),
                reader.GetInt32(4) != 0,
                reader.GetString(5),
                reader.GetString(6)));
        }

        return entries;
    }

    private void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath) ?? AppPaths.RootDirectory);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS battery_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                recorded_at TEXT NOT NULL,
                percent INTEGER NULL,
                charge_state TEXT NOT NULL,
                state_group TEXT NOT NULL,
                is_success INTEGER NOT NULL,
                device_name TEXT NOT NULL,
                receiver_path TEXT NOT NULL,
                source TEXT NOT NULL,
                message TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_battery_history_recorded_at
                ON battery_history(recorded_at);

            CREATE INDEX IF NOT EXISTS idx_battery_history_state_group
                ON battery_history(state_group, recorded_at);
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString());
        connection.Open();
        return connection;
    }

    private static string Classify(BatterySnapshot snapshot)
    {
        if (!snapshot.IsSuccess)
        {
            return BatteryHistoryStateGroups.Sleeping;
        }

        return snapshot.ChargeState is BatteryChargeState.Recharging or BatteryChargeState.SlowRecharge
            ? BatteryHistoryStateGroups.Charging
            : BatteryHistoryStateGroups.Using;
    }
}

internal static class BatteryHistoryStateGroups
{
    public const string Charging = "charging";
    public const string Using = "using";
    public const string Sleeping = "sleeping";
}

internal sealed record BatteryHistoryEntry(
    DateTimeOffset RecordedAt,
    int? Percent,
    BatteryChargeState ChargeState,
    string StateGroup,
    bool IsSuccess,
    string DeviceName,
    string Message);
