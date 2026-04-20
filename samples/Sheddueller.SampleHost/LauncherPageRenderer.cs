namespace Sheddueller.SampleHost;

using System.Globalization;
using System.Net;
using System.Text;

using Sheddueller.Dashboard;
using Sheddueller.Storage;

internal static class LauncherPageRenderer
{
    public static async Task<string> RenderAsync(
        string? statusMessage,
        IDashboardJobReader reader,
        IRecurringScheduleManager scheduleManager,
        CancellationToken cancellationToken)
    {
        var overview = await reader.GetOverviewAsync(cancellationToken).ConfigureAwait(false);
        var schedules = new List<RecurringScheduleInfo>();

        await foreach (var schedule in scheduleManager.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            schedules.Add(schedule);
        }

        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"utf-8\" />");
        builder.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        builder.AppendLine("  <title>Sheddueller Sample Host</title>");
        builder.AppendLine("  <style>");
        builder.AppendLine("    :root { color-scheme: dark; }");
        builder.AppendLine("    body { font-family: Inter, Arial, sans-serif; margin: 0; background: #111827; color: #e5e7eb; }");
        builder.AppendLine("    main { max-width: 1200px; margin: 0 auto; padding: 2rem; }");
        builder.AppendLine("    a { color: #93c5fd; }");
        builder.AppendLine("    h1, h2, h3 { margin-top: 0; }");
        builder.AppendLine("    .hero { display: grid; gap: 1rem; margin-bottom: 2rem; padding: 1.5rem; border: 1px solid #374151; border-radius: 1rem; background: linear-gradient(135deg, #111827, #1f2937); }");
        builder.AppendLine("    .hero-actions { display: flex; flex-wrap: wrap; gap: 0.75rem; }");
        builder.AppendLine("    .layout { display: grid; gap: 1.5rem; grid-template-columns: repeat(auto-fit, minmax(320px, 1fr)); }");
        builder.AppendLine("    section { padding: 1rem; border: 1px solid #374151; border-radius: 1rem; background: #0f172a; }");
        builder.AppendLine("    .cards { display: grid; gap: 0.75rem; grid-template-columns: repeat(auto-fit, minmax(140px, 1fr)); }");
        builder.AppendLine("    .card { padding: 0.9rem; border-radius: 0.75rem; background: #111827; border: 1px solid #1f2937; }");
        builder.AppendLine("    form { margin: 0; }");
        builder.AppendLine("    .scenario-grid { display: grid; gap: 0.75rem; }");
        builder.AppendLine("    .scenario { display: grid; gap: 0.4rem; padding: 0.9rem; border-radius: 0.75rem; background: #111827; border: 1px solid #1f2937; }");
        builder.AppendLine("    button { cursor: pointer; border: 0; border-radius: 999px; padding: 0.7rem 1rem; font: inherit; background: #2563eb; color: white; }");
        builder.AppendLine("    button.secondary { background: #374151; }");
        builder.AppendLine("    input { width: 100%; box-sizing: border-box; border-radius: 0.6rem; border: 1px solid #4b5563; background: #0b1120; color: inherit; padding: 0.7rem; }");
        builder.AppendLine("    table { width: 100%; border-collapse: collapse; }");
        builder.AppendLine("    th, td { padding: 0.5rem; text-align: left; border-bottom: 1px solid #1f2937; vertical-align: top; }");
        builder.AppendLine("    code { font-family: ui-monospace, SFMono-Regular, monospace; }");
        builder.AppendLine("    .status { margin-bottom: 1rem; padding: 0.85rem 1rem; border-radius: 0.75rem; background: #052e16; border: 1px solid #166534; }");
        builder.AppendLine("    .muted { color: #9ca3af; }");
        builder.AppendLine("  </style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("<main>");
        builder.AppendLine("  <section class=\"hero\">");
        builder.AppendLine("    <div>");
        builder.AppendLine("      <h1>Sheddueller Sample Host</h1>");
        builder.AppendLine("      <p>Launch representative jobs, then inspect them in the embedded dashboard while tuning the UI and scheduler behavior.</p>");
        builder.AppendLine("    </div>");
        builder.AppendLine("    <div class=\"hero-actions\">");
        builder.AppendLine("      <a href=\"/sheddueller/\">Open dashboard</a>");
        builder.AppendLine("      <a href=\"/sheddueller/jobs\">Open job search</a>");
        builder.AppendLine("    </div>");
        builder.AppendLine("  </section>");

        if (!string.IsNullOrWhiteSpace(statusMessage))
        {
            builder.Append("  <div class=\"status\">");
            builder.Append(Html(statusMessage));
            builder.AppendLine("</div>");
        }

        builder.AppendLine("  <div class=\"layout\">");
        builder.AppendLine("    <section>");
        builder.AppendLine("      <h2>Launcher</h2>");
        builder.AppendLine("      <p class=\"muted\">Each action creates scheduler state for the dashboard. Nothing is seeded automatically on startup.</p>");
        builder.AppendLine("      <div class=\"scenario-grid\">");
        AppendScenario(builder, "/launch/quick-success", "Quick success", "Immediate completion for happy-path rows.");
        AppendScenario(builder, "/launch/progress", "Progress + logs", "Emits durable logs and progress snapshots over a few seconds.");
        AppendScenario(builder, "/launch/retry-then-succeed", "Retry then succeed", "Fails twice, then succeeds so retry history is visible.");
        AppendScenario(builder, "/launch/permanent-failure", "Permanent failure", "Fails terminally without retries.");
        AppendScenario(builder, "/launch/delayed", "Delayed job", "Queues a short delayed job to exercise delayed state and not-before time.");
        AppendScenario(builder, "/launch/blocking-batch", "Concurrency batch", "Sets a shared group limit to 1 and enqueues several long jobs.");
        AppendScenario(builder, "/launch/recurring", "Recurring demo", "Creates or updates a minute-based recurring schedule.");
        AppendScenario(builder, "/launch/cancelable", "Cancelable delayed job", "Creates a delayed queued job that can be canceled from the form below.");
        builder.AppendLine("      </div>");
        builder.AppendLine("    </section>");

        builder.AppendLine("    <section>");
        builder.AppendLine("      <h2>Cancel Queued Job</h2>");
        builder.AppendLine("      <p class=\"muted\">Paste the job id from the launcher response or dashboard to cancel a queued delayed job.</p>");
        builder.AppendLine("      <form method=\"post\" action=\"/launch/cancel\"> ");
        builder.AppendLine("        <label for=\"jobId\">Job id</label>");
        builder.AppendLine("        <input id=\"jobId\" name=\"jobId\" placeholder=\"00000000-0000-0000-0000-000000000000\" />");
        builder.AppendLine("        <div style=\"margin-top:0.75rem\"><button type=\"submit\" class=\"secondary\">Cancel job</button></div>");
        builder.AppendLine("      </form>");
        builder.AppendLine("    </section>");

        builder.AppendLine("    <section>");
        builder.AppendLine("      <h2>State Counts</h2>");
        builder.AppendLine("      <div class=\"cards\">");
        foreach (var state in Enum.GetValues<JobState>())
        {
            var count = overview.StateCounts.TryGetValue(state, out var value) ? value : 0;
            builder.Append("        <div class=\"card\"><strong>");
            builder.Append(Html(state.ToString()));
            builder.Append("</strong><div>");
            builder.Append(Html(count.ToString(CultureInfo.InvariantCulture)));
            builder.AppendLine("</div></div>");
        }

        builder.AppendLine("      </div>");
        builder.AppendLine("    </section>");

        builder.AppendLine("    <section>");
        builder.AppendLine("      <h2>Recurring Schedules</h2>");
        if (schedules.Count == 0)
        {
            builder.AppendLine("      <p>No recurring schedules yet.</p>");
        }
        else
        {
            builder.AppendLine("      <table>");
            builder.AppendLine("        <thead><tr><th>Key</th><th>Cron</th><th>Paused</th><th>Overlap</th><th>Priority</th><th>Next fire</th></tr></thead>");
            builder.AppendLine("        <tbody>");
            foreach (var schedule in schedules.OrderBy(static item => item.ScheduleKey, StringComparer.Ordinal))
            {
                builder.AppendLine("          <tr>");
                builder.Append("            <td><code>");
                builder.Append(Html(schedule.ScheduleKey));
                builder.AppendLine("</code></td>");
                builder.Append("            <td><code>");
                builder.Append(Html(schedule.CronExpression));
                builder.AppendLine("</code></td>");
                builder.Append("            <td>");
                builder.Append(Html(schedule.IsPaused ? "yes" : "no"));
                builder.AppendLine("</td>");
                builder.Append("            <td>");
                builder.Append(Html(schedule.OverlapMode.ToString()));
                builder.AppendLine("</td>");
                builder.Append("            <td>");
                builder.Append(Html(schedule.Priority.ToString(CultureInfo.InvariantCulture)));
                builder.AppendLine("</td>");
                builder.Append("            <td>");
                builder.Append(Html(FormatTimestamp(schedule.NextFireAtUtc)));
                builder.AppendLine("</td>");
                builder.AppendLine("          </tr>");
            }

            builder.AppendLine("        </tbody>");
            builder.AppendLine("      </table>");
        }

        builder.AppendLine("    </section>");
        builder.AppendLine("  </div>");

        builder.AppendLine(RenderJobTable("Running", overview.RunningJobs));
        builder.AppendLine(RenderJobTable("Recently Failed", overview.RecentlyFailedJobs));
        builder.AppendLine(RenderJobTable("Queued", overview.QueuedJobs));
        builder.AppendLine(RenderJobTable("Delayed", overview.DelayedJobs));
        builder.AppendLine(RenderJobTable("Retry Waiting", overview.RetryWaitingJobs));
        builder.AppendLine("</main>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    private static void AppendScenario(StringBuilder builder, string action, string title, string description)
    {
        builder.AppendLine("        <div class=\"scenario\">");
        builder.Append("          <strong>");
        builder.Append(Html(title));
        builder.AppendLine("</strong>");
        builder.Append("          <span class=\"muted\">");
        builder.Append(Html(description));
        builder.AppendLine("</span>");
        builder.Append("          <form method=\"post\" action=\"");
        builder.Append(Html(action));
        builder.AppendLine("\"><button type=\"submit\">Launch</button></form>");
        builder.AppendLine("        </div>");
    }

    private static string RenderJobTable(string title, IReadOnlyList<DashboardJobSummary> jobs)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<section style=\"margin-top:1.5rem\">");
        builder.Append("  <h2>");
        builder.Append(Html(title));
        builder.AppendLine("</h2>");

        if (jobs.Count == 0)
        {
            builder.AppendLine("  <p>No jobs.</p>");
            builder.AppendLine("</section>");
            return builder.ToString();
        }

        builder.AppendLine("  <table>");
        builder.AppendLine("    <thead><tr><th>Job</th><th>State</th><th>Handler</th><th>Attempts</th><th>Queue</th><th>Progress</th><th>Enqueued</th></tr></thead>");
        builder.AppendLine("    <tbody>");
        foreach (var job in jobs)
        {
            builder.AppendLine("      <tr>");
            builder.Append("        <td><a href=\"/sheddueller/jobs/");
            builder.Append(job.JobId.ToString("D"));
            builder.Append("\"><code>");
            builder.Append(Html(job.JobId.ToString("D")));
            builder.AppendLine("</code></a></td>");
            builder.Append("        <td>");
            builder.Append(Html(job.State.ToString()));
            builder.AppendLine("</td>");
            builder.Append("        <td>");
            builder.Append(Html($"{job.ServiceType}.{job.MethodName}"));
            builder.AppendLine("</td>");
            builder.Append("        <td>");
            builder.Append(Html($"{job.AttemptCount}/{job.MaxAttempts}"));
            builder.AppendLine("</td>");
            builder.Append("        <td>");
            builder.Append(Html(FormatQueuePosition(job.QueuePosition)));
            builder.AppendLine("</td>");
            builder.Append("        <td>");
            builder.Append(Html(FormatProgress(job.LatestProgress)));
            builder.AppendLine("</td>");
            builder.Append("        <td>");
            builder.Append(Html(job.EnqueuedAtUtc.ToString("u", CultureInfo.InvariantCulture)));
            builder.AppendLine("</td>");
            builder.AppendLine("      </tr>");
        }

        builder.AppendLine("    </tbody>");
        builder.AppendLine("  </table>");
        builder.AppendLine("</section>");
        return builder.ToString();
    }

    private static string FormatProgress(DashboardProgressSnapshot? progress)
      => progress is null
        ? string.Empty
        : progress.Percent is null
          ? progress.Message ?? string.Empty
          : string.Create(CultureInfo.InvariantCulture, $"{progress.Percent:0.#}% {progress.Message}").Trim();

    private static string FormatQueuePosition(DashboardQueuePosition? position)
      => position is null
        ? string.Empty
        : position.Position is null
          ? $"{position.Kind}: {position.Reason}"
          : string.Create(CultureInfo.InvariantCulture, $"{position.Kind}: {position.Position}");

    private static string FormatTimestamp(DateTimeOffset? value)
      => value?.ToString("u", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Html(string value)
      => WebUtility.HtmlEncode(value);
}
