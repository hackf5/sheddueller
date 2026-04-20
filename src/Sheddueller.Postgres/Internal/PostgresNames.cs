// spell-checker: ignore wakeup

namespace Sheddueller.Postgres.Internal;

internal sealed class PostgresNames
{
    public const int ExpectedSchemaVersion = 3;
    public const string WakeupChannel = "sheddueller_wakeup";
    public const string DashboardEventChannel = "sheddueller_dashboard_event";

    public PostgresNames(string schemaName)
    {
        this.SchemaName = schemaName;
        this.Schema = QuoteIdentifier(schemaName);
        this.SchemaInfo = this.Table("schema_info");
        this.Jobs = this.Table("jobs");
        this.JobConcurrencyGroups = this.Table("job_concurrency_groups");
        this.JobTags = this.Table("job_tags");
        this.ConcurrencyGroups = this.Table("concurrency_groups");
        this.RecurringSchedules = this.Table("recurring_schedules");
        this.ScheduleConcurrencyGroups = this.Table("schedule_concurrency_groups");
        this.DashboardEvents = this.Table("dashboard_events");
    }

    public string SchemaName { get; }

    public string Schema { get; }

    public string SchemaInfo { get; }

    public string Jobs { get; }

    public string JobConcurrencyGroups { get; }

    public string JobTags { get; }

    public string ConcurrencyGroups { get; }

    public string RecurringSchedules { get; }

    public string ScheduleConcurrencyGroups { get; }

    public string DashboardEvents { get; }

    public static string QuoteIdentifier(string identifier)
      => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private string Table(string tableName)
      => $"{this.Schema}.{QuoteIdentifier(tableName)}";
}
