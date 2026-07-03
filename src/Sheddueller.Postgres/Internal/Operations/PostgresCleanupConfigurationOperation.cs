namespace Sheddueller.Postgres.Internal.Operations;

using System.Text.Json;

using Npgsql;

using NpgsqlTypes;

using Sheddueller;

internal static class PostgresCleanupConfigurationOperation
{
    private const string JobRetentionKey = "cleanup.job_retention";
    private const string JobEventsKey = "cleanup.job_events";
    private const string MetricsKey = "cleanup.metrics";

    public static async ValueTask<ShedduellerCleanupConfiguration> GetAsync(
        PostgresOperationContext context,
        ShedduellerCleanupConfiguration defaultConfiguration,
        CancellationToken cancellationToken)
    {
        Validate(defaultConfiguration);

        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var configuration = new ShedduellerCleanupConfiguration(
          await ReadJobRetentionAsync(context, connection, transaction, defaultConfiguration.JobRetention, cancellationToken).ConfigureAwait(false),
          await ReadJobEventsAsync(context, connection, transaction, defaultConfiguration.JobEvents, cancellationToken).ConfigureAwait(false),
          await ReadMetricsAsync(context, connection, transaction, defaultConfiguration.Metrics, cancellationToken).ConfigureAwait(false));

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return configuration;
    }

