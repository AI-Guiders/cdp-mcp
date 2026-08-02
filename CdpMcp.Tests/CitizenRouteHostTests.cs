#nullable enable
using Xunit;

namespace CdpMcp.Tests;

public sealed class CitizenRouteHostTests
{
    [Fact]
    public void Execute_go_places_organ_on_seat()
    {
        IdeDeskSeats.EnsureDefaultsFromSettings();
        IdeDeskSeats.Clear();
        IdeDeskSeats.TryPlaceExplicit("p", "plan");
        IdeDeskSeats.TryPlaceExplicit("forward", "editor_scene");
        IdeDeskSeats.TryPlaceExplicit("m", "browser");

        var routes = new[] { CitizenIntentRouter.RouteOne("go=alert") };
        var applied = CitizenRouteHost.Execute(routes);

        Assert.Single(applied);
        Assert.True(applied[0].Ok);
        Assert.Equal("place", applied[0].Action);
        Assert.Equal("alert", applied[0].Go);
        Assert.NotNull(applied[0].Seat);

        var map = IdeDeskSeats.Snapshot();
        Assert.Contains(map, kv => string.Equals(kv.Value, "alert", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Execute_refuse_is_skipped()
    {
        var routes = new[] { CitizenIntentRouter.RouteOne("seats_detail=full") };
        var applied = CitizenRouteHost.Execute(routes);
        Assert.Single(applied);
        Assert.False(applied[0].Ok);
        Assert.Equal("refuse", applied[0].Action);
    }

    [Fact]
    public void Channel_dry_run_execute_true_runs_host()
    {
        IdeDeskSeats.EnsureDefaultsFromSettings();
        using var doc = System.Text.Json.JsonDocument.Parse("""
            {"op":"turn","message":"@intent go=plan","dry_run":true,"execute":true,"inject":false}
            """);
        var args = doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
        var json = IdeCitizenChannel.HandleJson(args);
        using var outDoc = System.Text.Json.JsonDocument.Parse(json);
        Assert.True(outDoc.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(outDoc.RootElement.GetProperty("execute").GetBoolean());
        Assert.True(outDoc.RootElement.TryGetProperty("executed", out var executed));
        Assert.Equal(System.Text.Json.JsonValueKind.Array, executed.ValueKind);
        Assert.True(executed.GetArrayLength() >= 1);
        Assert.True(executed[0].GetProperty("ok").GetBoolean());
        Assert.Equal("place", executed[0].GetProperty("action").GetString());
    }

    [Fact]
    public void Channel_dry_run_default_skips_execute()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""
            {"op":"turn","message":"@intent go=plan","dry_run":true,"inject":false}
            """);
        var args = doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
        var json = IdeCitizenChannel.HandleJson(args);
        using var outDoc = System.Text.Json.JsonDocument.Parse(json);
        Assert.True(outDoc.RootElement.GetProperty("ok").GetBoolean());
        Assert.False(outDoc.RootElement.GetProperty("execute").GetBoolean());
        Assert.True(outDoc.RootElement.TryGetProperty("executed", out var executed));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, executed.ValueKind);
    }
}
