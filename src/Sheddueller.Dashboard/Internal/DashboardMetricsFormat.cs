namespace Sheddueller.Dashboard.Internal;

using System.Globalization;

using Sheddueller.Inspection.Metrics;

internal static class DashboardMetricsFormat
{
    public static bool HasNoActivity(MetricsInspectionWindow window)
      => window.QueuedCount == 0
        && window.ClaimedCount == 0
        && window.FailedCount == 0
        && window.CanceledCount == 0
        && window.OldestQueuedAge is null
        && window.EnqueueRatePerMinute == 0
        && window.ClaimRatePerMinute == 0
        && window.SuccessRatePerMinute == 0
        && window.FailureRatePerMinute == 0
        && window.CancellationRatePerMinute == 0
        && window.RetryRatePerMinute == 0
        && window.P50QueueLatency is null
        && window.P95QueueLatency is null
        && window.P50ExecutionDuration is null
        && window.P95ExecutionDuration is null
        && window.P95ScheduleFireLag is null
        && window.SaturatedConcurrencyGroupCount == 0
        && window.ActiveNodeCount == 0
        && window.StaleNodeCount == 0
        && window.DeadNodeCount == 0;

    public static string WindowLabel(TimeSpan window)
    {
        if (window == TimeSpan.FromMinutes(5))
        {
            return "5m";
        }

        if (window == TimeSpan.FromHours(1))
        {
            return "1h";
        }

        if (window == TimeSpan.FromHours(24))
        {
            return "24h";
        }

        return window.TotalHours >= 1
          ? string.Create(CultureInfo.InvariantCulture, $"{window.TotalHours:0.#}h")
          : string.Create(CultureInfo.InvariantCulture, $"{window.TotalMinutes:0.#}m");
    }

    public static string Rate(double rate)
    {
        if (rate == 0)
        {
            return "0/min";
        }

        if (Math.Abs(rate) < 0.1)
        {
            return "<0.1/min";
        }

        return rate >= 1000
          ? string.Create(CultureInfo.InvariantCulture, $"{rate:N0}/min")
          : string.Create(CultureInfo.InvariantCulture, $"{rate:0.#}/min");
    }

    public static string Duration(TimeSpan? duration)
    {
        if (duration is null)
        {
            return "-";
        }

        if (duration.Value < TimeSpan.FromMilliseconds(1))
        {
            return "<1 ms";
        }

        if (duration.Value < TimeSpan.FromSeconds(1))
        {
            return string.Create(CultureInfo.InvariantCulture, $"{duration.Value.TotalMilliseconds:0.#} ms");
        }

        if (duration.Value < TimeSpan.FromMinutes(1))
        {
            return string.Create(CultureInfo.InvariantCulture, $"{duration.Value.TotalSeconds:0.#} s");
        }

        if (duration.Value < TimeSpan.FromHours(1))
        {
            return string.Create(CultureInfo.InvariantCulture, $"{duration.Value.TotalMinutes:0.#} m");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{duration.Value.TotalHours:0.#} h");
    }

    public static TimeSpan? MaxDuration(params TimeSpan?[] durations)
    {
        var values = durations
          .Where(static duration => duration is not null)
          .Select(static duration => duration!.Value)
          .ToArray();

        return values.Length == 0 ? null : values.Max();
    }

    public static string DurationBarWidthStyle(
        TimeSpan? duration,
        TimeSpan? maxDuration)
    {
        if (duration is null || maxDuration is null || maxDuration.Value <= TimeSpan.Zero)
        {
            return "width: 0%";
        }

        var percent = duration.Value <= TimeSpan.Zero
          ? 0
          : Math.Clamp(duration.Value.TotalMilliseconds / maxDuration.Value.TotalMilliseconds * 100, 4, 100);

        return string.Create(CultureInfo.InvariantCulture, $"width: {percent:0.##}%");
    }

    public static string AttentionValueClass(int value)
      => value > 0 ? "metrics-value--attention" : string.Empty;

    public static string AttentionValueClass(double value)
      => value > 0 ? "metrics-value--attention" : string.Empty;

    public static string WarningValueClass(double value)
      => value > 0 ? "metrics-value--warning" : string.Empty;

    public static string SaturatedGroupTitle(MetricsInspectionWindow window)
      => string.Create(CultureInfo.InvariantCulture, $"{DashboardFormat.Count(window.SaturatedConcurrencyGroupCount)} saturated groups");
}
