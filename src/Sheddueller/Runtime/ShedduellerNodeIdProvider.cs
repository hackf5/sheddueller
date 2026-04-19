namespace Sheddueller.Runtime;

using Microsoft.Extensions.Options;

using Sheddueller.DependencyInjection;

internal sealed class ShedduellerNodeIdProvider : IShedduellerNodeIdProvider
{
    public ShedduellerNodeIdProvider(IOptions<ShedduellerOptions> options)
    {
        var configuredNodeId = options.Value.NodeId;
        this.NodeId = string.IsNullOrWhiteSpace(configuredNodeId)
            ? $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}"
            : configuredNodeId;
    }

    public string NodeId { get; }
}
