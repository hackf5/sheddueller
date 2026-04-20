namespace Sheddueller.Runtime;

using System.Diagnostics.CodeAnalysis;

using Sheddueller.Dashboard;

internal sealed class JobContext(
    Guid jobId,
    int attemptNumber,
    IDashboardEventSink eventSink,
    CancellationToken cancellationToken) : IJobContext
{
    public Guid JobId { get; } = jobId;

    public int AttemptNumber { get; } = attemptNumber;

    public CancellationToken CancellationToken { get; } = cancellationToken;

    public async ValueTask LogAsync(
        JobLogLevel level,
        string message,
        IReadOnlyDictionary<string, string>? fields = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ValidateFields(fields);

        await this.AppendBestEffortAsync(
          new AppendDashboardJobEventRequest(this.JobId, DashboardJobEventKind.Log, this.AttemptNumber, level, message, Fields: fields),
          cancellationToken)
          .ConfigureAwait(false);
    }

    public async ValueTask ReportProgressAsync(
        double? percent,
        string? message = null,
        CancellationToken cancellationToken = default)
    {
        if (percent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percent), percent, "Progress percent must be between 0 and 100.");
        }

        await this.AppendBestEffortAsync(
          new AppendDashboardJobEventRequest(
            this.JobId,
            DashboardJobEventKind.Progress,
            this.AttemptNumber,
            Message: message,
            ProgressPercent: percent),
          cancellationToken)
          .ConfigureAwait(false);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Job-context telemetry is best-effort by v4 design.")]
    private async ValueTask AppendBestEffortAsync(
        AppendDashboardJobEventRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await eventSink.AppendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Best-effort telemetry must not fail the owning job.
        }
    }

    private static void ValidateFields(IReadOnlyDictionary<string, string>? fields)
    {
        if (fields is null)
        {
            return;
        }

        foreach (var (key, value) in fields)
        {
            if (key is null)
            {
                throw new ArgumentException("Job log field names cannot be null.", nameof(fields));
            }

            if (value is null)
            {
                throw new ArgumentException("Job log field values cannot be null.", nameof(fields));
            }
        }
    }
}
