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
    public void Publish_keeps_prose_without_hands_laundry()
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
            ],
            elapsed: TimeSpan.FromSeconds(12));

        Assert.Equal("Готово.", body);
        Assert.DoesNotContain("Сделала", body, StringComparison.Ordinal);
        Assert.DoesNotContain("OK · ok×", body, StringComparison.Ordinal);
        Assert.DoesNotContain("@intent", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ack=", body, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatHands_marks_failure_with_softorgan_keywords()
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
        Assert.Contains("FAIL · ok×0 · fail×1", tip, StringComparison.Ordinal);
        Assert.Contains("• plan · fail · busy", tip, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatHands_includes_elapsed_for_softorgan_tip()
    {
        var tip = CitizenIntercomHumanSurface.FormatHands(
        [
            new CitizenRouteHost.Applied(
                Raw: "@intent kb",
                Verb: "Kb",
                Ok: true,
                Pulse: "kb · read · file_path=worlds/x.md · n=3")
        ],
        elapsed: TimeSpan.FromSeconds(12.4));
        Assert.Contains("OK · ok×1 · 12s", tip, StringComparison.Ordinal);
        Assert.Contains("• KB · ok · kb · read · file_path=worlds/x.md · n=3", tip, StringComparison.Ordinal);
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
