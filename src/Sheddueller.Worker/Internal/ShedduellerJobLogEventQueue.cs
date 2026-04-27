namespace Sheddueller.Worker.Internal;

using System.Threading.Channels;

using Sheddueller.Storage;

internal sealed class ShedduellerJobLogEventQueue
{
    private readonly Channel<AppendJobEventRequest> _channel = Channel.CreateUnbounded<AppendJobEventRequest>(
      new UnboundedChannelOptions
      {
          AllowSynchronousContinuations = false,
          SingleReader = true,
          SingleWriter = false,
      });

    public ChannelReader<AppendJobEventRequest> Reader
      => this._channel.Reader;

    public bool TryEnqueue(AppendJobEventRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return this._channel.Writer.TryWrite(request);
    }
}
