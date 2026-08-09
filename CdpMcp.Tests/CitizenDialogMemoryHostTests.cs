#nullable enable
using Xunit;

namespace CdpMcp.Tests;

public sealed class CitizenDialogMemoryHostTests
{
    public CitizenDialogMemoryHostTests()
    {
        CitizenDialogHistory.ResetForTests();
        CitizenStickyFacts.ResetForTests();
    }

    [Fact]
    public void RouteOne_dialog_clear_and_amnesia()
    {
        var clear = CitizenIntentRouter.RouteOne("dialog clear");
        Assert.True(clear.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.DialogMemory, clear.Verb);
        Assert.Equal("clear", clear.Op);

        var amnesia = CitizenIntentRouter.RouteOne("amnesia");
        Assert.True(amnesia.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.DialogMemory, amnesia.Verb);
        Assert.Equal("clear", amnesia.Op);
    }

    [Fact]
    public void Execute_dialog_clear_wipes_history()
    {
        CitizenDialogHistory.SetTestMemory(
        [
            new CitizenCompletions.ChatMessage("user", "hi"),
            new CitizenCompletions.ChatMessage("assistant", "hello")
        ]);
        Assert.NotEmpty(CitizenDialogHistory.Load());

        var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("dialog clear")]);
        Assert.Single(applied);
        Assert.True(applied[0].Ok);
        Assert.Equal("clear", applied[0].Action);
        Assert.Contains("cleared", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(CitizenDialogHistory.Load());
    }

    [Fact]
    public void Execute_dialog_scene_pulses_pairs()
    {
        CitizenDialogHistory.SetTestMemory(
        [
            new CitizenCompletions.ChatMessage("user", "a"),
            new CitizenCompletions.ChatMessage("assistant", "b")
        ]);

        var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("dialog")]);
        Assert.True(applied[0].Ok);
        Assert.Equal("scene", applied[0].Action);
        Assert.Contains("pairs=1", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
    }
}
