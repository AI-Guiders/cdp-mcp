#nullable enable

namespace CdpMcp;

/// <summary>Process-local ignite seat (replaces CDP_IGNITE_SEAT env). Set from durable-job payload or --ignite-notify --seat.</summary>
internal static class IdeIgniteSeatContext
{
    static string? _current;

    internal static void Set(string? seat) =>
        _current = string.IsNullOrWhiteSpace(seat) ? null : seat.Trim();

    internal static string? Current => _current;
}
