#nullable enable
namespace CdpMcp;

/// <summary>Once-per-process cold desk warm (bookmark restore on first cockpit).</summary>
internal static class DeskWarm
{
    static int _consumed;

    /// <returns>true if this process may attempt auto-restore now.</returns>
    public static bool TryConsume() => Interlocked.Exchange(ref _consumed, 1) == 0;
}
