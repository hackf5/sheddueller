namespace Sheddueller.Testing;

using System.Linq.Expressions;

using Sheddueller.Serialization;

/// <summary>
/// Async-context-aware recurring schedule manager for dependency-injected tests.
/// </summary>
public sealed class CapturingRecurringScheduleManager(
    IJobPayloadSerializer serializer,
    TimeProvider timeProvider) : IRecurringScheduleManager
{
    private readonly AsyncLocal<FakeRecurringScheduleManager?> _current = new();
    private readonly IJobPayloadSerializer _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <summary>
    /// Begins capturing recurring schedules in the current async context.
    /// </summary>
    public RecurringScheduleManagerCapture Capture()
    {
        if (this._current.Value is not null)
        {
            throw new InvalidOperationException("A recurring schedule manager capture is already active in the current async context.");
        }

        var fake = new FakeRecurringScheduleManager(this._serializer, this._timeProvider);
        this._current.Value = fake;

        return new RecurringScheduleManagerCapture(fake, this);
    }

    /// <inheritdoc />
    public ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateAsync(
      string scheduleKey,
      string cronExpression,
      Expression<Func<CancellationToken, Task>> work,
      RecurringScheduleOptions? options = null,
      CancellationToken cancellationToken = default)
      => this.CurrentOrDiscardingFake().CreateOrUpdateAsync(scheduleKey, cronExpression, work, options, cancellationToken);

    /// <inheritdoc />
    public ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateAsync(
      string scheduleKey,
      string cronExpression,
      Expression<Func<CancellationToken, ValueTask>> work,
      RecurringScheduleOptions? options = null,
      CancellationToken cancellationToken = default)
      => this.CurrentOrDiscardingFake().CreateOrUpdateAsync(scheduleKey, cronExpression, work, options, cancellationToken);

    /// <inheritdoc />
    public ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateAsync<TService>(
      string scheduleKey,
      string cronExpression,
      Expression<Func<TService, CancellationToken, Task>> work,
      RecurringScheduleOptions? options = null,
      CancellationToken cancellationToken = default)
      => this.CurrentOrDiscardingFake().CreateOrUpdateAsync(scheduleKey, cronExpression, work, options, cancellationToken);

    /// <inheritdoc />
    public ValueTask<RecurringScheduleUpsertResult> CreateOrUpdateAsync<TService>(
      string scheduleKey,
      string cronExpression,
      Expression<Func<TService, CancellationToken, ValueTask>> work,
      RecurringScheduleOptions? options = null,
      CancellationToken cancellationToken = default)
      => this.CurrentOrDiscardingFake().CreateOrUpdateAsync(scheduleKey, cronExpression, work, options, cancellationToken);

    /// <inheritdoc />
    public ValueTask<RecurringScheduleTriggerResult> TriggerAsync(
      string scheduleKey,
      CancellationToken cancellationToken = default)
      => this.CurrentOrDiscardingFake().TriggerAsync(scheduleKey, cancellationToken);

    /// <inheritdoc />
    public ValueTask<bool> DeleteAsync(
      string scheduleKey,
      CancellationToken cancellationToken = default)
      => this.CurrentOrDiscardingFake().DeleteAsync(scheduleKey, cancellationToken);

    /// <inheritdoc />
    public ValueTask<bool> PauseAsync(
      string scheduleKey,
      CancellationToken cancellationToken = default)
      => this.CurrentOrDiscardingFake().PauseAsync(scheduleKey, cancellationToken);

    /// <inheritdoc />
    public ValueTask<bool> ResumeAsync(
      string scheduleKey,
      CancellationToken cancellationToken = default)
      => this.CurrentOrDiscardingFake().ResumeAsync(scheduleKey, cancellationToken);

    /// <inheritdoc />
    public ValueTask<RecurringScheduleInfo?> GetAsync(
      string scheduleKey,
      CancellationToken cancellationToken = default)
      => this.CurrentOrDiscardingFake().GetAsync(scheduleKey, cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<RecurringScheduleInfo> ListAsync(
      CancellationToken cancellationToken = default)
      => this.CurrentOrDiscardingFake().ListAsync(cancellationToken);

    internal void ClearCapture(FakeRecurringScheduleManager fake)
    {
        if (ReferenceEquals(this._current.Value, fake))
        {
            this._current.Value = null;
        }
    }

    private FakeRecurringScheduleManager CurrentOrDiscardingFake()
      => this._current.Value ?? new FakeRecurringScheduleManager(this._serializer, this._timeProvider);
}
