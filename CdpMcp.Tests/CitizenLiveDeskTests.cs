#nullable enable
using Xunit;

namespace CdpMcp.Tests;

public class CitizenLiveDeskTests
{
        [Fact]
    public void FromSeats_binds_plan_pulse_into_board()
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["p"] = "plan",
            ["forward"] = "editor_scene",
            ["m"] = "refactor_plan"
        };

        var pack = CitizenLiveDesk.FromSeats(map, "citizen chain dig wave25 › auto-bind");

        Assert.True(pack.FromLive);
        Assert.Equal(3, pack.BoardLines.Length);
        Assert.StartsWith("P", pack.BoardLines[0], StringComparison.Ordinal);
        Assert.Contains("citizen chain dig wave25", pack.BoardLines[0], StringComparison.Ordinal);
        Assert.Equal("citizen chain dig wave25 › auto-bind", pack.TmPulse);
        Assert.Contains("editor", pack.BoardLines[1], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromSeats_empty_seat_is_safe()
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["p"] = null,
            ["forward"] = "editor",
            ["m"] = null
        };

        var pack = CitizenLiveDesk.FromSeats(map, null);
        Assert.Equal(3, pack.BoardLines.Length);
        Assert.Contains("(empty)", pack.BoardLines[0], StringComparison.Ordinal);
        Assert.Null(pack.TmPulse);
    }

    [Fact]
    public void Channel_turn_omitted_board_sets_live_desk_flag()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""
            {"op":"turn","message":"hello","dry_run":true}
            """);
        var args = doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
        var json = IdeCitizenChannel.HandleJson(args);
        using var outDoc = System.Text.Json.JsonDocument.Parse(json);
        Assert.True(outDoc.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(outDoc.RootElement.GetProperty("injected").GetBoolean());
        Assert.True(outDoc.RootElement.TryGetProperty("live_desk", out var live));
        // Live bind may be empty in isolated test host — flag reports whether capture succeeded.
        _ = live.GetBoolean();
        var afferent = outDoc.RootElement.GetProperty("afferent").GetString();
        Assert.NotNull(afferent);
        Assert.StartsWith("@frame desk v0", afferent, StringComparison.Ordinal);
    }
}
