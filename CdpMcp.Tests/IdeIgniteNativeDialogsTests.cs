using Xunit;

namespace CdpMcp.Tests;

public class IdeIgniteNativeDialogsTests
{
    [Theory]
    [InlineData("The window is not responding. You can reopen or close the window or keep waiting.", true)]
    [InlineData("The window is not responding", false)] // alone — ambiguous vs OS hung copy without buttons
    [InlineData("Cursor.exe is not responding. Close the program / Wait for the program to respond.", false)]
    [InlineData("Connection Problems — Retry", false)]
    public void LooksLikeStallMessage_vs_os_hung(string text, bool expect) =>
        Assert.Equal(expect, IdeIgniteNativeDialogs.LooksLikeStallMessage(text));

    [Theory]
    [InlineData("The window terminated unexpectedly (reason: 'oom', code: '-536870904')", true)]
    [InlineData("We are sorry for the inconvenience. The window terminated unexpectedly (reason: oom).", true)]
    [InlineData("The window is not responding. Keep Waiting.", false)]
    [InlineData("Connection Problems — Retry", false)]
    public void LooksLikeOomTerminatedMessage(string text, bool expect) =>
        Assert.Equal(expect, IdeIgniteNativeDialogs.LooksLikeOomTerminatedMessage(text));

    [Theory]
    [InlineData("Keep Waiting", true)]
    [InlineData("&&Keep Waiting", true)]
    [InlineData("&Keep Waiting", true)]
    [InlineData("Reopen", false)]
    [InlineData("Close", false)]
    [InlineData("Wait for the program to respond", false)]
    public void IsKeepWaitingLabel_strips_mnemonic(string label, bool expect) =>
        Assert.Equal(expect, IdeIgniteNativeDialogs.IsKeepWaitingLabel(label));

    [Theory]
    [InlineData("New Window", false)]
    [InlineData("&New Window", false)]
    [InlineData("New empty window", false)]
    [InlineData("Reopen", true)]
    [InlineData("&Reopen", true)]
    [InlineData("Reopen Window", true)]
    [InlineData("Reopen the window", true)]
    [InlineData("Close", false)]
    [InlineData("Keep Waiting", false)]
    public void IsNewWindowLabel(string label, bool expect) =>
        Assert.Equal(expect, IdeIgniteNativeDialogs.IsNewWindowLabel(label));

    [Theory]
    [InlineData(new[] { "Reopen", "Close" }, true)]
    [InlineData(new[] { "&Reopen", "&Close" }, true)]
    [InlineData(new[] { "Reopen", "Close", "Keep Waiting" }, false)]
    [InlineData(new[] { "Close" }, false)]
    [InlineData(new[] { "New Window", "Close" }, false)]
    [InlineData(new string[0], false)]
    public void LooksLikeOomRecoveryButtons(string[] labels, bool expect) =>
        Assert.Equal(expect, IdeIgniteNativeDialogs.LooksLikeOomRecoveryButtons(labels));

    [Theory]
    [InlineData("Chrome_WidgetWin_1", true)]
    [InlineData("Chrome_WidgetWin_0", true)]
    [InlineData("#32770", true)]
    [InlineData("Electron_Dialog", true)]
    [InlineData("Notepad", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LooksLikeElectronClassName(string? cls, bool expect) =>
        Assert.Equal(expect, IdeIgniteNativeDialogs.LooksLikeElectronClassName(cls));

    [Fact]
    public void LooksLikeOomTerminatedMessage_accepts_msaa_joined_blob()
    {
        // WM_GETTEXT empty; body only in accessible names joined into blob.
        var blob = "Reopen Close The window terminated unexpectedly (reason: 'oom', code: '-1')";
        Assert.True(IdeIgniteNativeDialogs.LooksLikeOomTerminatedMessage(blob));
    }

    [Fact]
    public void TryClickKeepWaiting_without_dialog_returns_false()
    {
        // No stall dialog open — must not throw; typically false.
        var clicked = IdeIgniteNativeDialogs.TryClickKeepWaiting();
        Assert.False(clicked);
    }

    [Fact]
    public void TryClickOomNewWindow_without_dialog_returns_false()
    {
        var clicked = IdeIgniteNativeDialogs.TryClickOomNewWindow();
        Assert.False(clicked);
    }
}
