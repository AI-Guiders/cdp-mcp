using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace CdpMcp;

/// <summary>
/// Content-derived capabilities revision (ADR-0202). Epoch = хэш Meta-каталога
/// (имена + схемы), поэтому два процесса с одинаковым каталогом дают одинаковый
/// rev — кэши бриджа и сессий остаются валидными через рестарты, — а любое
/// изменение каталога или схем даёт новый epoch и гарантированную инвалидацию.
/// Bumps остаются монотонными внутри процесса (младшие 32 бита).
/// </summary>
internal sealed class CdpCapabilitiesRevision
{
    readonly long _epoch = EpochFromCatalog();
    long _bumps;

    internal CdpCapabilitiesRevision() { }

    internal CdpCapabilitiesRevision(long epoch) => _epoch = epoch;

    internal long Current => ((_epoch & 0xFFFF_FFFFL) << 32) | (Interlocked.Read(ref _bumps) & 0xFFFF_FFFFL);

    internal long Bump()
    {
        Interlocked.Increment(ref _bumps);
        var rev = Current;
        _ = _watch.Writer.TryWrite(rev);
        return rev;
    }

    static long EpochFromCatalog()
    {
        try
        {
            var payload = string.Join("|", MetaToolCatalog.All.Select(t =>
                $"{t.Name}:{t.Description}:{Raw(t.InputSchema)}:{Raw(t.OutputSchema)}"));
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
            return BitConverter.ToInt64(hash, 0) & 0xFFFF_FFFF;
        }
        catch
        {
            // Каталог недоступен при инициализации (не ожидается) — фолбэк на
            // epoch времени процесса: rev всё ещё уникален между процессами.
            return (uint)(DateTimeOffset.UtcNow.UtcTicks & 0xFFFF_FFFF);
        }
    }

    static string Raw(JsonElement? element) => element?.GetRawText() ?? "";

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
