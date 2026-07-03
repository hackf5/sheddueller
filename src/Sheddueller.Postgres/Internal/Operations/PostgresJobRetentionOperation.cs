namespace Sheddueller.Postgres.Internal.Operations;

using System.Globalization;

using Npgsql;

using NpgsqlTypes;

using Sheddueller.Storage;

internal static class PostgresJobRetentionOperation
{
    private const int RetentionAdvisoryLockKey = 7870834;

    public static async ValueTask<JobRetentionCleanupResult> ExecuteAsync(
        PostgresOperationContext context,
        JobRetentionCleanupRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var lockAcquired = await TryAcquireCleanupLockAsync(context, connection, transaction, cancellationToken).ConfigureAwait(false);
        if (!lockAcquired)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return new JobRetentionCleanupResult(0);
        }

        var deletedCount = await DeleteTerminalJobsAsync(context, connection, transaction, request, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new JobRetentionCleanupResult(deletedCount);
    }

    private static async ValueTask<bool> TryAcquireCleanupLockAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select pg_try_advisory_xact_lock(@lock_key, hashtext(@schema_name));";
        command.Parameters.AddWithValue("lock_key", RetentionAdvisoryLockKey);
        command.Parameters.AddWithValue("schema_name", context.Options.SchemaName);

        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
          ?? throw new InvalidOperationException("PostgreSQL did not return an advisory lock result."));
    }

    private static async ValueTask<int> DeleteTerminalJobsAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        JobRetentionCleanupRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
          $"""
          with candidates as (
              select job_id
              from {context.Names.Jobs}
              where
                  (state = 'Completed' and @completed_before_utc is not null and completed_at_utc < @completed_before_utc)
                  or (state = 'Failed' and @failed_before_utc is not null and failed_at_utc < @failed_before_utc)
                  or (state = 'Canceled' and @canceled_before_utc is not null and canceled_at_utc < @canceled_before_utc)
              order by coalesce(completed_at_utc, failed_at_utc, canceled_at_utc) asc, enqueue_sequence asc
              limit @batch_size
              for update skip locked
          ),
          deleted_jobs as (
              delete from {context.Names.Jobs} job
              using candidates
              where job.job_id = candidates.job_id
              returning 1
          )
          select count(*) from deleted_jobs;
          """;
        AddNullableTimestampParameter(command, "completed_before_utc", request.CompletedBeforeUtc);
        AddNullableTimestampParameter(command, "failed_before_utc", request.FailedBeforeUtc);
        AddNullableTimestampParameter(command, "canceled_before_utc", request.CanceledBeforeUtc);
        command.Parameters.Add("batch_size", NpgsqlDbType.Integer).Value = request.BatchSize;

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
          ?? throw new InvalidOperationException("PostgreSQL did not return a terminal job cleanup count.");
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static void AddNullableTimestampParameter(
        NpgsqlCommand command,
        string name,
        DateTimeOffset? value)
      => command.Parameters.Add(name, NpgsqlDbType.TimestampTz).Value = value is { } timestamp ? timestamp : DBNull.Value;
}
