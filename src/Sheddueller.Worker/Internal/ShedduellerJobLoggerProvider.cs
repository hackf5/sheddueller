namespace Sheddueller.Worker.Internal;

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sheddueller;
using Sheddueller.Storage;

internal sealed class ShedduellerJobLoggerProvider(
    ShedduellerJobLogEventQueue eventQueue,
    IOptions<ShedduellerOptions> options) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
      => new JobLogger(eventQueue, options.Value.EnableJobLogCapture, categoryName);

    public void Dispose()
    {
    }

    private sealed class JobLogger(
        ShedduellerJobLogEventQueue eventQueue,
        bool enableJobLogCapture,
        string categoryName) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state)
          where TState : notnull
          => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel)
          => logLevel != LogLevel.None
            && enableJobLogCapture;

        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Captured job logging is best-effort and must not fail jobs.")]
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.None || !enableJobLogCapture)
            {
                return;
            }

            var activeJob = JobLogCaptureContext.Active;
            if (activeJob is null)
            {
                return;
            }

            try
            {
                ArgumentNullException.ThrowIfNull(formatter);

                var message = formatter(state, exception);
                if (string.IsNullOrEmpty(message) && exception is not null)
                {
                    message = exception.Message;
                }

                var request = new AppendJobEventRequest(
                  activeJob.JobId,
                  JobEventKind.Log,
                  activeJob.AttemptNumber,
                  ToJobLogLevel(logLevel),
                  message,
                  Fields: this.CreateFields(eventId, state, exception));

                _ = eventQueue.TryEnqueue(request);
            }
            catch (Exception)
            {
                // Captured logs are durable telemetry, not part of the job contract.
            }
        }

        private Dictionary<string, string>? CreateFields<TState>(
            EventId eventId,
            TState state,
            Exception? exception)
        {
            Dictionary<string, string>? fields = null;

            AddField(ref fields, "LoggerCategory", categoryName);
            if (eventId.Id != 0)
            {
                AddField(ref fields, "EventId", eventId.Id.ToString(CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrEmpty(eventId.Name))
            {
                AddField(ref fields, "EventName", eventId.Name);
            }

            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                foreach (var (key, value) in values)
                {
                    if (string.IsNullOrEmpty(key) || key == "{OriginalFormat}" || value is null)
                    {
                        continue;
                    }

                    AddField(ref fields, key, FormatFieldValue(value));
                }
            }

            if (exception is not null)
            {
                AddField(ref fields, "ExceptionType", exception.GetType().FullName ?? exception.GetType().Name);
                AddField(ref fields, "ExceptionMessage", exception.Message);
            }

            return fields;
        }

        private static void AddField(
            ref Dictionary<string, string>? fields,
            string key,
            string value)
        {
            fields ??= new Dictionary<string, string>(StringComparer.Ordinal);
            fields[key] = value;
        }

        private static string FormatFieldValue(object value)
          => value is IFormattable formattable
            ? formattable.ToString(format: null, CultureInfo.InvariantCulture)
            : value.ToString() ?? string.Empty;

        private static JobLogLevel ToJobLogLevel(LogLevel logLevel)
          => logLevel switch
          {
              LogLevel.Trace => JobLogLevel.Trace,
              LogLevel.Debug => JobLogLevel.Debug,
              LogLevel.Information => JobLogLevel.Information,
              LogLevel.Warning => JobLogLevel.Warning,
              LogLevel.Error => JobLogLevel.Error,
              LogLevel.Critical => JobLogLevel.Critical,
              _ => JobLogLevel.Information,
          };
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
