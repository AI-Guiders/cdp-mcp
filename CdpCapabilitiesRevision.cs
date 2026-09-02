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
        await foreach (var rev in _watch.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return rev;
    }
}
