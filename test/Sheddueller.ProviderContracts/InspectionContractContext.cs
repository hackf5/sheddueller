namespace Sheddueller.ProviderContracts;

using Sheddueller.Inspection.ConcurrencyGroups;
using Sheddueller.Inspection.Jobs;
using Sheddueller.Inspection.Metrics;
using Sheddueller.Inspection.Nodes;
using Sheddueller.Inspection.Schedules;
using Sheddueller.Storage;

public sealed class InspectionContractContext(
    IJobStore store,
    IJobInspectionReader reader,
    IJobEventSink eventSink,
    IJobEventRetentionStore retentionStore,
    IScheduleInspectionReader scheduleReader,
    IConcurrencyGroupInspectionReader concurrencyGroupReader,
    INodeInspectionReader nodeReader,
    IMetricsInspectionReader metricsReader,
    IAsyncDisposable? asyncDisposable = null) : IAsyncDisposable
{
    public IJobStore Store { get; } = store;

    public IJobInspectionReader Reader { get; } = reader;

    public IJobEventSink EventSink { get; } = eventSink;

    public IJobEventRetentionStore RetentionStore { get; } = retentionStore;

    public IScheduleInspectionReader ScheduleReader { get; } = scheduleReader;

    public IConcurrencyGroupInspectionReader ConcurrencyGroupReader { get; } = concurrencyGroupReader;

    public INodeInspectionReader NodeReader { get; } = nodeReader;

    public IMetricsInspectionReader MetricsReader { get; } = metricsReader;

    public async ValueTask DisposeAsync()
    {
        if (asyncDisposable is not null)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
    }
}
