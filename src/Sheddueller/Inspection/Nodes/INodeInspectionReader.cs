namespace Sheddueller.Inspection.Nodes;

/// <summary>
/// Reads worker node inspection health metadata.
/// </summary>
public interface INodeInspectionReader
{
    /// <summary>
    /// Searches worker nodes using inspection filters.
    /// </summary>
    ValueTask<NodeInspectionPage> SearchNodesAsync(
        NodeInspectionQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one worker node detail record.
    /// </summary>
    ValueTask<NodeInspectionDetail?> GetNodeAsync(
        string nodeId,
        CancellationToken cancellationToken = default);
}
