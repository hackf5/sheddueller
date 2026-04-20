namespace Sheddueller.Postgres.Internal.Operations;

using Npgsql;

using Sheddueller.Storage;

internal static class PostgresReaders
{
    public static ClaimedTask ReadClaimedTask(NpgsqlDataReader reader, IReadOnlyList<string> groupKeys)
      => new(
        reader.GetGuid(0),
        reader.GetInt64(1),
        reader.GetInt32(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetFieldValue<string[]>(5),
        PostgresConversion.ToPayload(reader.GetValue(6), reader.GetValue(7)),
        groupKeys,
        reader.GetInt32(8),
        reader.GetInt32(9),
        reader.GetGuid(10),
        PostgresConversion.ToDateTimeOffset(reader.GetValue(11)),
        PostgresConversion.ToRetryBackoffKind(reader.GetValue(12)),
        PostgresConversion.FromMilliseconds(reader.GetValue(13)),
        PostgresConversion.FromMilliseconds(reader.GetValue(14)),
        reader.IsDBNull(15) ? null : reader.GetString(15),
        reader.IsDBNull(16) ? null : PostgresConversion.ToDateTimeOffset(reader.GetValue(16)));

    public static PostgresClaimedTask ReadPostgresClaimedTask(NpgsqlDataReader reader, IReadOnlyList<string> groupKeys)
      => new(
        reader.GetGuid(0),
        reader.GetInt32(1),
        reader.GetInt32(2),
        PostgresConversion.ToRetryBackoffKind(reader.GetValue(3)),
        PostgresConversion.FromMilliseconds(reader.GetValue(4)),
        PostgresConversion.FromMilliseconds(reader.GetValue(5)),
        groupKeys);

    public static PostgresScheduleDefinition ReadScheduleDefinition(NpgsqlDataReader reader, IReadOnlyList<string> groupKeys)
      => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetBoolean(2),
        PostgresConversion.ToRecurringOverlapMode(reader.GetValue(3)),
        reader.GetInt32(4),
        reader.GetString(5),
        reader.GetString(6),
        reader.GetFieldValue<string[]>(7),
        PostgresConversion.ToPayload(reader.GetValue(8), reader.GetValue(9)),
        groupKeys,
        PostgresConversion.ToRetryPolicy(
          reader.GetBoolean(10),
          reader.GetInt32(11),
          reader.GetValue(12),
          reader.GetValue(13),
          reader.GetValue(14)),
        reader.IsDBNull(15) ? null : PostgresConversion.ToDateTimeOffset(reader.GetValue(15)));
}
