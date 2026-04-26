# Logging

Sheddueller uses `Microsoft.Extensions.Logging` for runtime diagnostics. Logs describe library behavior; durable job logs written through `IJobContext.LogAsync` remain separate job-history events for dashboard inspection.

## LoggerMessage Pattern

Use source-generated `LoggerMessage` extension methods for all Sheddueller diagnostic logs.

```csharp
namespace Microsoft.Extensions.Logging;

internal static partial class ShedduellerWorkerLoggerMessages
{
    private const int EventIdStart = 1100;

    [LoggerMessage(
        EventIdStart + 20,
        LogLevel.Debug,
        "Completed job {JobId} attempt {AttemptNumber} on node {NodeId}.")]
    public static partial void JobCompleted(
        this ILogger logger,
        Guid jobId,
        int attemptNumber,
        string nodeId);
}
```

Keep structured parameters primitive or simple primitive collections. Do not log serialized payloads, method argument values, connection strings, arbitrary object graphs, or Serilog-specific destructuring syntax.

Use `ElapsedMs` with `long` and `{ElapsedMs:D} ms` when logging timings.

## EventId Ranges

Assign EventIds by assembly and group related events within each range.

| Assembly | Range | Current groups |
| --- | ---: | --- |
| `Sheddueller` | `1000-1099` | enqueue, cancellation, recurring schedules, concurrency groups, durable event append diagnostics |
| `Sheddueller.Worker` | `1100-1199` | worker lifecycle, claim loop, execution outcomes, leases, cancellation, recovery/materialization |
| `Sheddueller.Postgres` | `1200-1299` | wake listener, job-event listener, notification publishing |
| `Sheddueller.Dashboard` | `1300-1399` | listener dispatch, live update publishing, event retention cleanup |
| Future packages/providers | `1400-1499` | reserved |

## Testing

Prefer EventId-focused tests for diagnostic behavior. Capture and assert the EventId, level, message template, exception, and structured properties instead of relying only on rendered message text.

Use the repository-level verification commands after adding or changing logging:

```bash
dotnet clean
dotnet build Sheddueller.slnx --configuration Debug
dotnet build Sheddueller.slnx --configuration Release
dotnet test --solution Sheddueller.slnx --configuration Release
dotnet format Sheddueller.slnx --verify-no-changes --verbosity minimal
```
