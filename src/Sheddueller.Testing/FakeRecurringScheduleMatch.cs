namespace Sheddueller.Testing;

using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

/// <summary>
/// The recurring schedules matched by a <see cref="FakeRecurringScheduleManager"/> expression.
/// </summary>
[SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "This type represents a match result, not a general-purpose collection.")]
public sealed class FakeRecurringScheduleMatch : IReadOnlyList<FakeRecurringSchedule>
{
    private readonly ReadOnlyCollection<FakeRecurringSchedule> _schedules;

    internal FakeRecurringScheduleMatch(IReadOnlyList<FakeRecurringSchedule> schedules)
      => this._schedules = Array.AsReadOnly([.. schedules]);

    /// <inheritdoc />
    public int Count => this._schedules.Count;

    /// <inheritdoc />
    public FakeRecurringSchedule this[int index] => this._schedules[index];

    /// <inheritdoc />
    public IEnumerator<FakeRecurringSchedule> GetEnumerator()
      => this._schedules.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
      => this.GetEnumerator();
}
