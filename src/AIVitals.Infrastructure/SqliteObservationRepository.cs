using System.Globalization;
using AIVitals.Application;
using AIVitals.Domain;
using Microsoft.Data.Sqlite;

namespace AIVitals.Infrastructure;

public sealed class SqliteObservationRepository : IObservationRepository
{
    private readonly string _connectionString;

    public SqliteObservationRepository(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            // Short operations keep ownership simple and let tests, recovery and deletion
            // release the database file deterministically on Windows.
            Pooling = false
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;
            CREATE TABLE IF NOT EXISTS observations (
                id TEXT NOT NULL PRIMARY KEY,
                provider_id TEXT NOT NULL,
                connection_id TEXT NOT NULL,
                capability INTEGER NOT NULL,
                value TEXT NULL,
                unit TEXT NOT NULL,
                observed_at_utc TEXT NOT NULL,
                source TEXT NOT NULL,
                quality INTEGER NOT NULL,
                window_starts_at_utc TEXT NULL,
                window_resets_at_utc TEXT NULL,
                model TEXT NULL,
                anonymous_session_id TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_observations_latest
                ON observations(observed_at_utc DESC);
            PRAGMA user_version = 1;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AppendAsync(UsageObservation observation, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO observations (
                id, provider_id, connection_id, capability, value, unit, observed_at_utc,
                source, quality, window_starts_at_utc, window_resets_at_utc, model, anonymous_session_id)
            VALUES (
                $id, $provider, $connection, $capability, $value, $unit, $observed,
                $source, $quality, $windowStart, $windowReset, $model, $session);
            """;
        command.Parameters.AddWithValue("$id", observation.Id.ToString("D"));
        command.Parameters.AddWithValue("$provider", observation.ProviderId);
        command.Parameters.AddWithValue("$connection", observation.ConnectionId);
        command.Parameters.AddWithValue("$capability", (int)observation.Capability);
        command.Parameters.AddWithValue("$value", DbValue(observation.Value?.ToString(CultureInfo.InvariantCulture)));
        command.Parameters.AddWithValue("$unit", observation.Unit);
        command.Parameters.AddWithValue("$observed", observation.ObservedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$source", observation.Source);
        command.Parameters.AddWithValue("$quality", (int)observation.Quality);
        command.Parameters.AddWithValue("$windowStart", DbValue(observation.Window?.StartsAtUtc.ToString("O")));
        command.Parameters.AddWithValue("$windowReset", DbValue(observation.Window?.ResetsAtUtc?.ToString("O")));
        command.Parameters.AddWithValue("$model", DbValue(observation.Model));
        command.Parameters.AddWithValue("$session", DbValue(observation.AnonymousSessionId));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<UsageObservation?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, provider_id, connection_id, capability, value, unit, observed_at_utc,
                   source, quality, window_starts_at_utc, window_resets_at_utc, model, anonymous_session_id
            FROM observations
            ORDER BY observed_at_utc DESC, rowid DESC
            LIMIT 1;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;

        return ReadObservation(reader);
    }

    public async Task<IReadOnlyDictionary<string, UsageObservation>> GetLatestByProviderAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, provider_id, connection_id, capability, value, unit, observed_at_utc,
                   source, quality, window_starts_at_utc, window_resets_at_utc, model, anonymous_session_id
            FROM (
                SELECT *, ROW_NUMBER() OVER (
                    PARTITION BY provider_id
                    ORDER BY observed_at_utc DESC, rowid DESC
                ) AS provider_rank
                FROM observations
            )
            WHERE provider_rank = 1;
            """;

        var observations = new Dictionary<string, UsageObservation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var observation = ReadObservation(reader);
            observations[observation.ProviderId] = observation;
        }

        return observations;
    }

    public async Task<IReadOnlyList<UsageObservation>> QueryAsync(
        ObservationQuery query,
        CancellationToken cancellationToken = default)
    {
        query = query.Normalize();
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT id, provider_id, connection_id, capability, value, unit, observed_at_utc,
                   source, quality, window_starts_at_utc, window_resets_at_utc, model, anonymous_session_id
            FROM observations
            WHERE ($from IS NULL OR observed_at_utc >= $from)
              AND ($to IS NULL OR observed_at_utc < $to)
              AND ($provider IS NULL OR provider_id = $provider)
              AND ($capability IS NULL OR capability = $capability)
            ORDER BY observed_at_utc DESC, rowid DESC
            LIMIT {query.Limit};
            """;
        AddQueryParameters(command, query);

        var observations = new List<UsageObservation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            observations.Add(ReadObservation(reader));
        return observations;
    }

    public async Task<int> DeleteAsync(ObservationQuery query, CancellationToken cancellationToken = default)
    {
        query = query.Normalize();
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            DELETE FROM observations
            WHERE ($from IS NULL OR observed_at_utc >= $from)
              AND ($to IS NULL OR observed_at_utc < $to)
              AND ($provider IS NULL OR provider_id = $provider)
              AND ($capability IS NULL OR capability = $capability);
            """;
        AddQueryParameters(command, query);
        var deleted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return deleted;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static object DbValue(string? value) => value is null ? DBNull.Value : value;

    private static void AddQueryParameters(SqliteCommand command, ObservationQuery query)
    {
        command.Parameters.AddWithValue("$from", DbValue(query.FromUtc?.ToString("O")));
        command.Parameters.AddWithValue("$to", DbValue(query.ToUtc?.ToString("O")));
        command.Parameters.AddWithValue("$provider", DbValue(query.ProviderId));
        command.Parameters.AddWithValue("$capability", query.Capability is null ? DBNull.Value : (int)query.Capability.Value);
    }

    private static DateTimeOffset? ReadDateTimeOffset(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static UsageObservation ReadObservation(SqliteDataReader reader)
    {
        var windowStart = ReadDateTimeOffset(reader, 9);
        var windowReset = ReadDateTimeOffset(reader, 10);
        var window = windowStart is null ? null : new QuotaWindow(windowStart.Value, windowReset);

        return new UsageObservation(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            (UsageCapability)reader.GetInt32(3),
            reader.IsDBNull(4) ? null : decimal.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
            reader.GetString(5),
            DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            reader.GetString(7),
            (DataQuality)reader.GetInt32(8),
            window,
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12));
    }
}
