// spell-checker: ignore Cronos

namespace Sheddueller.Scheduling;

using Cronos;

internal static class CronSchedule
{
    public static void Validate(string cronExpression)
    {
        ArgumentException.ThrowIfNullOrEmpty(cronExpression);
        CronExpression.Parse(cronExpression, CronFormat.Standard);
    }

    public static DateTimeOffset GetNextOccurrenceAfter(string cronExpression, DateTimeOffset afterUtc)
    {
        var expression = CronExpression.Parse(cronExpression, CronFormat.Standard);
        var next = expression.GetNextOccurrence(afterUtc.UtcDateTime, TimeZoneInfo.Utc, inclusive: false)
          ?? throw new InvalidOperationException($"Cron expression '{cronExpression}' does not have a next occurrence.");

        return new DateTimeOffset(DateTime.SpecifyKind(next, DateTimeKind.Utc));
    }
}
