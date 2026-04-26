namespace Sheddueller.Postgres.Internal;

using System.Globalization;

using Microsoft.Extensions.Logging;

using Npgsql;

using Sheddueller.Postgres.Internal.Operations;
using Sheddueller.Runtime;
using Sheddueller.Storage;

internal sealed class PostgresJobEventListener(
    ShedduellerPostgresOptions options,
    IJobEventNotifier publisher,
    ILogger<PostgresJobEventListener> logger) : IShedduellerJobEventListener
{
    private static readonly TimeSpan ListenerRetryDelay = TimeSpan.FromSeconds(1);

    private readonly PostgresOperationContext _context = new(options);

    public async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await this.ListenUntilDisconnectedAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.PostgresJobEventListenerRetrying(exception, options.SchemaName, (long)ListenerRetryDelay.TotalMilliseconds);
                await Task.Delay(ListenerRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ListenUntilDisconnectedAsync(CancellationToken cancellationToken)
    {
        await using var connection = await options.DataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        connection.Notification += this.OnNotification;

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"listen {PostgresNames.JobEventChannel};";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            logger.PostgresJobEventListenerStarted(options.SchemaName);

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
      => this.HandleNotificationPayload(args.Payload);

    internal void HandleNotificationPayload(string payload)
    {
        if (!TryParsePayload(payload, options.SchemaName, out var jobId, out var eventSequence))
        {
            logger.PostgresJobEventNotificationPayloadInvalid(options.SchemaName);
            return;
        }

        _ = Task.Run(() => this.PublishNotificationAsync(jobId, eventSequence));
    }

    private async Task PublishNotificationAsync(Guid jobId, long eventSequence)
    {
        try
        {
            var jobEvent = await PostgresJobEvents.ReadEventAsync(this._context, jobId, eventSequence, CancellationToken.None)
              .ConfigureAwait(false);
            if (jobEvent is null)
            {
                logger.PostgresJobEventNotificationMissing(jobId, eventSequence);
                return;
            }

            await publisher.NotifyAsync(jobEvent, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.PostgresJobEventNotificationFailed(exception, jobId, eventSequence);
        }
    }

    private static bool TryParsePayload(
        string payload,
        string schemaName,
        out Guid jobId,
        out long eventSequence)
    {
        jobId = default;
        eventSequence = default;
        var parts = payload.Split('|', StringSplitOptions.None);
        return parts.Length == 3
          && string.Equals(parts[0], schemaName, StringComparison.Ordinal)
          && Guid.TryParseExact(parts[1], "N", out jobId)
          && long.TryParse(parts[2], CultureInfo.InvariantCulture, out eventSequence);
    }
}
