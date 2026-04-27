namespace Sheddueller.SampleHost.DemoJobs;

using System.Globalization;

using Microsoft.Extensions.Logging;

public sealed class DemoJobService(
    DemoJobState state,
    ILogger<DemoJobService> logger)
{
    private readonly DemoJobState _state = state;
    private readonly ILogger<DemoJobService> _logger = logger;

    public Task RunQuickAsync(string label, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public async Task RunProgressAsync(
        string label,
        IProgress<decimal> progress,
        CancellationToken cancellationToken)
    {
        for (var step = 0; step <= 4; step++)
        {
            var percent = step * 25;
            ProgressStep(this._logger, label, step + 1, null);
            progress.Report(percent);

            if (step < 4)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public Task RunRetryUntilSuccessAsync(
        string runKey,
        int failuresBeforeSuccess,
        IProgress<decimal> progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var attempt = this._state.IncrementAttemptCount(runKey);
        var message = $"retry demo {runKey} attempt {attempt}";
        RetryAttempt(this._logger, runKey, attempt, null);

        if (attempt <= failuresBeforeSuccess)
        {
            throw new InvalidOperationException($"{message} failed on purpose.");
        }

        progress.Report(100);
        return Task.CompletedTask;
    }

    public Task RunAlwaysFailAsync(string label, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PermanentFailure(this._logger, label, null);
        throw new InvalidOperationException($"{label} failed on purpose.");
    }

    public async Task RunGroupHoldAsync(
        string label,
        IProgress<decimal> progress,
        CancellationToken cancellationToken)
    {
        GroupSlotClaimed(this._logger, label, null);

        for (var step = 1; step <= 6; step++)
        {
            var percent = step * (100m / 6m);
            GroupSlotHeld(this._logger, label, step, null);
            progress.Report(percent);
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RunIdempotentDemoAsync(
        string label,
        IProgress<decimal> progress,
        CancellationToken cancellationToken)
    {
        IdempotentRunStarted(this._logger, label, null);

        for (var step = 1; step <= 10; step++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            var percent = step * 10;
            IdempotentRunStep(this._logger, label, step, null);
            progress.Report(percent);
        }
    }

    public Task RunRecurringAsync(
        IProgress<decimal> progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var firedAtUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        RecurringFired(this._logger, firedAtUtc, null);
        progress.Report(100);
        return Task.CompletedTask;
    }

    private static readonly Action<ILogger, string, int, Exception?> ProgressStep =
      LoggerMessage.Define<string, int>(
        LogLevel.Information,
        new EventId(100, nameof(ProgressStep)),
        "{Label} step {Step}/5");

    private static readonly Action<ILogger, string, int, Exception?> RetryAttempt =
      LoggerMessage.Define<string, int>(
        LogLevel.Information,
        new EventId(101, nameof(RetryAttempt)),
        "retry demo {RunKey} attempt {Attempt}");

    private static readonly Action<ILogger, string, Exception?> PermanentFailure =
      LoggerMessage.Define<string>(
        LogLevel.Error,
        new EventId(102, nameof(PermanentFailure)),
        "{Label} is about to fail permanently.");

    private static readonly Action<ILogger, string, Exception?> GroupSlotClaimed =
      LoggerMessage.Define<string>(
        LogLevel.Information,
        new EventId(103, nameof(GroupSlotClaimed)),
        "{Label} claimed the demo concurrency slot.");

    private static readonly Action<ILogger, string, int, Exception?> GroupSlotHeld =
      LoggerMessage.Define<string, int>(
        LogLevel.Information,
        new EventId(104, nameof(GroupSlotHeld)),
        "{Label} holding slot {Step}/6");

    private static readonly Action<ILogger, string, Exception?> IdempotentRunStarted =
      LoggerMessage.Define<string>(
        LogLevel.Information,
        new EventId(105, nameof(IdempotentRunStarted)),
        "{Label} started its 10-second idempotency demo run.");

    private static readonly Action<ILogger, string, int, Exception?> IdempotentRunStep =
      LoggerMessage.Define<string, int>(
        LogLevel.Information,
        new EventId(106, nameof(IdempotentRunStep)),
        "{Label} idempotent run {Step}/10");

    private static readonly Action<ILogger, string, Exception?> RecurringFired =
      LoggerMessage.Define<string>(
        LogLevel.Information,
        new EventId(107, nameof(RecurringFired)),
        "Recurring demo fired at {FiredAtUtc}");
}
