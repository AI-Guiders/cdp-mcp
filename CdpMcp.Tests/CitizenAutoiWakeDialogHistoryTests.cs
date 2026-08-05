#nullable enable
using Xunit;

namespace CdpMcp.Tests;

public sealed class CitizenAutoiWakeDialogHistoryTests : IDisposable
{
    public CitizenAutoiWakeDialogHistoryTests()
    {
        CitizenDialogHistory.ResetForTests();
        CitizenDialogHistory.SetTestMemory([]);
        IdeCitizenChannel.ResetAutoiWakeHooksForTests();
        IdeCitizenChannel.InviteReadyOverrideForTests = () => true;
        IdeCitizenChannel.AutoiWakeTurnOverrideForTests = charge =>
            new CitizenCompletions.TurnResult(
                Ok: true,
                Error: null,
                Hint: null,
                Text: "autoi-ok:" + charge,
                Model: "test",
                Provider: "mock",
                Built: null,
                WireIntents: null,
                Routes: null,
                DryRun: false);
    }

    public void Dispose()
    {
        IdeCitizenChannel.ResetAutoiWakeHooksForTests();
        CitizenDialogHistory.ResetForTests();
    }

    [Fact]
    public void TryDeliverAutoiWake_does_not_append_shared_dialog_history()
    {
        Assert.True(IdeCitizenChannel.TryDeliverAutoiWake("reason=remount — test", out var reply));
        Assert.Contains("autoi-ok:", reply, StringComparison.Ordinal);
        Assert.Empty(CitizenDialogHistory.Load());
    }
}
