using System.Threading.Channels;

namespace EconomyService.Services;

// Hand-off between the POST endpoint and the background runner that
// executes the saga. Bounded with Wait backpressure rather than unbounded,
// so a runaway backlog slows POST responses instead of growing memory
// without limit.
public sealed class ConversionSagaChannel
{
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.Wait });

    public ChannelWriter<Guid> Writer => _channel.Writer;
    public ChannelReader<Guid> Reader => _channel.Reader;
}
