namespace Sheddueller.SampleHost;

using System.Net;
using System.Text;

internal static class LauncherPageRenderer
{
    public static string Render(string? statusMessage)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"utf-8\" />");
        builder.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        builder.AppendLine("  <title>Sheddueller Sample Host</title>");
        builder.AppendLine("  <style>");
        builder.AppendLine("    :root { color-scheme: dark; }");
        builder.AppendLine("    * { box-sizing: border-box; }");
        builder.AppendLine("    body { margin: 0; font-family: Inter, Arial, sans-serif; background: #101418; color: #f4f1ea; }");
        builder.AppendLine("    main { max-width: 1180px; margin: 0 auto; padding: 32px 24px 40px; }");
        builder.AppendLine("    a { color: inherit; }");
        builder.AppendLine("    h1, h2, p { margin: 0; }");
        builder.AppendLine("    .hero { display: grid; gap: 20px; align-items: end; margin-bottom: 24px; padding-bottom: 24px; border-bottom: 1px solid #2d3439; }");
        builder.AppendLine("    .hero h1 { font-size: clamp(2rem, 6vw, 4.25rem); line-height: 0.95; font-weight: 800; max-width: 820px; }");
        builder.AppendLine("    .hero p { max-width: 720px; color: #bac5bd; font-size: 1.05rem; line-height: 1.55; }");
        builder.AppendLine("    .hero-actions { display: flex; flex-wrap: wrap; gap: 12px; }");
        builder.AppendLine("    .dashboard-link { display: inline-flex; align-items: center; min-height: 44px; padding: 0 18px; border-radius: 8px; background: #d8ff3e; color: #18200b; font-weight: 800; text-decoration: none; }");
        builder.AppendLine("    .status { margin-bottom: 24px; padding: 14px 16px; border: 1px solid #558b45; border-radius: 8px; background: #173a23; color: #dcfce7; }");
        builder.AppendLine("    .sections { display: grid; gap: 28px; }");
        builder.AppendLine("    .section-heading { display: flex; align-items: baseline; justify-content: space-between; gap: 16px; margin-bottom: 14px; }");
        builder.AppendLine("    .section-heading h2 { font-size: 1rem; letter-spacing: 0.08em; text-transform: uppercase; color: #f7d774; }");
        builder.AppendLine("    .section-heading p { color: #bac5bd; font-size: 0.95rem; }");
        builder.AppendLine("    .card-grid { display: grid; gap: 14px; grid-template-columns: repeat(auto-fit, minmax(230px, 1fr)); }");
        builder.AppendLine("    .action-card { display: grid; grid-template-rows: 1fr auto; gap: 16px; min-height: 190px; padding: 18px; border: 1px solid #303941; border-radius: 8px; background: #171d21; box-shadow: 0 12px 28px rgb(0 0 0 / 18%); }");
        builder.AppendLine("    .action-card__body { display: grid; gap: 8px; align-content: start; }");
        builder.AppendLine("    .action-card strong { font-size: 1.05rem; color: #ffffff; }");
        builder.AppendLine("    .action-card span { color: #bac5bd; line-height: 1.45; }");
        builder.AppendLine("    form { align-self: end; margin: 0; }");
        builder.AppendLine("    button { width: 100%; min-height: 42px; cursor: pointer; border: 0; border-radius: 8px; padding: 0 14px; font: inherit; font-weight: 800; background: #f7d774; color: #1d1a10; }");
        builder.AppendLine("    button:hover, .dashboard-link:hover { filter: brightness(1.06); }");
        builder.AppendLine("    @media (max-width: 640px) { main { padding: 24px 16px 32px; } .section-heading { display: grid; } .action-card { min-height: 0; } }");
        builder.AppendLine("  </style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("<main>");
        builder.AppendLine("  <section class=\"hero\">");
        builder.AppendLine("    <h1>Sheddueller Sample Host</h1>");
        builder.AppendLine("    <p>Create representative scheduler state here, then inspect jobs, schedules, logs, progress, and cancellation behavior in the embedded dashboard.</p>");
        builder.AppendLine("    <div class=\"hero-actions\">");
        builder.AppendLine("      <a class=\"dashboard-link\" href=\"/sheddueller/\">Open dashboard</a>");
        builder.AppendLine("    </div>");
        builder.AppendLine("  </section>");

        if (!string.IsNullOrWhiteSpace(statusMessage))
        {
            builder.Append("  <div class=\"status\">");
            builder.Append(Html(statusMessage));
            builder.AppendLine("</div>");
        }

        builder.AppendLine("  <div class=\"sections\">");
        builder.AppendLine("    <section>");
        builder.AppendLine("      <div class=\"section-heading\">");
        builder.AppendLine("        <h2>Enqueue Jobs</h2>");
        builder.AppendLine("        <p>Each card creates one dashboard-ready scenario.</p>");
        builder.AppendLine("      </div>");
        builder.AppendLine("      <div class=\"card-grid\">");
        AppendActionCard(builder, "/launch/quick-success", "Quick success", "Immediate completion for happy-path rows.", "Enqueue job");
        AppendActionCard(builder, "/launch/progress", "Progress + logs", "Emits durable logs and progress snapshots over a few seconds.", "Enqueue job");
        AppendActionCard(builder, "/launch/retry-then-succeed", "Retry then succeed", "Fails twice, then succeeds so retry history is visible.", "Enqueue job");
        AppendActionCard(builder, "/launch/permanent-failure", "Permanent failure", "Fails terminally without retries.", "Enqueue job");
        AppendActionCard(builder, "/launch/delayed", "Delayed job", "Queues a short delayed job to exercise delayed state and not-before time.", "Enqueue job");
        AppendActionCard(builder, "/launch/blocking-batch", "Concurrency batch", "Sets a shared group limit to 1 and enqueues several long jobs.", "Enqueue batch");
        AppendActionCard(builder, "/launch/idempotent", "Idempotent reprice", "Queues one reprice-listing-3 job behind a held group slot; click twice quickly to reuse the queued job.", "Enqueue job");
        AppendActionCard(builder, "/launch/cancelable", "Cancelable delayed job", "Creates a delayed queued job that can be canceled from the dashboard.", "Enqueue job");
        builder.AppendLine("      </div>");
        builder.AppendLine("    </section>");

        builder.AppendLine("    <section>");
        builder.AppendLine("      <div class=\"section-heading\">");
        builder.AppendLine("        <h2>Create Schedules</h2>");
        builder.AppendLine("        <p>Recurring definitions appear in the dashboard schedule views.</p>");
        builder.AppendLine("      </div>");
        builder.AppendLine("      <div class=\"card-grid\">");
        AppendActionCard(builder, "/launch/recurring", "Recurring demo", "Creates or updates a minute-based recurring schedule.", "Create schedule");
        builder.AppendLine("      </div>");
        builder.AppendLine("    </section>");
        builder.AppendLine("  </div>");
        builder.AppendLine("</main>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    private static void AppendActionCard(StringBuilder builder, string action, string title, string description, string buttonText)
    {
        builder.AppendLine("        <article class=\"action-card\">");
        builder.AppendLine("          <div class=\"action-card__body\">");
        builder.Append("            <strong>");
        builder.Append(Html(title));
        builder.AppendLine("</strong>");
        builder.Append("            <span>");
        builder.Append(Html(description));
        builder.AppendLine("</span>");
        builder.AppendLine("          </div>");
        builder.Append("          <form method=\"post\" action=\"");
        builder.Append(Html(action));
        builder.Append("\"><button type=\"submit\">");
        builder.Append(Html(buttonText));
        builder.AppendLine("</button></form>");
        builder.AppendLine("        </article>");
    }

    private static string Html(string value)
      => WebUtility.HtmlEncode(value);
}
