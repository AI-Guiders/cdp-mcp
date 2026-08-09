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
    public void RouteOne_partition_persist_rebuild_aliases()
    {
        var partition = CitizenIntentRouter.RouteOne("partition");
        Assert.True(partition.Ok);
        Assert.Equal("partition", partition.Op);

        var fork = CitizenIntentRouter.RouteOne("fork");
        Assert.True(fork.Ok);
        Assert.Equal("partition", fork.Op);

        var persist = CitizenIntentRouter.RouteOne("dialog persist key=call_me value=агентка");
        Assert.True(persist.Ok);
        Assert.Equal("persist", persist.Op);
        Assert.Equal("call_me", persist.Path);
        Assert.Equal("агентка", persist.Detail);

        var rebuild = CitizenIntentRouter.RouteOne("rebuild");
        Assert.True(rebuild.Ok);
        Assert.Equal("rebuild", rebuild.Op);

        var antidote = CitizenIntentRouter.RouteOne("antidote");
        Assert.True(antidote.Ok);
        Assert.Equal("rebuild", antidote.Op);
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
        Assert.Contains("pruned", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(CitizenDialogHistory.Load());
    }

    [Fact]
    public void Execute_partition_keeps_sticky()
    {
        CitizenStickyFacts.SetTestMemory(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["call_me"] = "агентка"
        });
        CitizenDialogHistory.SetTestMemory(
        [
            new CitizenCompletions.ChatMessage("user", "a"),
            new CitizenCompletions.ChatMessage("assistant", "b")
        ]);

        var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("dialog partition")]);
        Assert.True(applied[0].Ok);
        Assert.Equal("partition", applied[0].Action);
        Assert.Empty(CitizenDialogHistory.Load());
        Assert.Equal("агентка", CitizenStickyFacts.Load()["call_me"]);
        Assert.Contains("sticky kept", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_persist_sets_sticky()
    {
        CitizenStickyFacts.SetTestMemory(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        var applied = CitizenRouteHost.Execute(
            [CitizenIntentRouter.RouteOne("dialog persist key=north v=15.08")]);
        Assert.True(applied[0].Ok);
        Assert.Equal("persist", applied[0].Action);
        Assert.Equal("15.08", CitizenStickyFacts.Load()["north"]);
        Assert.Contains("persisted", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_rebuild_wipes_dialog_keeps_sticky()
    {
        CitizenStickyFacts.SetTestMemory(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["pin"] = "1"
        });
        CitizenDialogHistory.SetTestMemory(
        [
            new CitizenCompletions.ChatMessage("user", "poison"),
            new CitizenCompletions.ChatMessage("assistant", "lie")
        ]);

        var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("dialog rebuild")]);
        Assert.True(applied[0].Ok);
        Assert.Equal("rebuild", applied[0].Action);
        Assert.Empty(CitizenDialogHistory.Load());
        Assert.Equal("1", CitizenStickyFacts.Load()["pin"]);
        Assert.Contains("rebuilt", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public void AfferentLine_fat_includes_adcm_pressure()
    {
        var msgs = new List<CitizenCompletions.ChatMessage>();
        for (var i = 0; i < 8; i++)
            msgs.Add(new CitizenCompletions.ChatMessage(i % 2 == 0 ? "user" : "assistant", new string('x', 600)));
        CitizenDialogHistory.SetTestMemory(msgs);

        var line = CitizenDialogHistory.AfferentLine();
        Assert.Contains("pressure FAT", line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Partition=", line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Rebuild=", line, StringComparison.OrdinalIgnoreCase);
    }
}
