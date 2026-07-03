namespace Sheddueller.Postgres.Tests;

using Microsoft.Extensions.DependencyInjection;

using Sheddueller;
using Sheddueller.Inspection.Metrics;
using Sheddueller.Storage;

using Shouldly;

public sealed class PostgresMetricsRollupTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task MetricsRead_OldRollupBuckets_RemovesExpiredBucketsAndHistogramBins()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        await InsertOldRollupAsync(context);

        await context.Provider.GetRequiredService<IMetricsInspectionReader>()
          .GetMetricsAsync(new MetricsInspectionQuery([TimeSpan.FromMinutes(5)]));

        (await CountAsync(context, "metrics_buckets")).ShouldBe(0L);
        (await CountAsync(context, "metrics_histogram_bins")).ShouldBe(0L);
    }

    [Fact]
    public async Task MetricsRead_PersistedMetricsRetention_RetainsBucketsInsideConfiguredWindow()
    {
        await using var context = await PostgresTestContext.CreateMigratedAsync(fixture);
        await context.Provider.GetRequiredService<IShedduellerCleanupConfigurationStore>()
          .SetCleanupConfigurationAsync(new ShedduellerCleanupConfiguration(
            JobRetentionCleanupConfiguration.FromOptions(new JobRetentionOptions()),
            new JobEventCleanupConfiguration(TimeSpan.FromDays(7), TimeSpan.FromHours(1)),
            new MetricsCleanupConfiguration(TimeSpan.FromDays(10), TimeSpan.FromHours(1))));
        await InsertOldRollupAsync(context);

        await context.Provider.GetRequiredService<IMetricsInspectionReader>()
          .GetMetricsAsync(new MetricsInspectionQuery([TimeSpan.FromMinutes(5)]));

        (await CountAsync(context, "metrics_buckets")).ShouldBe(1L);
        (await CountAsync(context, "metrics_histogram_bins")).ShouldBe(1L);
    }

    private static async ValueTask InsertOldRollupAsync(PostgresTestContext context)
    {
        await using var command = context.DataSource.CreateCommand(
          $"""
          insert into {context.Table("metrics_buckets")} (
              bucket_started_at_utc,
              enqueued_count)
          values (
              transaction_timestamp() - interval '8 days',
              1);

          insert into {context.Table("metrics_histogram_bins")} (
              bucket_started_at_utc,
              metric,
              bin_index,
              sample_count)
          values (
              (select bucket_started_at_utc from {context.Table("metrics_buckets")} limit 1),
              'queue_latency',
              0,
              1);
          """);
        await command.ExecuteNonQueryAsync();
    }

    private static async ValueTask<long> CountAsync(
        PostgresTestContext context,
        string tableName)
    {
        await using var command = context.DataSource.CreateCommand($"select count(*) from {context.Table(tableName)};");
        var result = await command.ExecuteScalarAsync();
        result.ShouldNotBeNull();
        return result.ShouldBeOfType<long>();
    }
}
