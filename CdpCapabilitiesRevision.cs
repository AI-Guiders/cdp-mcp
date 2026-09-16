using System.Threading.Channels;

namespace CdpMcp;

/// <summary>Monotonic capabilities revision for bridge list_changed (ADR-0202).</summary>
internal sealed class CdpCapabilitiesRevision
{
    static long s_processSeq;

    readonly long _boot = Interlocked.Increment(ref s_processSeq);
    long _bumps;

    internal long Current => (_boot << 32) | (Interlocked.Read(ref _bumps) & 0xFFFF_FFFFL);

    internal long Bump()
    {
        Interlocked.Increment(ref _bumps);
        var rev = Current;
        _ = _watch.Writer.TryWrite(rev);
        return rev;
    }

    readonly Channel<long> _watch = Channel.CreateUnbounded<long>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

    internal async IAsyncEnumerable<long> WatchAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return Current;
        // WaitToReadAsync/TryRead instead of ReadAllAsync: the ReadAllAsync
        // iterator was the NRE source in the slot crash stream (AsyncStateMachineBox
        // inside ChannelReader.ReadAllAsync). The explicit loop keeps every fault
        // inside the consumer's own MoveNextAsync, which the SSE endpoint catches.
        var reader = _watch.Reader;
        while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (reader.TryRead(out var rev))
                yield return rev;
        }
    }
}
