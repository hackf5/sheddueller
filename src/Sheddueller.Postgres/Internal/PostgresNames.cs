// spell-checker: ignore wakeup

namespace Sheddueller.Postgres.Internal;

internal sealed class PostgresNames
{
    public const int ExpectedSchemaVersion = 2;
    public const string WakeupChannel = "sheddueller_wakeup";
    public const string DashboardEventChannel = "sheddueller_dashboard_event";

    public PostgresNames(string schemaName)
    {
        this.SchemaName = schemaName;
        this.Schema = QuoteIdentifier(schemaName);
        this.SchemaInfo = this.Table("schema_info");
        this.Tasks = this.Table("tasks");
        this.TaskConcurrencyGroups = this.Table("task_concurrency_groups");
        this.TaskTags = this.Table("task_tags");
        this.ConcurrencyGroups = this.Table("concurrency_groups");
        this.RecurringSchedules = this.Table("recurring_schedules");
        this.ScheduleConcurrencyGroups = this.Table("schedule_concurrency_groups");
        this.DashboardEvents = this.Table("dashboard_events");
    }

    public string SchemaName { get; }

    public string Schema { get; }

    public string SchemaInfo { get; }

    public string Tasks { get; }

    public string TaskConcurrencyGroups { get; }

    public string TaskTags { get; }

    public string ConcurrencyGroups { get; }

    public string RecurringSchedules { get; }

    public string ScheduleConcurrencyGroups { get; }

    public string DashboardEvents { get; }

    public static string QuoteIdentifier(string identifier)
      => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private string Table(string tableName)
      => $"{this.Schema}.{QuoteIdentifier(tableName)}";
}
