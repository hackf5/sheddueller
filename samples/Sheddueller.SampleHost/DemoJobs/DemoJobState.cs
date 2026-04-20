namespace Sheddueller.SampleHost.DemoJobs;

using System.Collections.Concurrent;

public sealed class DemoJobState
{
    private readonly ConcurrentDictionary<string, int> _attemptCounts = new(StringComparer.Ordinal);

    public int IncrementAttemptCount(string key)
      => this._attemptCounts.AddOrUpdate(key, 1, static (_, count) => count + 1);
}
