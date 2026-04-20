namespace Sheddueller.Postgres.Internal;

internal sealed class PostgresNames
{
    public const int ExpectedSchemaVersion = 1;
    public const string WakeupChannel = "sheddueller_wakeup";

    public PostgresNames(string schemaName)
    {
        this.SchemaName = schemaName;
        this.Schema = QuoteIdentifier(schemaName);
        this.SchemaInfo = this.Table("schema_info");
        this.Tasks = this.Table("tasks");
        this.TaskConcurrencyGroups = this.Table("task_concurrency_groups");
        this.ConcurrencyGroups = this.Table("concurrency_groups");
        this.RecurringSchedules = this.Table("recurring_schedules");
        this.ScheduleConcurrencyGroups = this.Table("schedule_concurrency_groups");
    }

    public string SchemaName { get; }

    public string Schema { get; }

    public string SchemaInfo { get; }

    public string Tasks { get; }

    public string TaskConcurrencyGroups { get; }

    public string ConcurrencyGroups { get; }

    public string RecurringSchedules { get; }

    public string ScheduleConcurrencyGroups { get; }

    public static string QuoteIdentifier(string identifier)
      => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private string Table(string tableName)
      => $"{this.Schema}.{QuoteIdentifier(tableName)}";
}
