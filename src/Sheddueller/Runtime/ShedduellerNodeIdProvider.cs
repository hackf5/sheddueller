#pragma warning disable IDE0130

namespace Sheddueller;

using Microsoft.Extensions.Options;

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
