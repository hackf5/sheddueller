namespace Sheddueller.Postgres.Internal.Operations;

using Sheddueller.Storage;

internal static class SetConcurrencyLimitOperation
{
    public static async ValueTask ExecuteAsync(
        PostgresOperationContext context,
        SetConcurrencyLimitRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await PostgresOperationContext.ExecuteCountAsync(
          connection,
          transaction,
          $"""
          insert into {context.Names.ConcurrencyGroups} (group_key, configured_limit, in_use_count, updated_at_utc)
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
        await context.NotifyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
