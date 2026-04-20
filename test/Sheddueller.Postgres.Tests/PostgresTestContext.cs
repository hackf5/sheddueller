#pragma warning disable CA1849 // Test row readers keep assertions compact and do not run on production paths.
#pragma warning disable CA2000 // Factory transfers ServiceProvider ownership to PostgresTestContext.

namespace Sheddueller.Postgres.Tests;

using System.Globalization;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Sheddueller.Postgres;
using Sheddueller.Storage;

internal sealed class PostgresTestContext(
    NpgsqlDataSource dataSource,
    ServiceProvider provider,
    string schemaName) : IAsyncDisposable
{
    public NpgsqlDataSource DataSource { get; } = dataSource;

    public ServiceProvider Provider { get; } = provider;

    public string SchemaName { get; } = schemaName;

    public IJobStore Store => this.Provider.GetRequiredService<IJobStore>();

    public static async ValueTask<PostgresTestContext> CreateMigratedAsync(PostgresFixture fixture)
    {
        var schemaName = CreateSchemaName();
        var provider = CreateProvider(fixture.DataSource, schemaName);
        await provider.GetRequiredService<IPostgresMigrator>().ApplyAsync();
        return new PostgresTestContext(fixture.DataSource, provider, schemaName);
    }

    public static ServiceProvider CreateProvider(NpgsqlDataSource dataSource, string schemaName)
    {
        var services = new ServiceCollection();
        services.AddSheddueller(sheddueller => sheddueller.UsePostgres(options =>
        {
            options.DataSource = dataSource;
            options.SchemaName = schemaName;
        }));

        return services.BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
      => await this.Provider.DisposeAsync();

    public async ValueTask MakeScheduleDueAsync(string scheduleKey)
      => await this.ExecuteAsync(
        $"""
        update {this.Table("recurring_schedules")}
        set next_fire_at_utc = transaction_timestamp() - interval '1 minute'
        where schedule_key = @schedule_key;
        """,
        command => command.Parameters.AddWithValue("schedule_key", scheduleKey));

    public async ValueTask ForceClaimExpiredAsync(Guid jobId)
      => await this.ExecuteAsync(
        $"""
        update {this.Table("jobs")}
        set lease_expires_at_utc = transaction_timestamp() - interval '1 millisecond'
        where job_id = @job_id;
        """,
        command => command.Parameters.AddWithValue("job_id", jobId));

    public async ValueTask<int?> ReadSchemaVersionAsync()
    {
        var result = await this.ExecuteScalarAsync(
          $"select schema_version from {this.Table("schema_info")} where singleton_id = 1;",
          _ => { });
        return result is null or DBNull ? null : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public async ValueTask<PostgresJobRow> ReadJobAsync(Guid jobId)
    {
        await using var command = this.DataSource.CreateCommand(
          $"""
          select
              job_id,
              state,
              priority,
              enqueue_sequence,
              enqueued_at_utc,
              not_before_utc,
              service_type,
              method_name,
              method_parameter_types,
              serialized_arguments_content_type,
              serialized_arguments,
              attempt_count,
              max_attempts,
              retry_backoff_kind,
              retry_base_delay_ms,
              retry_max_delay_ms,
              claimed_by_node_id,
              claimed_at_utc,
              lease_token,
              lease_expires_at_utc,
              last_heartbeat_at_utc,
              completed_at_utc,
              failed_at_utc,
              canceled_at_utc,
              failure_type_name,
              failure_message,
              failure_stack_trace,
              source_schedule_key,
              scheduled_fire_at_utc
          from {this.Table("jobs")}
          where job_id = @job_id;
          """);
        command.Parameters.AddWithValue("job_id", jobId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException($"Job '{jobId}' was not found.");
        }

        return new PostgresJobRow(
          reader.GetGuid(0),
          reader.GetString(1),
          reader.GetInt32(2),
          reader.GetInt64(3),
          ToDateTimeOffset(reader.GetValue(4)),
          reader.IsDBNull(5) ? null : ToDateTimeOffset(reader.GetValue(5)),
          reader.GetString(6),
          reader.GetString(7),
          reader.GetFieldValue<string[]>(8),
          reader.GetString(9),
          reader.GetFieldValue<byte[]>(10),
          reader.GetInt32(11),
          reader.GetInt32(12),
          reader.IsDBNull(13) ? null : reader.GetString(13),
          reader.IsDBNull(14) ? null : reader.GetInt64(14),
          reader.IsDBNull(15) ? null : reader.GetInt64(15),
          reader.IsDBNull(16) ? null : reader.GetString(16),
          reader.IsDBNull(17) ? null : ToDateTimeOffset(reader.GetValue(17)),
          reader.IsDBNull(18) ? null : reader.GetGuid(18),
          reader.IsDBNull(19) ? null : ToDateTimeOffset(reader.GetValue(19)),
          reader.IsDBNull(20) ? null : ToDateTimeOffset(reader.GetValue(20)),
          reader.IsDBNull(21) ? null : ToDateTimeOffset(reader.GetValue(21)),
          reader.IsDBNull(22) ? null : ToDateTimeOffset(reader.GetValue(22)),
          reader.IsDBNull(23) ? null : ToDateTimeOffset(reader.GetValue(23)),
          reader.IsDBNull(24) ? null : reader.GetString(24),
          reader.IsDBNull(25) ? null : reader.GetString(25),
          reader.IsDBNull(26) ? null : reader.GetString(26),
          reader.IsDBNull(27) ? null : reader.GetString(27),
          reader.IsDBNull(28) ? null : ToDateTimeOffset(reader.GetValue(28)));
    }

    public async ValueTask<PostgresScheduleRow> ReadScheduleAsync(string scheduleKey)
    {
        await using var command = this.DataSource.CreateCommand(
          $"""
          select
              schedule_key,
              cron_expression,
              is_paused,
              overlap_mode,
              priority,
              service_type,
              method_name,
              method_parameter_types,
              serialized_arguments_content_type,
              serialized_arguments,
              retry_policy_configured,
              max_attempts,
              retry_backoff_kind,
              retry_base_delay_ms,
              retry_max_delay_ms,
              next_fire_at_utc
          from {this.Table("recurring_schedules")}
          where schedule_key = @schedule_key;
          """);
        command.Parameters.AddWithValue("schedule_key", scheduleKey);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException($"Schedule '{scheduleKey}' was not found.");
        }

        return new PostgresScheduleRow(
          reader.GetString(0),
          reader.GetString(1),
          reader.GetBoolean(2),
          reader.GetString(3),
          reader.GetInt32(4),
          reader.GetString(5),
          reader.GetString(6),
          reader.GetFieldValue<string[]>(7),
          reader.GetString(8),
          reader.GetFieldValue<byte[]>(9),
          reader.GetBoolean(10),
          reader.GetInt32(11),
          reader.IsDBNull(12) ? null : reader.GetString(12),
          reader.IsDBNull(13) ? null : reader.GetInt64(13),
          reader.IsDBNull(14) ? null : reader.GetInt64(14),
          reader.IsDBNull(15) ? null : ToDateTimeOffset(reader.GetValue(15)));
    }

    public async ValueTask<IReadOnlyList<string>> ReadJobGroupKeysAsync(Guid jobId)
      => await this.ReadStringListAsync(
        $"select group_key from {this.Table("job_concurrency_groups")} where job_id = @id order by group_key asc;",
        command => command.Parameters.AddWithValue("id", jobId));

    public async ValueTask<IReadOnlyList<string>> ReadScheduleGroupKeysAsync(string scheduleKey)
      => await this.ReadStringListAsync(
        $"select group_key from {this.Table("schedule_concurrency_groups")} where schedule_key = @id order by group_key asc;",
        command => command.Parameters.AddWithValue("id", scheduleKey));

    public async ValueTask<PostgresConcurrencyGroupRow?> ReadConcurrencyGroupAsync(string groupKey)
    {
        await using var command = this.DataSource.CreateCommand(
          $"""
          select group_key, configured_limit, in_use_count
          from {this.Table("concurrency_groups")}
          where group_key = @group_key;
          """);
        command.Parameters.AddWithValue("group_key", groupKey);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new PostgresConcurrencyGroupRow(
          reader.GetString(0),
          reader.IsDBNull(1) ? null : reader.GetInt32(1),
          reader.GetInt32(2));
    }

    public async ValueTask<int> CountJobsForScheduleAsync(string scheduleKey)
    {
        var result = await this.ExecuteScalarAsync(
          $"select count(*) from {this.Table("jobs")} where source_schedule_key = @schedule_key;",
          command => command.Parameters.AddWithValue("schedule_key", scheduleKey));
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public async ValueTask<int> CountSchedulesAsync()
    {
        var result = await this.ExecuteScalarAsync(
          $"select count(*) from {this.Table("recurring_schedules")};",
          _ => { });
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public string Table(string tableName)
      => $"{Quote(this.SchemaName)}.{Quote(tableName)}";

    private async ValueTask<IReadOnlyList<string>> ReadStringListAsync(
        string commandText,
        Action<NpgsqlCommand> configure)
    {
        await using var command = this.DataSource.CreateCommand(commandText);
        configure(command);

        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private async ValueTask ExecuteAsync(
        string commandText,
        Action<NpgsqlCommand> configure)
    {
        await using var command = this.DataSource.CreateCommand(commandText);
        configure(command);
        await command.ExecuteNonQueryAsync();
    }

    private async ValueTask<object?> ExecuteScalarAsync(
        string commandText,
        Action<NpgsqlCommand> configure)
    {
        await using var command = this.DataSource.CreateCommand(commandText);
        configure(command);
        return await command.ExecuteScalarAsync();
    }

    private static string CreateSchemaName()
      => "sheddueller_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

    private static string Quote(string identifier)
      => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static DateTimeOffset ToDateTimeOffset(object value)
      => value switch
      {
          DateTimeOffset dateTimeOffset => dateTimeOffset,
          DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
          _ => throw new InvalidOperationException($"Unexpected timestamp type '{value.GetType()}'."),
      };
}

internal sealed record PostgresJobRow(
    Guid JobId,
    string State,
    int Priority,
    long EnqueueSequence,
    DateTimeOffset EnqueuedAtUtc,
    DateTimeOffset? NotBeforeUtc,
    string ServiceType,
    string MethodName,
    IReadOnlyList<string> MethodParameterTypes,
    string SerializedArgumentsContentType,
    byte[] SerializedArguments,
    int AttemptCount,
    int MaxAttempts,
    string? RetryBackoffKind,
    long? RetryBaseDelayMs,
    long? RetryMaxDelayMs,
    string? ClaimedByNodeId,
    DateTimeOffset? ClaimedAtUtc,
    Guid? LeaseToken,
    DateTimeOffset? LeaseExpiresAtUtc,
    DateTimeOffset? LastHeartbeatAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? FailedAtUtc,
    DateTimeOffset? CanceledAtUtc,
    string? FailureTypeName,
    string? FailureMessage,
    string? FailureStackTrace,
    string? SourceScheduleKey,
    DateTimeOffset? ScheduledFireAtUtc);

internal sealed record PostgresScheduleRow(
    string ScheduleKey,
    string CronExpression,
    bool IsPaused,
    string OverlapMode,
    int Priority,
    string ServiceType,
    string MethodName,
    IReadOnlyList<string> MethodParameterTypes,
    string SerializedArgumentsContentType,
    byte[] SerializedArguments,
    bool RetryPolicyConfigured,
    int MaxAttempts,
    string? RetryBackoffKind,
    long? RetryBaseDelayMs,
    long? RetryMaxDelayMs,
    DateTimeOffset? NextFireAtUtc);

internal sealed record PostgresConcurrencyGroupRow(
    string GroupKey,
    int? ConfiguredLimit,
    int InUseCount);
