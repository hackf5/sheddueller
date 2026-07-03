namespace Sheddueller.Postgres.Internal.Operations;

using Sheddueller.Storage;

internal static class ClearConcurrencyLimitOverrideOperation
{
    public static async ValueTask ExecuteAsync(
        PostgresOperationContext context,
        ClearConcurrencyLimitOverrideRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await context.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await PostgresOperationContext.ExecuteCountAsync(
          connection,
          transaction,
          $"""
          update {context.Names.ConcurrencyGroups}
          set configured_limit = null,
              updated_at_utc = transaction_timestamp()
          where group_key = @group_key;
          """,
          command => command.Parameters.AddWithValue("group_key", request.GroupKey),
          cancellationToken)
          .ConfigureAwait(false);
        await context.NotifyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
