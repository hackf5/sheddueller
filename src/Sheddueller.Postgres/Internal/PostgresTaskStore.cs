namespace Sheddueller.Postgres.Internal;

using System.Data;
using System.Globalization;

using Npgsql;

using Sheddueller.Scheduling;
using Sheddueller.Serialization;
using Sheddueller.Storage;

internal sealed class PostgresTaskStore(ShedduellerPostgresOptions options) : ITaskStore
{
    private const int ClaimCandidateLimit = 64;

    private readonly ShedduellerPostgresOptions _options = options;
    private readonly PostgresNames _names = new(options.SchemaName);

    public async ValueTask<EnqueueTaskResult> EnqueueAsync(
        EnqueueTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var connection = await this.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
          $"""
          insert into {this._names.Tasks} (
              task_id,
              state,
              priority,
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
              source_schedule_key,
              scheduled_fire_at_utc)
          values (
              @task_id,
              'Queued',
              @priority,
              transaction_timestamp(),
              @not_before_utc,
              @service_type,
              @method_name,
              @method_parameter_types,
              @serialized_arguments_content_type,
              @serialized_arguments,
              0,
              @max_attempts,
              @retry_backoff_kind,
              @retry_base_delay_ms,
              @retry_max_delay_ms,
              @source_schedule_key,
              @scheduled_fire_at_utc)
          returning enqueue_sequence;
          """;
        command.Parameters.AddWithValue("task_id", request.TaskId);
        command.Parameters.AddWithValue("priority", request.Priority);
        command.Parameters.AddWithValue("not_before_utc", ToDbValue(request.NotBeforeUtc));
        command.Parameters.AddWithValue("service_type", request.ServiceType);
        command.Parameters.AddWithValue("method_name", request.MethodName);
        command.Parameters.AddWithValue("method_parameter_types", request.MethodParameterTypes.ToArray());
        command.Parameters.AddWithValue("serialized_arguments_content_type", request.SerializedArguments.ContentType);
        command.Parameters.AddWithValue("serialized_arguments", request.SerializedArguments.Data);
        command.Parameters.AddWithValue("max_attempts", request.MaxAttempts);
        command.Parameters.AddWithValue("retry_backoff_kind", ToDbValue(PostgresConversion.ToText(request.RetryBackoffKind)));
        command.Parameters.AddWithValue("retry_base_delay_ms", ToDbValue(PostgresConversion.ToMilliseconds(request.RetryBaseDelay)));
        command.Parameters.AddWithValue("retry_max_delay_ms", ToDbValue(PostgresConversion.ToMilliseconds(request.RetryMaxDelay)));
        command.Parameters.AddWithValue("source_schedule_key", ToDbValue(request.SourceScheduleKey));
        command.Parameters.AddWithValue("scheduled_fire_at_utc", ToDbValue(request.ScheduledFireAtUtc));

        var enqueueSequence = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        await this.ReplaceTaskGroupsAsync(connection, transaction, request.TaskId, request.ConcurrencyGroupKeys, cancellationToken).ConfigureAwait(false);
        await this.NotifyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new EnqueueTaskResult(request.TaskId, enqueueSequence);
    }

    public async ValueTask<ClaimTaskResult> TryClaimNextAsync(
        ClaimTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var leaseDuration = request.LeaseExpiresAtUtc - request.ClaimedAtUtc;
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentException("Lease expiry must be after the claimed timestamp.", nameof(request));
        }

        await using var connection = await this.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

