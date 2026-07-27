#nullable enable
namespace CdpMcp.Cockpit.Surface;

/// <summary>Project soft-organ board into seat pane DTO (full wrap or as-is).</summary>
public static class SeatOrganPanePresenter
{
    public static object FullOr(object board, bool wantFull, string go, string tool) =>
        wantFull ? Full(board, go, tool) : board;

    public static object Full(object board, string go, string tool) => new
    {
        ok = true,
        go,
        tool,
        detail = "full",
        truncated = false,
        result = board
    };

    public static object Pulse(string go, string tool, string pulse, string? schema, string? hint) => new
    {
        ok = true,
        go,
        tool,
        detail = "pulse",
        pulse,
        schema,
        hint
    };

    public static object PulseWithResult(
        object board,
        string go,
        string pulse,
        string? tool = null) => new
    {
        ok = true,
        go,
        tool,
        detail = "pulse",
        pulse,
        result = board
    };
}