    public static async ValueTask<JobRetentionCleanupConfiguration> GetJobRetentionAsync(
        PostgresOperationContext context,
        JobRetentionCleanupConfiguration defaultConfiguration,
        CancellationToken cancellationToken)
    {
        Validate(defaultConfiguration);

        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var configuration = await ReadJobRetentionAsync(context, connection, transaction, defaultConfiguration, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return configuration;
    }

    public static async ValueTask<JobEventCleanupConfiguration> GetJobEventsAsync(
        PostgresOperationContext context,
        JobEventCleanupConfiguration defaultConfiguration,
        CancellationToken cancellationToken)
    {
        Validate(defaultConfiguration);

        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var configuration = await ReadJobEventsAsync(context, connection, transaction, defaultConfiguration, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return configuration;
    }

    public static async ValueTask<MetricsCleanupConfiguration> GetMetricsAsync(
        PostgresOperationContext context,
        MetricsCleanupConfiguration defaultConfiguration,
        CancellationToken cancellationToken)
    {
        Validate(defaultConfiguration);

        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var configuration = await ReadMetricsAsync(context, connection, transaction, defaultConfiguration, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return configuration;
    }

    public static async ValueTask SetAsync(
        PostgresOperationContext context,
        ShedduellerCleanupConfiguration configuration,
        CancellationToken cancellationToken)
    {
        Validate(configuration);

        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await UpsertAsync(context, connection, transaction, JobRetentionKey, ToDocument(configuration.JobRetention), cancellationToken).ConfigureAwait(false);
        await UpsertAsync(context, connection, transaction, JobEventsKey, ToDocument(configuration.JobEvents), cancellationToken).ConfigureAwait(false);
        await UpsertAsync(context, connection, transaction, MetricsKey, ToDocument(configuration.Metrics), cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<JobRetentionCleanupConfiguration> ReadJobRetentionAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        JobRetentionCleanupConfiguration defaultConfiguration,
        CancellationToken cancellationToken)
    {
        await SeedAsync(context, connection, transaction, JobRetentionKey, ToDocument(defaultConfiguration), cancellationToken).ConfigureAwait(false);
        var document = await ReadAsync<JobRetentionSettingsDocument>(context, connection, transaction, JobRetentionKey, cancellationToken).ConfigureAwait(false);
        var configuration = document.ToConfiguration();
        Validate(configuration);

        return configuration;
    }

    private static async ValueTask<JobEventCleanupConfiguration> ReadJobEventsAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        JobEventCleanupConfiguration defaultConfiguration,
        CancellationToken cancellationToken)
    {
        await SeedAsync(context, connection, transaction, JobEventsKey, ToDocument(defaultConfiguration), cancellationToken).ConfigureAwait(false);
        var document = await ReadAsync<JobEventSettingsDocument>(context, connection, transaction, JobEventsKey, cancellationToken).ConfigureAwait(false);
        var configuration = document.ToConfiguration();
        Validate(configuration);

        return configuration;
    }

    private static async ValueTask<MetricsCleanupConfiguration> ReadMetricsAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        MetricsCleanupConfiguration defaultConfiguration,
        CancellationToken cancellationToken)
    {
        await SeedAsync(context, connection, transaction, MetricsKey, ToDocument(defaultConfiguration), cancellationToken).ConfigureAwait(false);
        var document = await ReadAsync<MetricsSettingsDocument>(context, connection, transaction, MetricsKey, cancellationToken).ConfigureAwait(false);
        var configuration = document.ToConfiguration();
        Validate(configuration);

        return configuration;
    }

    private static async ValueTask SeedAsync<TDocument>(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string key,
        TDocument document,
        CancellationToken cancellationToken)
      => await PostgresOperationContext.ExecuteCountAsync(
        connection,
        transaction,
        $"""
        insert into {context.Names.Settings} (setting_key, value, updated_at_utc)
        values (@setting_key, @value, transaction_timestamp())
        on conflict (setting_key) do nothing;
        """,
        command => AddSettingParameters(command, key, document),
        cancellationToken)
        .ConfigureAwait(false);

    private static async ValueTask UpsertAsync<TDocument>(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string key,
        TDocument document,
        CancellationToken cancellationToken)
      => await PostgresOperationContext.ExecuteCountAsync(
        connection,
        transaction,
        $"""
        insert into {context.Names.Settings} (setting_key, value, updated_at_utc)
        values (@setting_key, @value, transaction_timestamp())
        on conflict (setting_key) do update
        set value = excluded.value,
            updated_at_utc = excluded.updated_at_utc;
        """,
        command => AddSettingParameters(command, key, document),
        cancellationToken)
        .ConfigureAwait(false);

    private static async ValueTask<TDocument> ReadAsync<TDocument>(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string key,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
          $"""
          select value::text
          from {context.Names.Settings}
          where setting_key = @setting_key;
          """;
        command.Parameters.AddWithValue("setting_key", key);

        var json = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string
          ?? throw new InvalidOperationException($"PostgreSQL cleanup setting '{key}' was not found.");
        try
        {
            return JsonSerializer.Deserialize<TDocument>(json)
              ?? throw new InvalidOperationException($"PostgreSQL cleanup setting '{key}' was empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"PostgreSQL cleanup setting '{key}' is not valid JSON.", exception);
        }
    }

    private static void AddSettingParameters<TDocument>(
        NpgsqlCommand command,
        string key,
        TDocument document)
    {
        command.Parameters.AddWithValue("setting_key", key);
        command.Parameters.Add("value", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(document);
    }

    private static JobRetentionSettingsDocument ToDocument(JobRetentionCleanupConfiguration configuration)
      => new(
        configuration.Enabled,
        configuration.CompletedRetention?.Ticks,
        configuration.FailedRetention?.Ticks,
        configuration.CanceledRetention?.Ticks,
        configuration.CleanupInterval.Ticks,
        configuration.BatchSize);

    private static JobEventSettingsDocument ToDocument(JobEventCleanupConfiguration configuration)
      => new(configuration.Retention.Ticks, configuration.CleanupInterval.Ticks);

    private static MetricsSettingsDocument ToDocument(MetricsCleanupConfiguration configuration)
      => new(configuration.Retention.Ticks, configuration.CleanupInterval.Ticks);

    private static void Validate(ShedduellerCleanupConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Validate(configuration.JobRetention);
        Validate(configuration.JobEvents);
        Validate(configuration.Metrics);
    }

    private static void Validate(JobRetentionCleanupConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.CompletedRetention is { } completedRetention && completedRetention <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Job retention completed retention must be positive or null.");
        }

        if (configuration.FailedRetention is { } failedRetention && failedRetention <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Job retention failed retention must be positive or null.");
        }

        if (configuration.CanceledRetention is { } canceledRetention && canceledRetention <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Job retention canceled retention must be positive or null.");
        }

        if (configuration.CleanupInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Job retention cleanup interval must be positive.");
        }

        if (configuration.BatchSize <= 0)
        {
            throw new InvalidOperationException("Job retention batch size must be positive.");
        }
    }

    private static void Validate(JobEventCleanupConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.Retention <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Job event retention must be positive.");
        }

        if (configuration.CleanupInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Job event cleanup interval must be positive.");
        }
    }

    private static void Validate(MetricsCleanupConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.Retention <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Metrics retention must be positive.");
        }

        if (configuration.CleanupInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Metrics cleanup interval must be positive.");
        }
    }

    private sealed record JobRetentionSettingsDocument(
        bool Enabled,
        long? CompletedRetentionTicks,
        long? FailedRetentionTicks,
        long? CanceledRetentionTicks,
        long CleanupIntervalTicks,
        int BatchSize)
    {
        public JobRetentionCleanupConfiguration ToConfiguration()
          => new(
            this.Enabled,
            ToNullableTimeSpan(this.CompletedRetentionTicks),
            ToNullableTimeSpan(this.FailedRetentionTicks),
            ToNullableTimeSpan(this.CanceledRetentionTicks),
            TimeSpan.FromTicks(this.CleanupIntervalTicks),
            this.BatchSize);
    }

    private sealed record JobEventSettingsDocument(
        long RetentionTicks,
        long CleanupIntervalTicks)
    {
        public JobEventCleanupConfiguration ToConfiguration()
          => new(TimeSpan.FromTicks(this.RetentionTicks), TimeSpan.FromTicks(this.CleanupIntervalTicks));
    }

    private sealed record MetricsSettingsDocument(
        long RetentionTicks,
        long CleanupIntervalTicks)
    {
        public MetricsCleanupConfiguration ToConfiguration()
          => new(TimeSpan.FromTicks(this.RetentionTicks), TimeSpan.FromTicks(this.CleanupIntervalTicks));
    }

    private static TimeSpan? ToNullableTimeSpan(long? ticks)
      => ticks is { } value ? TimeSpan.FromTicks(value) : null;
}
