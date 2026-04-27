namespace Sheddueller.Worker.Internal;

using System.Collections.Concurrent;

internal static class JobLogCaptureContext
{
    private static readonly AsyncLocal<CapturedJobLogContext?> Current = new();
    private static readonly ConcurrentDictionary<Guid, byte> ActiveExecutions = new();

    public static CapturedJobLogContext? Active
    {
        get
        {
            var current = Current.Value;
            return current is not null && ActiveExecutions.ContainsKey(current.ExecutionId)
              ? current
              : null;
        }
    }

    public static IDisposable Begin(
        Guid jobId,
        int attemptNumber)
    {
        var previous = Current.Value;
        var current = new CapturedJobLogContext(Guid.NewGuid(), jobId, attemptNumber);
        ActiveExecutions.TryAdd(current.ExecutionId, 0);
        Current.Value = current;

        return new Scope(current.ExecutionId, previous);
    }

    private sealed class Scope(
        Guid executionId,
        CapturedJobLogContext? previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref this._disposed, 1) != 0)
            {
                return;
            }

            ActiveExecutions.TryRemove(executionId, out _);
            Current.Value = previous;
        }
    }
}
