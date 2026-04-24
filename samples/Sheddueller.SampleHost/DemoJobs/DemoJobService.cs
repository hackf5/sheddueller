namespace Sheddueller.SampleHost.DemoJobs;

using System.Globalization;

public sealed class DemoJobService(DemoJobState state)
{
    private readonly DemoJobState _state = state;

    public Task RunQuickAsync(string label, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public async Task RunProgressAsync(string label, IJobContext jobContext, CancellationToken cancellationToken)
    {
        for (var step = 0; step <= 4; step++)
        {
            var percent = step * 25;
            var message = $"{label} step {step + 1}/5";
            await jobContext.LogAsync(JobLogLevel.Information, message, cancellationToken: cancellationToken).ConfigureAwait(false);
            await jobContext.ReportProgressAsync(percent, message, cancellationToken).ConfigureAwait(false);

            if (step < 4)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task RunRetryUntilSuccessAsync(
        string runKey,
        int failuresBeforeSuccess,
        IJobContext jobContext,
        CancellationToken cancellationToken)
    {
        var attempt = this._state.IncrementAttemptCount(runKey);
        var message = $"retry demo {runKey} attempt {attempt}";
        await jobContext.LogAsync(JobLogLevel.Information, message, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (attempt <= failuresBeforeSuccess)
        {
            throw new InvalidOperationException($"{message} failed on purpose.");
        }

        await jobContext.ReportProgressAsync(100, $"{message} succeeded", cancellationToken).ConfigureAwait(false);
    }

    public async Task RunAlwaysFailAsync(string label, IJobContext jobContext, CancellationToken cancellationToken)
    {
        await jobContext.LogAsync(JobLogLevel.Error, $"{label} is about to fail permanently.", cancellationToken: cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException($"{label} failed on purpose.");
    }

    public async Task RunGroupHoldAsync(string label, IJobContext jobContext, CancellationToken cancellationToken)
    {
        await jobContext.LogAsync(JobLogLevel.Information, $"{label} claimed the demo concurrency slot.", cancellationToken: cancellationToken).ConfigureAwait(false);

        for (var step = 1; step <= 6; step++)
        {
            var percent = step * (100d / 6d);
            await jobContext.ReportProgressAsync(percent, $"{label} holding slot {step}/6", cancellationToken).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RunIdempotentDemoAsync(string label, IJobContext jobContext, CancellationToken cancellationToken)
    {
        await jobContext.LogAsync(JobLogLevel.Information, $"{label} started its 10-second idempotency demo run.", cancellationToken: cancellationToken)
          .ConfigureAwait(false);

        for (var step = 1; step <= 10; step++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            var percent = step * 10;
            await jobContext.ReportProgressAsync(percent, $"{label} idempotent run {step}/10", cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RunRecurringAsync(IJobContext jobContext, CancellationToken cancellationToken)
    {
        var message = string.Create(
          CultureInfo.InvariantCulture,
          $"Recurring demo fired at {DateTimeOffset.UtcNow:O}");
        await jobContext.LogAsync(JobLogLevel.Information, message, cancellationToken: cancellationToken).ConfigureAwait(false);
        await jobContext.ReportProgressAsync(100, "Recurring occurrence completed", cancellationToken).ConfigureAwait(false);
    }
}
