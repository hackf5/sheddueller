namespace Sheddueller.Inspection.Jobs;

/// <summary>
/// Options for reading one job's durable event stream.
/// </summary>
public sealed record JobEventReadOptions(
    long? AfterEventSequence = null,
    int Limit = 500);
