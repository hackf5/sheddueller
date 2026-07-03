namespace Sheddueller.Postgres.Tests;

using Microsoft.Extensions.DependencyInjection;

using Sheddueller.Postgres;
using Sheddueller.Postgres.Internal;

using Shouldly;

public sealed class PostgresMigrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Migration_FreshSchema_CreatesSchemaAndStampsVersion()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);

        (await context.ReadSchemaVersionAsync()).ShouldBe(PostgresNames.ExpectedSchemaVersion);
    }

    [Fact]
    public async Task Migration_Reapplied_IsIdempotent()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        var migrator = context.Provider.GetRequiredService<IPostgresMigrator>();

        await migrator.ApplyAsync();

        (await context.ReadSchemaVersionAsync()).ShouldBe(PostgresNames.ExpectedSchemaVersion);
    }

    [Fact]
    public async Task Migration_Reapplied_DropsRedundantIndexes()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        await ExecuteAsync(
          context,
          $"""
          create index idx_jobs_inspection_newest on {context.Table("jobs")} (enqueue_sequence desc);
          create index idx_job_events_job_sequence on {context.Table("job_events")} (job_id, event_sequence);
          """);
        (await IndexExistsAsync(context, "idx_jobs_inspection_newest")).ShouldBeTrue();
        (await IndexExistsAsync(context, "idx_job_events_job_sequence")).ShouldBeTrue();

        await context.Provider.GetRequiredService<IPostgresMigrator>().ApplyAsync();

        (await IndexExistsAsync(context, "idx_jobs_inspection_newest")).ShouldBeFalse();
        (await IndexExistsAsync(context, "idx_job_events_job_sequence")).ShouldBeFalse();
    }

    [Fact]
    public async Task Migration_FreshSchema_CreatesIndexedHandlerSearchColumn()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);

        (await ScalarAsync<bool>(
          context,
          "select exists (select 1 from pg_extension where extname = 'pg_trgm');"))
          .ShouldBeTrue();

        (await ScalarAsync<bool>(
          context,
          """
          select exists (
              select 1
              from information_schema.columns
              where table_schema = @schema_name
                and table_name = 'jobs'
                and column_name = 'handler_search_text'
                and is_generated = 'ALWAYS'
          );
          """))
          .ShouldBeTrue();

        var indexDefinition = await ScalarAsync<string>(
          context,
          """
          select indexdef
          from pg_indexes
          where schemaname = @schema_name
            and indexname = 'idx_jobs_inspection_handler_search_trgm';
          """);

        indexDefinition.ShouldContain("USING gin");
        indexDefinition.ShouldContain("handler_search_text");
        indexDefinition.ShouldContain("gin_trgm_ops");
    }

    [Fact]
    public async Task Migration_FreshSchema_CreatesQueuedIdempotencyKeyIndex()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);

        (await ScalarAsync<bool>(
          context,
          """
          select exists (
              select 1
              from information_schema.columns
              where table_schema = @schema_name
                and table_name = 'jobs'
                and column_name = 'idempotency_key'
          );
          """))
          .ShouldBeTrue();

        var indexDefinition = await ScalarAsync<string>(
          context,
          """
          select indexdef
          from pg_indexes
          where schemaname = @schema_name
            and indexname = 'idx_jobs_queued_idempotency_key';
          """);

        indexDefinition.ShouldContain("UNIQUE INDEX");
        indexDefinition.ShouldContain("idempotency_key");
        indexDefinition.ShouldContain("state = 'Queued'");
    }

    [Fact]
    public async Task Migration_FreshSchema_CreatesClaimedJobsByNodeIndex()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);

        var indexDefinition = await ScalarAsync<string>(
          context,
          """
          select indexdef
          from pg_indexes
          where schemaname = @schema_name
            and indexname = 'idx_jobs_claimed_by_node';
          """);

        var normalized = indexDefinition.ToLowerInvariant();
        normalized.ShouldContain("claimed_by_node_id");
        normalized.ShouldContain("enqueue_sequence");
        normalized.ShouldContain("state = 'claimed'");
        normalized.ShouldContain("claimed_by_node_id is not null");
    }

    [Fact]
    public async Task Migration_FreshSchema_CreatesMetricsRollupTables()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);

        await AssertTableExistsAsync(context, "metrics_buckets");
        await AssertTableExistsAsync(context, "metrics_histogram_bins");
        await AssertTableExistsAsync(context, "metrics_rollup_state");

        (await ScalarAsync<long>(
          context,
          $"select count(*) from {context.Table("metrics_buckets")};"))
          .ShouldBe(0L);
        (await ScalarAsync<long>(
          context,
          $"select count(*) from {context.Table("metrics_histogram_bins")};"))
          .ShouldBe(0L);
        (await ScalarAsync<long>(
          context,
          $"select count(*) from {context.Table("metrics_rollup_state")};"))
          .ShouldBe(1L);

        var indexDefinition = await ScalarAsync<string>(
          context,
          """
          select indexdef
          from pg_indexes
          where schemaname = @schema_name
            and indexname = 'idx_metrics_histogram_bins_metric_bucket';
          """);

        indexDefinition.ShouldContain("metric");
        indexDefinition.ShouldContain("bucket_started_at_utc");
        indexDefinition.ShouldContain("bin_index");
    }

    [Fact]
    public async Task Migration_FreshSchema_CreatesTagOrdinalColumnsAndIndexes()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);

        await AssertOrdinalColumnAsync(context, "job_tags");
        await AssertOrdinalColumnAsync(context, "schedule_tags");

        (await ScalarAsync<bool>(
          context,
          """
          select exists (
              select 1
              from pg_indexes
              where schemaname = @schema_name
                and indexname = 'idx_job_tags_job_id_ordinal'
                and indexdef like '%UNIQUE INDEX%'
          );
          """))
          .ShouldBeTrue();

        (await ScalarAsync<bool>(
          context,
          """
          select exists (
              select 1
              from pg_indexes
              where schemaname = @schema_name
                and indexname = 'idx_schedule_tags_schedule_key_ordinal'
                and indexdef like '%UNIQUE INDEX%'
          );
          """))
          .ShouldBeTrue();
    }

    private static async ValueTask<T> ScalarAsync<T>(
        PostgresTestContext context,
        string commandText)
    {
        await using var command = context.DataSource.CreateCommand(commandText);
        command.Parameters.AddWithValue("schema_name", context.SchemaName);
        var result = await command.ExecuteScalarAsync();
        result.ShouldNotBeNull();
        return result.ShouldBeOfType<T>();
    }

    private static async Task AssertOrdinalColumnAsync(
        PostgresTestContext context,
        string tableName)
      => (await ScalarAsync<bool>(
          context,
          """
          select exists (
              select 1
              from information_schema.columns
              where table_schema = @schema_name
                and table_name = @table_name
                and column_name = 'ordinal'
                and data_type = 'integer'
                and is_nullable = 'NO'
          );
          """,
          command => command.Parameters.AddWithValue("table_name", tableName)))
        .ShouldBeTrue();

    private static async Task AssertTableExistsAsync(
        PostgresTestContext context,
        string tableName)
      => (await ScalarAsync<bool>(
          context,
          """
          select exists (
              select 1
              from information_schema.tables
              where table_schema = @schema_name
                and table_name = @table_name
          );
          """,
          command => command.Parameters.AddWithValue("table_name", tableName)))
        .ShouldBeTrue();

    private static async ValueTask<T> ScalarAsync<T>(
        PostgresTestContext context,
        string commandText,
        Action<Npgsql.NpgsqlCommand> configure)
    {
        await using var command = context.DataSource.CreateCommand(commandText);
        command.Parameters.AddWithValue("schema_name", context.SchemaName);
        configure(command);
        var result = await command.ExecuteScalarAsync();
        result.ShouldNotBeNull();
        return result.ShouldBeOfType<T>();
    }

    private static async ValueTask ExecuteAsync(
        PostgresTestContext context,
        string commandText)
    {
        await using var command = context.DataSource.CreateCommand(commandText);
        await command.ExecuteNonQueryAsync();
    }

    private static async ValueTask<bool> IndexExistsAsync(
        PostgresTestContext context,
        string indexName)
      => await ScalarAsync<bool>(
          context,
          """
          select exists (
              select 1
              from pg_indexes
              where schemaname = @schema_name
                and indexname = @index_name
          );
          """,
          command => command.Parameters.AddWithValue("index_name", indexName));
}
