namespace Sheddueller.Postgres.Internal.Operations;

using Npgsql;

using Sheddueller.Storage;

internal static class PostgresReaders
{
    public static ClaimedJob ReadClaimedJob(NpgsqlDataReader reader, IReadOnlyList<string> groupKeys)
      => new(
        reader.GetGuid(0),
        reader.GetInt64(1),
        reader.GetInt32(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetFieldValue<string[]>(5),
        PostgresConversion.ToPayload(reader.GetValue(8), reader.GetValue(9)),
        groupKeys,
        reader.GetInt32(10),
        reader.GetInt32(11),
        reader.GetGuid(12),
        PostgresConversion.ToDateTimeOffset(reader.GetValue(13)),
        PostgresConversion.ToRetryBackoffKind(reader.GetValue(14)),
        PostgresConversion.FromMilliseconds(reader.GetValue(15)),
        PostgresConversion.FromMilliseconds(reader.GetValue(16)),
        reader.IsDBNull(17) ? null : reader.GetString(17),
        reader.IsDBNull(18) ? null : PostgresConversion.ToDateTimeOffset(reader.GetValue(18)),
        PostgresConversion.ToInvocationTargetKind(reader.GetValue(6)),
        PostgresConversion.ToParameterBindings(reader.GetValue(7)));

    public static PostgresClaimedJob ReadPostgresClaimedJob(NpgsqlDataReader reader, IReadOnlyList<string> groupKeys)
      => new(
        reader.GetGuid(0),
        reader.GetInt32(1),
        reader.GetInt32(2),
        PostgresConversion.ToRetryBackoffKind(reader.GetValue(3)),
        PostgresConversion.FromMilliseconds(reader.GetValue(4)),
        PostgresConversion.FromMilliseconds(reader.GetValue(5)),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        groupKeys);

    public static PostgresScheduleDefinition ReadScheduleDefinition(
        NpgsqlDataReader reader,
        IReadOnlyList<string> groupKeys,
        IReadOnlyList<JobTag> tags)
      => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetBoolean(2),
        PostgresConversion.ToRecurringOverlapMode(reader.GetValue(3)),
        reader.GetInt32(4),
        reader.GetString(5),
        reader.GetString(6),
        reader.GetFieldValue<string[]>(7),
        PostgresConversion.ToInvocationTargetKind(reader.GetValue(8)),
        PostgresConversion.ToParameterBindings(reader.GetValue(9)),
        PostgresConversion.ToPayload(reader.GetValue(10), reader.GetValue(11)),
        groupKeys,
        tags,
        PostgresConversion.ToRetryPolicy(
          reader.GetBoolean(12),
          reader.GetInt32(13),
          reader.GetValue(14),
          reader.GetValue(15),
          reader.GetValue(16)),
        reader.IsDBNull(17) ? null : PostgresConversion.ToDateTimeOffset(reader.GetValue(17)));
}
