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
    [InlineData("Keep Waiting", true)]
    [InlineData("&&Keep Waiting", true)]
    [InlineData("&Keep Waiting", true)]
    [InlineData("Reopen", false)]
    [InlineData("Close", false)]
    [InlineData("Wait for the program to respond", false)]
    public void IsKeepWaitingLabel_strips_mnemonic(string label, bool expect) =>
        Assert.Equal(expect, IdeIgniteNativeDialogs.IsKeepWaitingLabel(label));

    [Fact]
    public void TryClickKeepWaiting_without_dialog_returns_false()
    {
        // No stall dialog open — must not throw; typically false.
        var clicked = IdeIgniteNativeDialogs.TryClickKeepWaiting();
        Assert.False(clicked);
    }
}
