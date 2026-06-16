namespace Sheddueller;

/// <summary>
/// Controls which existing recurring schedule state is overwritten by <c>CreateOrUpdateAsync</c>.
/// </summary>
/// <param name="OverwriteCronExpression">Whether an existing schedule's cron expression is replaced by the submitted cron expression.</param>
/// <param name="OverwritePausedState">Whether an existing schedule is reconciled to the submitted enabled state.</param>
public sealed record RecurringScheduleUpdateOptions(
    bool OverwriteCronExpression = true,
    bool OverwritePausedState = true)
{
    /// <summary>
    /// Gets the default update behavior.
    /// </summary>
    public static RecurringScheduleUpdateOptions Default { get; } = new();
}
