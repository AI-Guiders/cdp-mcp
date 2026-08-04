#nullable enable
using Xunit;

namespace CdpMcp.Tests;

public sealed class CitizenIntercomHumanSurfaceTests
{
    [Fact]
    public void StripWire_drops_intent_event_and_peer_tip()
    {
        var raw = """
            Коротко: план открыт.

            @intent go=plan
            @event peer v0
            kind  | intent_ack
            id    | turn-1

            ok · gen=1 · mcp=live · compact=no · ack=1/1 · plan
            """;

        var clean = CitizenIntercomHumanSurface.StripWire(raw);
        Assert.Equal("Коротко: план открыт.", clean);
        Assert.DoesNotContain("@intent", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ack=", clean, StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_appends_human_hands_not_peer_wire()
    {
        var body = CitizenIntercomHumanSurface.Publish(
            "Готово.\n\n@intent build",
            [
                new CitizenRouteHost.Applied(
                    Raw: "@intent build",
                    Verb: "Build",
                    Ok: true,
                    Action: "build",
                    Pulse: "build ok E×0 W×180")
            ]);

        Assert.StartsWith("Готово.", body, StringComparison.Ordinal);
        Assert.Contains("Сделала: сборка · build ok E×0 W×180", body, StringComparison.Ordinal);
        Assert.DoesNotContain("@intent", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ack=", body, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatHands_marks_failure()
    {
        var tip = CitizenIntercomHumanSurface.FormatHands(
        [
            new CitizenRouteHost.Applied(
                Raw: "@intent go=plan",
                Verb: "Go",
                Ok: false,
                Go: "plan",
                Reason: "busy")
        ]);
        Assert.Equal("Сделала: plan — не вышло", tip);
    }

    [Fact]
    public void LooksLikeSaInstrumentWall_detects_frame_desk_dump()
    {
        var wall =
            "Света, спасибо за `@frame desk v0`. Вижу:\n\n" +
            "- **`tm | Shared-SSOT › Dig densest`**\n" +
            "- **`board | P:webcam_desk · F:editor · M:shell`**\n" +
            "…[truncated habitat wake]";
        Assert.True(CitizenIntercomHumanSurface.LooksLikeSaInstrumentWall(wall));
    }

    [Fact]
    public void StripWire_drops_sa_instrument_bullets()
    {
        var raw =
            "Коротко: leaf жив.\n\n" +
            "- **`tm | Shared-SSOT › Dig densest`**\n" +
            "- **`board | P:webcam · F:editor`**\n" +
            "ok · gen=1 · mcp=live · compact=no · ack=1/1";
        var clean = CitizenIntercomHumanSurface.StripWire(raw);
        Assert.Equal("Коротко: leaf жив.", clean);
        Assert.DoesNotContain("tm |", clean, StringComparison.Ordinal);
        Assert.DoesNotContain("board |", clean, StringComparison.Ordinal);
    }
}
