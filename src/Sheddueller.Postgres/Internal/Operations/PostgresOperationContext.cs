namespace Sheddueller.Postgres.Internal.Operations;

using Npgsql;

internal sealed class PostgresOperationContext(ShedduellerPostgresOptions options)
{
    public ShedduellerPostgresOptions Options { get; } = options;

    public PostgresNames Names { get; } = new(options.SchemaName);

    public ValueTask<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
      => this.Options.DataSource.OpenConnectionAsync(cancellationToken);

    public async ValueTask NotifyAsync(
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
            command.Parameters.AddWithValue("payload", this.Options.SchemaName);
        },
        cancellationToken)
      .ConfigureAwait(false);

    public static object ToDbValue(object? value)
      => value ?? DBNull.Value;

    public static async ValueTask<DateTimeOffset> ReadTransactionTimestampAsync(
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

    public static async ValueTask<int> ExecuteCountAsync(
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

    public static async ValueTask<int> ExecuteCountAsync(
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
}
