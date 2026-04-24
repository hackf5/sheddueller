namespace Sheddueller.Testing;

using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

/// <summary>
/// The recorded jobs matched by a <see cref="FakeJobEnqueuer"/> expression.
/// </summary>
[SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "This type represents a match result, not a general-purpose collection.")]
public sealed class FakeJobMatch : IReadOnlyList<FakeEnqueuedJob>
{
    private readonly ReadOnlyCollection<FakeEnqueuedJob> _jobs;

    internal FakeJobMatch(IReadOnlyList<FakeEnqueuedJob> jobs)
      => this._jobs = Array.AsReadOnly([.. jobs]);

    /// <inheritdoc />
    public int Count => this._jobs.Count;

    /// <inheritdoc />
    public FakeEnqueuedJob this[int index] => this._jobs[index];

    /// <inheritdoc />
    public IEnumerator<FakeEnqueuedJob> GetEnumerator()
      => this._jobs.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
      => this.GetEnumerator();
}