        var candidates = await this.ReadClaimCandidatesAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        foreach (var taskId in candidates)
        {
            var groupKeys = await this.ReadTaskGroupKeysAsync(connection, transaction, taskId, cancellationToken).ConfigureAwait(false);
            await this.EnsureGroupRowsAsync(connection, transaction, groupKeys, cancellationToken).ConfigureAwait(false);

            if (!await this.TryReserveGroupsAsync(connection, transaction, groupKeys, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var leaseToken = Guid.NewGuid();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
              $"""
              update {this._names.Tasks}
              set state = 'Claimed',
                  attempt_count = attempt_count + 1,
                  claimed_by_node_id = @node_id,
                  claimed_at_utc = transaction_timestamp(),
                  lease_token = @lease_token,
                  lease_expires_at_utc = transaction_timestamp() + @lease_duration,
                  last_heartbeat_at_utc = null
              where task_id = @task_id
                and state = 'Queued'
              returning
                  task_id,
                  enqueue_sequence,
                  priority,
                  service_type,
                  method_name,
                  method_parameter_types,
                  serialized_arguments_content_type,
                  serialized_arguments,
                  attempt_count,
                  max_attempts,
                  lease_token,
                  lease_expires_at_utc,
                  retry_backoff_kind,
                  retry_base_delay_ms,
                  retry_max_delay_ms,
                  source_schedule_key,
                  scheduled_fire_at_utc;
              """;
            command.Parameters.AddWithValue("task_id", taskId);
            command.Parameters.AddWithValue("node_id", request.NodeId);
            command.Parameters.AddWithValue("lease_token", leaseToken);
            command.Parameters.AddWithValue("lease_duration", leaseDuration);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var claimed = ReadClaimedTask(reader, groupKeys);
            await reader.DisposeAsync().ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return new ClaimTaskResult.Claimed(claimed);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new ClaimTaskResult.NoTaskAvailable();
    }

    public async ValueTask<bool> MarkCompletedAsync(
        CompleteTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var connection = await this.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var groupKeys = await this.TryLockCurrentClaimGroupsAsync(
          connection,
          transaction,
          request.TaskId,
          request.NodeId,
          request.LeaseToken,
          cancellationToken)
          .ConfigureAwait(false);

        if (groupKeys is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        await this.DecrementGroupsAsync(connection, transaction, groupKeys, cancellationToken).ConfigureAwait(false);
        var updated = await ExecuteCountAsync(
          connection,
          transaction,
          $"""
          update {this._names.Tasks}
          set state = 'Completed',
              completed_at_utc = transaction_timestamp()
          where task_id = @task_id
            and state = 'Claimed'
            and claimed_by_node_id = @node_id
            and lease_token = @lease_token
            and lease_expires_at_utc > transaction_timestamp();
          """,
          command =>
          {
              command.Parameters.AddWithValue("task_id", request.TaskId);
              command.Parameters.AddWithValue("node_id", request.NodeId);
              command.Parameters.AddWithValue("lease_token", request.LeaseToken);
          },
          cancellationToken)
          .ConfigureAwait(false);

        await this.NotifyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return updated == 1;
    }

    public async ValueTask<bool> MarkFailedAsync(
        FailTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var connection = await this.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var task = await this.TryReadCurrentClaimForFailureAsync(
          connection,
          transaction,
          request.TaskId,
          request.NodeId,
          request.LeaseToken,
          cancellationToken)
          .ConfigureAwait(false);

        if (task is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        await this.DecrementGroupsAsync(connection, transaction, task.GroupKeys, cancellationToken).ConfigureAwait(false);
        await this.ApplyFailedAttemptAsync(connection, transaction, task, request.Failure, cancellationToken).ConfigureAwait(false);
        await this.NotifyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    public async ValueTask<bool> RenewLeaseAsync(
        RenewLeaseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var leaseDuration = request.LeaseExpiresAtUtc - request.HeartbeatAtUtc;
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentException("Lease expiry must be after the heartbeat timestamp.", nameof(request));
        }

        await using var connection = await this.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var updated = await ExecuteCountAsync(
          connection,
          transaction,
          $"""
          update {this._names.Tasks}
          set last_heartbeat_at_utc = transaction_timestamp(),
              lease_expires_at_utc = transaction_timestamp() + @lease_duration
          where task_id = @task_id
            and state = 'Claimed'
            and claimed_by_node_id = @node_id
            and lease_token = @lease_token
            and lease_expires_at_utc > transaction_timestamp();
          """,
          command =>
          {
              command.Parameters.AddWithValue("task_id", request.TaskId);
              command.Parameters.AddWithValue("node_id", request.NodeId);
              command.Parameters.AddWithValue("lease_token", request.LeaseToken);
              command.Parameters.AddWithValue("lease_duration", leaseDuration);
          },
          cancellationToken)
          .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return updated == 1;
    }

    public async ValueTask<bool> ReleaseTaskAsync(
        ReleaseTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var connection = await this.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var groupKeys = await this.TryLockCurrentClaimGroupsAsync(
          connection,
          transaction,
          request.TaskId,
          request.NodeId,
          request.LeaseToken,
          cancellationToken)
          .ConfigureAwait(false);

        if (groupKeys is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        await this.DecrementGroupsAsync(connection, transaction, groupKeys, cancellationToken).ConfigureAwait(false);
        var updated = await ExecuteCountAsync(
          connection,
          transaction,
          $"""
          update {this._names.Tasks}
          set state = 'Queued',
              attempt_count = greatest(0, attempt_count - 1),
              not_before_utc = null,
              claimed_by_node_id = null,
              claimed_at_utc = null,
              lease_token = null,
              lease_expires_at_utc = null,
              last_heartbeat_at_utc = null
          where task_id = @task_id
            and state = 'Claimed'
            and claimed_by_node_id = @node_id
            and lease_token = @lease_token
            and lease_expires_at_utc > transaction_timestamp();
          """,
          command =>
          {
              command.Parameters.AddWithValue("task_id", request.TaskId);
              command.Parameters.AddWithValue("node_id", request.NodeId);
              command.Parameters.AddWithValue("lease_token", request.LeaseToken);
          },
          cancellationToken)
          .ConfigureAwait(false);

        await this.NotifyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return updated == 1;
    }

    public async ValueTask<int> RecoverExpiredLeasesAsync(
        RecoverExpiredLeasesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var connection = await this.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var expiredTasks = await this.ReadExpiredClaimsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        foreach (var task in expiredTasks)
        {
            await this.DecrementGroupsAsync(connection, transaction, task.GroupKeys, cancellationToken).ConfigureAwait(false);
            await this.ApplyFailedAttemptAsync(
              connection,
              transaction,
              task,
              new TaskFailureInfo("Sheddueller.LeaseExpired", "The task lease expired before the owning node renewed it.", null),
              cancellationToken)
              .ConfigureAwait(false);
        }

        if (expiredTasks.Count > 0)
        {
            await this.NotifyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return expiredTasks.Count;
    }

    public async ValueTask<bool> CancelAsync(
        CancelTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var connection = await this.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var updated = await ExecuteCountAsync(
          connection,
          transaction,
          $"""
          update {this._names.Tasks}
          set state = 'Canceled',
              canceled_at_utc = transaction_timestamp()
          where task_id = @task_id
            and state = 'Queued';
          """,
          command => command.Parameters.AddWithValue("task_id", request.TaskId),
          cancellationToken)
          .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return updated == 1;
    }

    public async ValueTask SetConcurrencyLimitAsync(
        SetConcurrencyLimitRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var connection = await this.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteCountAsync(
          connection,
          transaction,
          $"""
          insert into {this._names.ConcurrencyGroups} (group_key, configured_limit, in_use_count, updated_at_utc)
          values (@group_key, @configured_limit, 0, transaction_timestamp())
          on conflict (group_key) do update
          set configured_limit = excluded.configured_limit,
              updated_at_utc = excluded.updated_at_utc;
          """,
          command =>
          {
              command.Parameters.AddWithValue("group_key", request.GroupKey);
              command.Parameters.AddWithValue("configured_limit", request.Limit);
          },
          cancellationToken)
          .ConfigureAwait(false);
        await this.NotifyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<int?> GetConfiguredConcurrencyLimitAsync(
        string groupKey,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await this.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"select configured_limit from {this._names.ConcurrencyGroups} where group_key = @group_key;";
        command.Parameters.AddWithValue("group_key", groupKey);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        return result is null or DBNull ? null : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public async ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateRecurringScheduleAsync(
        UpsertRecurringScheduleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var retry = NormalizeRetryPolicy(request.RetryPolicy);
        await using var connection = await this.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var existing = await this.ReadScheduleDefinitionForUpdateAsync(connection, transaction, request.ScheduleKey, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            var nextFireAtUtc = CronSchedule.GetNextOccurrenceAfter(
              request.CronExpression,
              await ReadTransactionTimestampAsync(connection, transaction, cancellationToken).ConfigureAwait(false));
            await this.InsertScheduleAsync(connection, transaction, request, retry, nextFireAtUtc, cancellationToken).ConfigureAwait(false);
            await this.NotifyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return RecurringScheduleUpsertResult.Created;
        }

        if (existing.EqualsRequest(request))
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return RecurringScheduleUpsertResult.Unchanged;
        }

        DateTimeOffset? updatedNextFireAtUtc = existing.IsPaused
          ? null
          : CronSchedule.GetNextOccurrenceAfter(
            request.CronExpression,
            await ReadTransactionTimestampAsync(connection, transaction, cancellationToken).ConfigureAwait(false));
        await this.UpdateScheduleAsync(connection, transaction, request, retry, updatedNextFireAtUtc, cancellationToken).ConfigureAwait(false);
        await this.NotifyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return RecurringScheduleUpsertResult.Updated;
    }

    public async ValueTask<bool> DeleteRecurringScheduleAsync(
        string scheduleKey,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await this.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var updated = await ExecuteCountAsync(
          connection,
          $"""
          delete from {this._names.RecurringSchedules}
          where schedule_key = @schedule_key;
          """,
          command => command.Parameters.AddWithValue("schedule_key", scheduleKey),
          cancellationToken)
          .ConfigureAwait(false);

        return updated == 1;
    }

    public async ValueTask<bool> PauseRecurringScheduleAsync(
        string scheduleKey,
        DateTimeOffset pausedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await this.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var updated = await ExecuteCountAsync(
          connection,
          $"""
          update {this._names.RecurringSchedules}
          set is_paused = true,
              next_fire_at_utc = null,
              updated_at_utc = transaction_timestamp()
          where schedule_key = @schedule_key;
          """,
          command => command.Parameters.AddWithValue("schedule_key", scheduleKey),
          cancellationToken)
          .ConfigureAwait(false);

        return updated == 1;
    }

    public async ValueTask<bool> ResumeRecurringScheduleAsync(
        string scheduleKey,
        DateTimeOffset resumedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await this.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var schedule = await this.ReadScheduleDefinitionForUpdateAsync(connection, transaction, scheduleKey, cancellationToken).ConfigureAwait(false);
        if (schedule is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        var nextFireAtUtc = CronSchedule.GetNextOccurrenceAfter(
          schedule.CronExpression,
          await ReadTransactionTimestampAsync(connection, transaction, cancellationToken).ConfigureAwait(false));
        var updated = await ExecuteCountAsync(
          connection,
          transaction,
          $"""
          update {this._names.RecurringSchedules}
          set is_paused = false,
              next_fire_at_utc = @next_fire_at_utc,
              updated_at_utc = transaction_timestamp()
          where schedule_key = @schedule_key;
          """,
          command =>
          {
              command.Parameters.AddWithValue("schedule_key", scheduleKey);
              command.Parameters.AddWithValue("next_fire_at_utc", nextFireAtUtc);
          },
          cancellationToken)
          .ConfigureAwait(false);

        await this.NotifyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return updated == 1;
    }

    public async ValueTask<RecurringScheduleInfo?> GetRecurringScheduleAsync(
        string scheduleKey,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await this.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var schedules = await this.ReadSchedulesAsync(
          connection,
          $"where schedule.schedule_key = @schedule_key",
          command => command.Parameters.AddWithValue("schedule_key", scheduleKey),
          cancellationToken)
          .ConfigureAwait(false);

        return schedules.SingleOrDefault();
    }

    public async ValueTask<IReadOnlyList<RecurringScheduleInfo>> ListRecurringSchedulesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await this.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await this.ReadSchedulesAsync(connection, string.Empty, _ => { }, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<int> MaterializeDueRecurringSchedulesAsync(
        MaterializeDueRecurringSchedulesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var connection = await this.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var schedules = await this.ReadDueSchedulesAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        var transactionTimestamp = await ReadTransactionTimestampAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        var materialized = 0;

        foreach (var schedule in schedules)
        {
            var canMaterialize = schedule.OverlapMode == RecurringOverlapMode.Allow
              || !await this.HasNonTerminalOccurrenceAsync(connection, transaction, schedule.ScheduleKey, cancellationToken).ConfigureAwait(false);

            if (canMaterialize)
            {
                var retry = NormalizeRetryPolicy(schedule.RetryPolicy ?? request.DefaultRetryPolicy);
                var taskId = Guid.NewGuid();
                await this.InsertMaterializedTaskAsync(connection, transaction, schedule, retry, taskId, transactionTimestamp, cancellationToken).ConfigureAwait(false);
                materialized++;
            }

            var nextFireAtUtc = CronSchedule.GetNextOccurrenceAfter(schedule.CronExpression, transactionTimestamp);
            await ExecuteCountAsync(
              connection,
              transaction,
              $"""
              update {this._names.RecurringSchedules}
              set next_fire_at_utc = @next_fire_at_utc,
                  updated_at_utc = transaction_timestamp()
              where schedule_key = @schedule_key;
              """,
              command =>
              {
                  command.Parameters.AddWithValue("schedule_key", schedule.ScheduleKey);
                  command.Parameters.AddWithValue("next_fire_at_utc", nextFireAtUtc);
              },
              cancellationToken)
              .ConfigureAwait(false);
        }

        if (materialized > 0)
        {
            await this.NotifyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return materialized;
    }

    private async ValueTask<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
      => await this._options.DataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

    private async ValueTask<IReadOnlyList<Guid>> ReadClaimCandidatesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
          $"""
          select task_id
          from {this._names.Tasks}
          where state = 'Queued'
            and (not_before_utc is null or not_before_utc <= transaction_timestamp())
          order by priority desc, enqueue_sequence asc
          for update skip locked
          limit @candidate_limit;
          """;
        command.Parameters.AddWithValue("candidate_limit", ClaimCandidateLimit);

        var taskIds = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            taskIds.Add(reader.GetGuid(0));
        }

        return taskIds;
    }

    private async ValueTask<IReadOnlyList<string>> ReadTaskGroupKeysAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
          $"""
          select group_key
          from {this._names.TaskConcurrencyGroups}
          where task_id = @task_id
          order by group_key asc;
          """;
        command.Parameters.AddWithValue("task_id", taskId);

        var groupKeys = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            groupKeys.Add(reader.GetString(0));
        }

        return groupKeys;
    }

    private async ValueTask EnsureGroupRowsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<string> groupKeys,
        CancellationToken cancellationToken)
    {
        foreach (var groupKey in groupKeys)
        {
            await ExecuteCountAsync(
              connection,
              transaction,
              $"""
              insert into {this._names.ConcurrencyGroups} (group_key, configured_limit, in_use_count, updated_at_utc)
              values (@group_key, null, 0, transaction_timestamp())
              on conflict (group_key) do nothing;
              """,
              command => command.Parameters.AddWithValue("group_key", groupKey),
              cancellationToken)
              .ConfigureAwait(false);
        }
    }

    private async ValueTask<bool> TryReserveGroupsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<string> groupKeys,
        CancellationToken cancellationToken)
    {
        if (groupKeys.Count == 0)
        {
            return true;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
          $"""
          select group_key, configured_limit, in_use_count
          from {this._names.ConcurrencyGroups}
          where group_key = any(@group_keys)
          order by group_key asc
          for update;
          """;
        command.Parameters.AddWithValue("group_keys", groupKeys.ToArray());

        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            var lockCount = 0;
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                lockCount++;
                var configuredLimit = reader.IsDBNull(1) ? 1 : reader.GetInt32(1);
                var inUseCount = reader.GetInt32(2);
                if (inUseCount >= configuredLimit)
                {
                    return false;
                }
            }

            if (lockCount != groupKeys.Count)
            {
                return false;
            }
        }

        await ExecuteCountAsync(
          connection,
          transaction,
          $"""
          update {this._names.ConcurrencyGroups}
          set in_use_count = in_use_count + 1,
              updated_at_utc = transaction_timestamp()
          where group_key = any(@group_keys);
          """,
          updateCommand => updateCommand.Parameters.AddWithValue("group_keys", groupKeys.ToArray()),
          cancellationToken)
          .ConfigureAwait(false);

        return true;
    }

    private async ValueTask<IReadOnlyList<string>?> TryLockCurrentClaimGroupsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid taskId,
        string nodeId,
        Guid leaseToken,
        CancellationToken cancellationToken)
    {
        var task = await this.TryReadCurrentClaimForFailureAsync(connection, transaction, taskId, nodeId, leaseToken, cancellationToken).ConfigureAwait(false);
        return task?.GroupKeys;
    }

    private async ValueTask<PostgresClaimedTask?> TryReadCurrentClaimForFailureAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid taskId,
        string nodeId,
        Guid leaseToken,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
          $"""
          select
              task.task_id,
              task.attempt_count,
              task.max_attempts,
              task.retry_backoff_kind,
              task.retry_base_delay_ms,
              task.retry_max_delay_ms
          from {this._names.Tasks} task
          where task.task_id = @task_id
            and task.state = 'Claimed'
            and task.claimed_by_node_id = @node_id
            and task.lease_token = @lease_token
            and task.lease_expires_at_utc > transaction_timestamp()
          for update;
          """;
        command.Parameters.AddWithValue("task_id", taskId);
        command.Parameters.AddWithValue("node_id", nodeId);
        command.Parameters.AddWithValue("lease_token", leaseToken);

        PostgresClaimedTask? task = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                task = ReadPostgresClaimedTask(reader, []);
            }
        }

