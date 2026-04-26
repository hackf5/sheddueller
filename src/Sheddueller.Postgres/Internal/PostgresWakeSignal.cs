// spell-checker: ignore wakeup

namespace Sheddueller.Postgres.Internal;

using Microsoft.Extensions.Logging;

using Npgsql;

using Sheddueller.Runtime;

internal sealed class PostgresWakeSignal(
    ShedduellerPostgresOptions options,
    ILogger<PostgresWakeSignal> logger) : IShedduellerWakeSignal, IDisposable
{
    private static readonly TimeSpan ListenerRetryDelay = TimeSpan.FromSeconds(1);

    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _disposeTokenSource = new();
    private readonly Lock _listenerLock = new();
    private readonly ShedduellerPostgresOptions _options = options;
    private bool _listenerStarted;
    private int _signaled;

    public void Notify()
      => this.Signal();

    public async ValueTask WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        this.EnsureListening();
        if (await this._signal.WaitAsync(timeout, cancellationToken).ConfigureAwait(false))
        {
            Volatile.Write(ref this._signaled, 0);
        }
    }

    public void Dispose()
    {
        this._disposeTokenSource.Cancel();
        this._signal.Dispose();
        this._disposeTokenSource.Dispose();
    }

    private void EnsureListening()
    {
        lock (this._listenerLock)
        {
            if (this._listenerStarted)
            {
                return;
            }

            this._listenerStarted = true;
            _ = Task.Run(this.ListenAsync);
        }
    }

    private async Task ListenAsync()
    {
        while (!this._disposeTokenSource.IsCancellationRequested)
        {
            try
            {
                await this.ListenUntilDisconnectedAsync(this._disposeTokenSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (this._disposeTokenSource.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.PostgresWakeListenerRetrying(exception, this._options.SchemaName, (long)ListenerRetryDelay.TotalMilliseconds);
                await Task.Delay(ListenerRetryDelay, this._disposeTokenSource.Token).ConfigureAwait(false);
            }
        }
    }

    private async Task ListenUntilDisconnectedAsync(CancellationToken cancellationToken)
    {
        await using var connection = await this._options.DataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        connection.Notification += this.OnNotification;

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"listen {PostgresNames.WakeupChannel};";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            logger.PostgresWakeListenerStarted(this._options.SchemaName);

            while (!cancellationToken.IsCancellationRequested)
            {
                await connection.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            connection.Notification -= this.OnNotification;
        }
    }

    private void OnNotification(object sender, NpgsqlNotificationEventArgs args)
    {
        if (string.Equals(args.Payload, this._options.SchemaName, StringComparison.Ordinal))
        {
            this.Signal();
        }
    }

    private void Signal()
    {
        if (Interlocked.Exchange(ref this._signaled, 1) == 0)
        {
            try
            {
                this._signal.Release();
            }
            catch (ObjectDisposedException)
            {
                // Disposal can race with PostgreSQL notification callbacks during host shutdown.
            }
        }
    }
}
