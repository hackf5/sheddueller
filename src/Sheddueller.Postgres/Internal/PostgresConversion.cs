namespace Sheddueller.Postgres.Internal;

using System.Globalization;

using Sheddueller.Dashboard;
using Sheddueller.Serialization;
using Sheddueller.Storage;

internal static class PostgresConversion
{
    public static long? ToMilliseconds(TimeSpan? value)
      => value is null ? null : checked((long)value.Value.TotalMilliseconds);

    public static TimeSpan? FromMilliseconds(object value)
      => value is DBNull ? null : TimeSpan.FromMilliseconds(Convert.ToInt64(value, CultureInfo.InvariantCulture));

    public static string? ToText(RetryBackoffKind? value)
      => value?.ToString();

    public static RetryBackoffKind? ToRetryBackoffKind(object value)
      => value is DBNull ? null : Enum.Parse<RetryBackoffKind>((string)value);

    public static string ToText(JobState value)
      => value.ToString();

    public static JobState ToJobState(object value)
      => Enum.Parse<JobState>((string)value);

    public static string ToText(RecurringOverlapMode value)
      => value.ToString();

    public static RecurringOverlapMode ToRecurringOverlapMode(object value)
      => Enum.Parse<RecurringOverlapMode>((string)value);

    public static string ToText(DashboardJobEventKind value)
      => value.ToString();

    public static DashboardJobEventKind ToDashboardJobEventKind(object value)
      => Enum.Parse<DashboardJobEventKind>((string)value);

    public static string ToText(JobLogLevel value)
      => value.ToString();

    public static JobLogLevel? ToJobLogLevel(object value)
      => value is DBNull ? null : Enum.Parse<JobLogLevel>((string)value);

    public static DateTimeOffset ToDateTimeOffset(object value)
      => value switch
      {
          DateTimeOffset dateTimeOffset => dateTimeOffset,
          DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
          _ => throw new InvalidOperationException($"Value of type '{value.GetType()}' is not a PostgreSQL timestamp."),
      };

    public static SerializedJobPayload ToPayload(object contentType, object data)
      => new((string)contentType, (byte[])data);

    public static RetryPolicy? ToRetryPolicy(bool configured, int maxAttempts, object backoffKind, object baseDelayMs, object maxDelayMs)
    {
        if (!configured || maxAttempts <= 1)
        {
            return null;
        }

        return new RetryPolicy(
          maxAttempts,
          Enum.Parse<RetryBackoffKind>((string)backoffKind),
          TimeSpan.FromMilliseconds(Convert.ToInt64(baseDelayMs, CultureInfo.InvariantCulture)),
          FromMilliseconds(maxDelayMs));
    }

    public static JobFailureInfo? ToFailure(object typeName, object message, object stackTrace)
    {
        if (typeName is DBNull || message is DBNull)
        {
            return null;
        }

        return new JobFailureInfo((string)typeName, (string)message, stackTrace is DBNull ? null : (string)stackTrace);
    }
}