        if (task is null)
        {
            return null;
        }

        var groupKeys = await this.ReadTaskGroupKeysAsync(connection, transaction, task.TaskId, cancellationToken).ConfigureAwait(false);
        return task with { GroupKeys = groupKeys };
    }

    private async ValueTask<IReadOnlyList<PostgresClaimedTask>> ReadExpiredClaimsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
          $"""
          select
              task.task_id,
              task.attempt_count,
              task.max_attempts,
              task.retry_backoff_kind,
              task.retry_base_delay_ms,
              task.retry_max_delay_ms
          from {this._names.Tasks} task
          where task.state = 'Claimed'
            and task.lease_expires_at_utc <= transaction_timestamp()
          order by task.lease_expires_at_utc asc, task.enqueue_sequence asc
          for update skip locked;
          """;

        var tasks = new List<PostgresClaimedTask>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                tasks.Add(ReadPostgresClaimedTask(reader, []));
            }
        }

        for (var i = 0; i < tasks.Count; i++)
        {
            var groupKeys = await this.ReadTaskGroupKeysAsync(connection, transaction, tasks[i].TaskId, cancellationToken).ConfigureAwait(false);
            tasks[i] = tasks[i] with { GroupKeys = groupKeys };
        }

        return tasks;
    }

    private async ValueTask DecrementGroupsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<string> groupKeys,
        CancellationToken cancellationToken)
    {
        if (groupKeys.Count == 0)
        {
            return;
        }

        await ExecuteCountAsync(
          connection,
          transaction,
          $"""
          select group_key
          from {this._names.ConcurrencyGroups}
          where group_key = any(@group_keys)
          order by group_key asc
          for update;
          """,
          command => command.Parameters.AddWithValue("group_keys", groupKeys.ToArray()),
          cancellationToken)
          .ConfigureAwait(false);

        await ExecuteCountAsync(
          connection,
          transaction,
          $"""
          update {this._names.ConcurrencyGroups}
          set in_use_count = in_use_count - 1,
              updated_at_utc = transaction_timestamp()
          where group_key = any(@group_keys);
          """,
          command => command.Parameters.AddWithValue("group_keys", groupKeys.ToArray()),
          cancellationToken)
          .ConfigureAwait(false);
    }

    private async ValueTask ApplyFailedAttemptAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PostgresClaimedTask task,
        TaskFailureInfo failure,
        CancellationToken cancellationToken)
    {
        var retriesRemain = task.AttemptCount < task.MaxAttempts;
        var notBeforeExpression = retriesRemain ? "transaction_timestamp() + @retry_delay" : "null";
        await ExecuteCountAsync(
          connection,
          transaction,
          $"""
          update {this._names.Tasks}
          set state = @state,
              failed_at_utc = transaction_timestamp(),
              failure_type_name = @failure_type_name,
              failure_message = @failure_message,
              failure_stack_trace = @failure_stack_trace,
              not_before_utc = {notBeforeExpression},
              claimed_by_node_id = null,
              claimed_at_utc = null,
              lease_token = null,
              lease_expires_at_utc = null,
              last_heartbeat_at_utc = null
          where task_id = @task_id;
          """,
          command =>
          {
              command.Parameters.AddWithValue("task_id", task.TaskId);
              command.Parameters.AddWithValue("state", retriesRemain ? "Queued" : "Failed");
              command.Parameters.AddWithValue("failure_type_name", failure.ExceptionType);
              command.Parameters.AddWithValue("failure_message", failure.Message);
              command.Parameters.AddWithValue("failure_stack_trace", ToDbValue(failure.StackTrace));

              if (retriesRemain)
              {
                  command.Parameters.AddWithValue("retry_delay", CalculateBackoff(task));
              }
          },
          cancellationToken)
          .ConfigureAwait(false);
    }

    private async ValueTask ReplaceTaskGroupsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid taskId,
        IReadOnlyList<string> groupKeys,
        CancellationToken cancellationToken)
    {
        foreach (var groupKey in groupKeys.Distinct(StringComparer.Ordinal))
        {
            await ExecuteCountAsync(
              connection,
              transaction,
              $"""
              insert into {this._names.TaskConcurrencyGroups} (task_id, group_key)
              values (@task_id, @group_key)
              on conflict (task_id, group_key) do nothing;
              """,
              command =>
              {
                  command.Parameters.AddWithValue("task_id", taskId);
                  command.Parameters.AddWithValue("group_key", groupKey);
              },
              cancellationToken)
              .ConfigureAwait(false);
        }
    }

    private async ValueTask<PostgresScheduleDefinition?> ReadScheduleDefinitionForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string scheduleKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
          $"""
          select
              schedule.schedule_key,
              schedule.cron_expression,
              schedule.is_paused,
              schedule.overlap_mode,
              schedule.priority,
              schedule.service_type,
              schedule.method_name,
              schedule.method_parameter_types,
              schedule.serialized_arguments_content_type,
              schedule.serialized_arguments,
              schedule.retry_policy_configured,
              schedule.max_attempts,
              schedule.retry_backoff_kind,
              schedule.retry_base_delay_ms,
              schedule.retry_max_delay_ms,
              schedule.next_fire_at_utc
          from {this._names.RecurringSchedules} schedule
          where schedule.schedule_key = @schedule_key
          for update;
          """;
        command.Parameters.AddWithValue("schedule_key", scheduleKey);

        PostgresScheduleDefinition? schedule = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                schedule = ReadScheduleDefinition(reader, []);
            }
        }

        if (schedule is null)
        {
            return null;
        }

        var groupKeys = await this.ReadScheduleGroupKeysAsync(connection, transaction, schedule.ScheduleKey, cancellationToken).ConfigureAwait(false);
        return schedule with { ConcurrencyGroupKeys = groupKeys };
    }

    private async ValueTask InsertScheduleAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        UpsertRecurringScheduleRequest request,
        PostgresRetryPolicy retry,
        DateTimeOffset nextFireAtUtc,
        CancellationToken cancellationToken)
    {
        await ExecuteCountAsync(
          connection,
          transaction,
          $"""
          insert into {this._names.RecurringSchedules} (
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
              next_fire_at_utc,
              created_at_utc,
              updated_at_utc)
          values (
              @schedule_key,
              @cron_expression,
              false,
              @overlap_mode,
              @priority,
              @service_type,
              @method_name,
              @method_parameter_types,
              @serialized_arguments_content_type,
              @serialized_arguments,
              @retry_policy_configured,
              @max_attempts,
              @retry_backoff_kind,
              @retry_base_delay_ms,
              @retry_max_delay_ms,
              @next_fire_at_utc,
              transaction_timestamp(),
              transaction_timestamp());
          """,
          command => AddScheduleParameters(command, request, retry, nextFireAtUtc),
          cancellationToken)
          .ConfigureAwait(false);
        await this.ReplaceScheduleGroupsAsync(connection, transaction, request.ScheduleKey, request.ConcurrencyGroupKeys, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask UpdateScheduleAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        UpsertRecurringScheduleRequest request,
        PostgresRetryPolicy retry,
        DateTimeOffset? nextFireAtUtc,
        CancellationToken cancellationToken)
    {
        await ExecuteCountAsync(
          connection,
          transaction,
          $"""
          update {this._names.RecurringSchedules}
          set cron_expression = @cron_expression,
              overlap_mode = @overlap_mode,
              priority = @priority,
              service_type = @service_type,
              method_name = @method_name,
              method_parameter_types = @method_parameter_types,
              serialized_arguments_content_type = @serialized_arguments_content_type,
              serialized_arguments = @serialized_arguments,
              retry_policy_configured = @retry_policy_configured,
              max_attempts = @max_attempts,
              retry_backoff_kind = @retry_backoff_kind,
              retry_base_delay_ms = @retry_base_delay_ms,
              retry_max_delay_ms = @retry_max_delay_ms,
              next_fire_at_utc = @next_fire_at_utc,
              updated_at_utc = transaction_timestamp()
          where schedule_key = @schedule_key;
          """,
          command => AddScheduleParameters(command, request, retry, nextFireAtUtc),
          cancellationToken)
          .ConfigureAwait(false);
        await ExecuteCountAsync(
          connection,
          transaction,
          $"delete from {this._names.ScheduleConcurrencyGroups} where schedule_key = @schedule_key;",
          command => command.Parameters.AddWithValue("schedule_key", request.ScheduleKey),
          cancellationToken)
          .ConfigureAwait(false);
        await this.ReplaceScheduleGroupsAsync(connection, transaction, request.ScheduleKey, request.ConcurrencyGroupKeys, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask ReplaceScheduleGroupsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string scheduleKey,
        IReadOnlyList<string> groupKeys,
        CancellationToken cancellationToken)
    {
        foreach (var groupKey in groupKeys.Distinct(StringComparer.Ordinal))
        {
            await ExecuteCountAsync(
              connection,
              transaction,
              $"""
              insert into {this._names.ScheduleConcurrencyGroups} (schedule_key, group_key)
              values (@schedule_key, @group_key)
              on conflict (schedule_key, group_key) do nothing;
              """,
              command =>
              {
                  command.Parameters.AddWithValue("schedule_key", scheduleKey);
                  command.Parameters.AddWithValue("group_key", groupKey);
              },
              cancellationToken)
              .ConfigureAwait(false);
        }
    }

    private async ValueTask<IReadOnlyList<string>> ReadScheduleGroupKeysAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string scheduleKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
          $"""
          select group_key
          from {this._names.ScheduleConcurrencyGroups}
          where schedule_key = @schedule_key
          order by group_key asc;
          """;
        command.Parameters.AddWithValue("schedule_key", scheduleKey);

        var groupKeys = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            groupKeys.Add(reader.GetString(0));
        }

        return groupKeys;
    }

    private async ValueTask<IReadOnlyList<RecurringScheduleInfo>> ReadSchedulesAsync(
        NpgsqlConnection connection,
        string whereClause,
        Action<NpgsqlCommand> configure,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
          $"""
          select
              schedule.schedule_key,
              schedule.cron_expression,
              schedule.is_paused,
              schedule.overlap_mode,
              schedule.priority,
              schedule.retry_policy_configured,
              schedule.max_attempts,
              schedule.retry_backoff_kind,
              schedule.retry_base_delay_ms,
              schedule.retry_max_delay_ms,
              schedule.next_fire_at_utc,
              coalesce(array_agg(schedule_group.group_key order by schedule_group.group_key) filter (where schedule_group.group_key is not null), array[]::text[]) as group_keys
          from {this._names.RecurringSchedules} schedule
          left join {this._names.ScheduleConcurrencyGroups} schedule_group on schedule_group.schedule_key = schedule.schedule_key
          {whereClause}
          group by schedule.schedule_key
          order by schedule.schedule_key asc;
          """;
        configure(command);

        var schedules = new List<RecurringScheduleInfo>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            schedules.Add(new RecurringScheduleInfo(
              reader.GetString(0),
              reader.GetString(1),
              reader.GetBoolean(2),
              PostgresConversion.ToRecurringOverlapMode(reader.GetValue(3)),
              reader.GetInt32(4),
              reader.GetFieldValue<string[]>(11),
              PostgresConversion.ToRetryPolicy(
                reader.GetBoolean(5),
                reader.GetInt32(6),
                reader.GetValue(7),
                reader.GetValue(8),
                reader.GetValue(9)),
              reader.IsDBNull(10) ? null : PostgresConversion.ToDateTimeOffset(reader.GetValue(10))));
        }

        return schedules;
    }

    private async ValueTask<IReadOnlyList<PostgresScheduleDefinition>> ReadDueSchedulesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
          $"""
          select
              schedule.schedule_key,
              schedule.cron_expression,
              schedule.is_paused,
              schedule.overlap_mode,
              schedule.priority,
              schedule.service_type,
              schedule.method_name,
              schedule.method_parameter_types,
              schedule.serialized_arguments_content_type,
              schedule.serialized_arguments,
              schedule.retry_policy_configured,
              schedule.max_attempts,
              schedule.retry_backoff_kind,
              schedule.retry_base_delay_ms,
              schedule.retry_max_delay_ms,
              schedule.next_fire_at_utc
          from {this._names.RecurringSchedules} schedule
          where schedule.is_paused = false
            and schedule.next_fire_at_utc <= transaction_timestamp()
          order by schedule.next_fire_at_utc asc, schedule.schedule_key asc
          for update skip locked;
          """;

        var schedules = new List<PostgresScheduleDefinition>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                schedules.Add(ReadScheduleDefinition(reader, []));
            }
        }

        for (var i = 0; i < schedules.Count; i++)
        {
            var groupKeys = await this.ReadScheduleGroupKeysAsync(connection, transaction, schedules[i].ScheduleKey, cancellationToken).ConfigureAwait(false);
            schedules[i] = schedules[i] with { ConcurrencyGroupKeys = groupKeys };
        }

        return schedules;
    }

    private async ValueTask<bool> HasNonTerminalOccurrenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string scheduleKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
          $"""
          select exists (
              select 1
              from {this._names.Tasks}
              where source_schedule_key = @schedule_key
                and state in ('Queued', 'Claimed'));
          """;
        command.Parameters.AddWithValue("schedule_key", scheduleKey);

        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? false);
    }

    private async ValueTask InsertMaterializedTaskAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PostgresScheduleDefinition schedule,
        PostgresRetryPolicy retry,
        Guid taskId,
        DateTimeOffset materializedAtUtc,
        CancellationToken cancellationToken)
    {
        await ExecuteCountAsync(
          connection,
          transaction,
          $"""
          insert into {this._names.Tasks} (
              task_id,
              state,
              priority,
              enqueued_at_utc,
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
              source_schedule_key,
              scheduled_fire_at_utc)
          values (
              @task_id,
              'Queued',
              @priority,
              transaction_timestamp(),
              @service_type,
              @method_name,
              @method_parameter_types,
              @serialized_arguments_content_type,
              @serialized_arguments,
              0,
              @max_attempts,
              @retry_backoff_kind,
              @retry_base_delay_ms,
              @retry_max_delay_ms,
              @source_schedule_key,
              @scheduled_fire_at_utc);
          """,
          command =>
          {
              command.Parameters.AddWithValue("task_id", taskId);
              command.Parameters.AddWithValue("priority", schedule.Priority);
              command.Parameters.AddWithValue("service_type", schedule.ServiceType);
              command.Parameters.AddWithValue("method_name", schedule.MethodName);
              command.Parameters.AddWithValue("method_parameter_types", schedule.MethodParameterTypes.ToArray());
              command.Parameters.AddWithValue("serialized_arguments_content_type", schedule.SerializedArguments.ContentType);
              command.Parameters.AddWithValue("serialized_arguments", schedule.SerializedArguments.Data);
              command.Parameters.AddWithValue("max_attempts", retry.MaxAttempts);
              command.Parameters.AddWithValue("retry_backoff_kind", ToDbValue(PostgresConversion.ToText(retry.BackoffKind)));
              command.Parameters.AddWithValue("retry_base_delay_ms", ToDbValue(PostgresConversion.ToMilliseconds(retry.BaseDelay)));
              command.Parameters.AddWithValue("retry_max_delay_ms", ToDbValue(PostgresConversion.ToMilliseconds(retry.MaxDelay)));
              command.Parameters.AddWithValue("source_schedule_key", schedule.ScheduleKey);
              command.Parameters.AddWithValue("scheduled_fire_at_utc", schedule.NextFireAtUtc ?? materializedAtUtc);
          },
          cancellationToken)
          .ConfigureAwait(false);
        await this.ReplaceTaskGroupsAsync(connection, transaction, taskId, schedule.ConcurrencyGroupKeys, cancellationToken).ConfigureAwait(false);
    }

    private static ClaimedTask ReadClaimedTask(NpgsqlDataReader reader, IReadOnlyList<string> groupKeys)
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

    private static PostgresClaimedTask ReadPostgresClaimedTask(NpgsqlDataReader reader, IReadOnlyList<string> groupKeys)
      => new(
        reader.GetGuid(0),
        reader.GetInt32(1),
        reader.GetInt32(2),
        PostgresConversion.ToRetryBackoffKind(reader.GetValue(3)),
        PostgresConversion.FromMilliseconds(reader.GetValue(4)),
        PostgresConversion.FromMilliseconds(reader.GetValue(5)),
        groupKeys);

    private static PostgresScheduleDefinition ReadScheduleDefinition(NpgsqlDataReader reader, IReadOnlyList<string> groupKeys)
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

    private static PostgresRetryPolicy NormalizeRetryPolicy(RetryPolicy? retryPolicy)
    {
        if (retryPolicy is not { MaxAttempts: > 1 })
        {
            return new PostgresRetryPolicy(false, 1, null, null, null);
        }

        return new PostgresRetryPolicy(true, retryPolicy.MaxAttempts, retryPolicy.BackoffKind, retryPolicy.BaseDelay, retryPolicy.MaxDelay);
    }

    private static TimeSpan CalculateBackoff(PostgresClaimedTask task)
    {
        if (task.RetryBackoffKind is null || task.RetryBaseDelay is null)
        {
            return TimeSpan.Zero;
        }

        if (task.RetryBackoffKind == RetryBackoffKind.Fixed)
        {
            return task.RetryBaseDelay.Value;
        }

        var multiplier = Math.Pow(2, task.AttemptCount - 1);
        var ticks = task.RetryBaseDelay.Value.Ticks * multiplier;
        var delay = TimeSpan.FromTicks((long)Math.Min(TimeSpan.MaxValue.Ticks, ticks));

        return task.RetryMaxDelay is { } maxDelay && delay > maxDelay ? maxDelay : delay;
    }

    private static void AddScheduleParameters(
        NpgsqlCommand command,
        UpsertRecurringScheduleRequest request,
        PostgresRetryPolicy retry,
        DateTimeOffset? nextFireAtUtc)
    {
        command.Parameters.AddWithValue("schedule_key", request.ScheduleKey);
        command.Parameters.AddWithValue("cron_expression", request.CronExpression);
        command.Parameters.AddWithValue("overlap_mode", PostgresConversion.ToText(request.OverlapMode));
        command.Parameters.AddWithValue("priority", request.Priority);
        command.Parameters.AddWithValue("service_type", request.ServiceType);
        command.Parameters.AddWithValue("method_name", request.MethodName);
        command.Parameters.AddWithValue("method_parameter_types", request.MethodParameterTypes.ToArray());
        command.Parameters.AddWithValue("serialized_arguments_content_type", request.SerializedArguments.ContentType);
        command.Parameters.AddWithValue("serialized_arguments", request.SerializedArguments.Data);
        command.Parameters.AddWithValue("retry_policy_configured", retry.IsConfigured);
        command.Parameters.AddWithValue("max_attempts", retry.MaxAttempts);
        command.Parameters.AddWithValue("retry_backoff_kind", ToDbValue(PostgresConversion.ToText(retry.BackoffKind)));
        command.Parameters.AddWithValue("retry_base_delay_ms", ToDbValue(PostgresConversion.ToMilliseconds(retry.BaseDelay)));
        command.Parameters.AddWithValue("retry_max_delay_ms", ToDbValue(PostgresConversion.ToMilliseconds(retry.MaxDelay)));
        command.Parameters.AddWithValue("next_fire_at_utc", ToDbValue(nextFireAtUtc));
    }

    private static async ValueTask<DateTimeOffset> ReadTransactionTimestampAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select transaction_timestamp();";
        return PostgresConversion.ToDateTimeOffset(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
          ?? throw new InvalidOperationException("PostgreSQL did not return transaction_timestamp()."));
    }

    private static async ValueTask<int> ExecuteCountAsync(
        NpgsqlConnection connection,
        string commandText,
        Action<NpgsqlCommand> configure,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        configure(command);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<int> ExecuteCountAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string commandText,
        Action<NpgsqlCommand> configure,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        configure(command);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask NotifyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
      => await ExecuteCountAsync(
        connection,
        transaction,
        "select pg_notify(@channel, @payload);",
        command =>
        {
            command.Parameters.AddWithValue("channel", PostgresNames.WakeupChannel);
            command.Parameters.AddWithValue("payload", this._options.SchemaName);
        },
        cancellationToken)
      .ConfigureAwait(false);

    private static object ToDbValue(object? value)
      => value ?? DBNull.Value;

    private sealed record PostgresClaimedTask(
        Guid TaskId,
        int AttemptCount,
        int MaxAttempts,
        RetryBackoffKind? RetryBackoffKind,
        TimeSpan? RetryBaseDelay,
        TimeSpan? RetryMaxDelay,
        IReadOnlyList<string> GroupKeys);

    private sealed record PostgresRetryPolicy(
        bool IsConfigured,
        int MaxAttempts,
        RetryBackoffKind? BackoffKind,
        TimeSpan? BaseDelay,
        TimeSpan? MaxDelay);

    private sealed record PostgresScheduleDefinition(
        string ScheduleKey,
        string CronExpression,
        bool IsPaused,
        RecurringOverlapMode OverlapMode,
        int Priority,
        string ServiceType,
        string MethodName,
        IReadOnlyList<string> MethodParameterTypes,
        SerializedTaskPayload SerializedArguments,
        IReadOnlyList<string> ConcurrencyGroupKeys,
        RetryPolicy? RetryPolicy,
        DateTimeOffset? NextFireAtUtc)
    {
        public bool EqualsRequest(UpsertRecurringScheduleRequest request)
          => string.Equals(this.CronExpression, request.CronExpression, StringComparison.Ordinal)
            && this.OverlapMode == request.OverlapMode
            && this.Priority == request.Priority
            && string.Equals(this.ServiceType, request.ServiceType, StringComparison.Ordinal)
            && string.Equals(this.MethodName, request.MethodName, StringComparison.Ordinal)
            && this.MethodParameterTypes.SequenceEqual(request.MethodParameterTypes, StringComparer.Ordinal)
            && string.Equals(this.SerializedArguments.ContentType, request.SerializedArguments.ContentType, StringComparison.Ordinal)
            && this.SerializedArguments.Data.SequenceEqual(request.SerializedArguments.Data)
            && this.ConcurrencyGroupKeys.SequenceEqual(request.ConcurrencyGroupKeys, StringComparer.Ordinal)
            && this.RetryPolicy == request.RetryPolicy;
    }
}
