// spell-checker: ignore dashboard

namespace Sheddueller.Postgres.Internal;

using System.Globalization;

using Microsoft.Extensions.Hosting;

using Npgsql;

using Sheddueller.Dashboard;
using Sheddueller.Postgres.Internal.Operations;

internal sealed class PostgresDashboardEventListener(
    ShedduellerPostgresOptions options,
    IDashboardLiveUpdatePublisher publisher) : BackgroundService
{
    private readonly PostgresOperationContext _context = new(options);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await this.ListenUntilDisconnectedAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
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
            command.CommandText = $"listen {PostgresNames.DashboardEventChannel};";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

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
        if (!TryParsePayload(args.Payload, options.SchemaName, out var taskId, out var eventSequence))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            var jobEvent = await PostgresDashboardEvents.ReadEventAsync(this._context, taskId, eventSequence, CancellationToken.None)
              .ConfigureAwait(false);
            if (jobEvent is not null)
            {
                await publisher.PublishAsync(jobEvent, CancellationToken.None).ConfigureAwait(false);
            }
        });
    }

    private static bool TryParsePayload(
        string payload,
        string schemaName,
        out Guid taskId,
        out long eventSequence)
    {
        taskId = default;
        eventSequence = default;
        var parts = payload.Split('|', StringSplitOptions.None);
        return parts.Length == 3
          && string.Equals(parts[0], schemaName, StringComparison.Ordinal)
          && Guid.TryParseExact(parts[1], "N", out taskId)
          && long.TryParse(parts[2], CultureInfo.InvariantCulture, out eventSequence);
    }
}
