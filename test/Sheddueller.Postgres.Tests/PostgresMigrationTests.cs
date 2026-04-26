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
}
