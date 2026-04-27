namespace Sheddueller.Worker.Internal;

using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Sheddueller.Storage;

internal sealed class ShedduellerJobLogEventDispatcher(
    ShedduellerJobLogEventQueue queue,
    IJobEventSink eventSink,
    ILogger<ShedduellerJobLogEventDispatcher> logger) : BackgroundService
{
    private const int MaxBatchSize = 64;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(250);

    private readonly ShedduellerJobLogEventQueue _queue = queue;
    private readonly IJobEventSink _eventSink = eventSink;
    private readonly ILogger<ShedduellerJobLogEventDispatcher> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<AppendJobEventRequest>(MaxBatchSize);

        try
        {
            while (await this._queue.Reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
            {
                this.DrainAvailable(batch);
                if (batch.Count < MaxBatchSize)
                {
                    await Task.Delay(FlushInterval, stoppingToken).ConfigureAwait(false);
                    this.DrainAvailable(batch);
                }

                await this.FlushAsync(batch).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown drains any queued log events below.
        }
        finally
        {
            this.DrainAvailable(batch, int.MaxValue);
            await this.FlushAsync(batch).ConfigureAwait(false);
        }
    }

    private void DrainAvailable(List<AppendJobEventRequest> batch, int maxBatchSize = MaxBatchSize)
    {
        while (batch.Count < maxBatchSize && this._queue.Reader.TryRead(out var request))
        {
            batch.Add(request);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Captured job logging is best-effort and must not fail the dispatcher.")]
    private async ValueTask FlushAsync(List<AppendJobEventRequest> batch)
    {
        try
        {
            foreach (var request in batch)
            {
                try
                {
                    await this._eventSink.AppendAsync(request).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    this._logger.JobEventAppendFailed(exception, request.JobId);
                }
            }
        }
        finally
        {
            batch.Clear();
        }
    }
}
