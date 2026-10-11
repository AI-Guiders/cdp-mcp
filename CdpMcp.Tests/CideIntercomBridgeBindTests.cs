using Xunit;

namespace CdpMcp.Tests;

[Collection(nameof(IntercomLatchSerial))]
public class CideIntercomBridgeBindTests : IDisposable
{
    readonly string _root;

    public CideIntercomBridgeBindTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-icm-bridge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CideIntercomBridgeBind.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CideIntercomBridgeBind.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Bind_then_Resolve_returns_last_row_per_bridge()
    {
        CideIntercomBridgeBind.Bind("b1", "ses_old", "Штиль");
        CideIntercomBridgeBind.Bind("b1", "ses_new", "Штиль");

        var (session, nick) = CideIntercomBridgeBind.Resolve("b1");
        Assert.Equal("ses_new", session);
        Assert.Equal("Штиль", nick);
    }

    [Fact]
    public void Resolve_isolates_bridges()
    {
        CideIntercomBridgeBind.Bind("b1", "ses_one", "Один");
        CideIntercomBridgeBind.Bind("b2", "ses_two", "Два");

        Assert.Equal("ses_one", CideIntercomBridgeBind.SessionOf("b1"));
        Assert.Equal("ses_two", CideIntercomBridgeBind.SessionOf("b2"));
        Assert.Null(CideIntercomBridgeBind.SessionOf("b-missing"));
    }

    [Fact]
    public void ResolveCallerSession_prefers_ses_conversation()
    {
        CideIntercomBridgeBind.Bind("b1", "ses_bound", "Штиль");

        Assert.Equal(
            "ses_from_conversation",
            CdpIgniteArmHost.ResolveCallerSession("ses_from_conversation", "b1"));
    }

    [Fact]
    public void ResolveCallerSession_ignores_progress_token_conversation()
    {
        CideIntercomBridgeBind.Bind("b1", "ses_bound", "Штиль");

        Assert.Equal(
            "ses_bound",
            CdpIgniteArmHost.ResolveCallerSession("pt:42", "b1"));
    }

    [Fact]
    public void ResolveCallerSession_null_when_unknown()
    {
        Assert.Null(CdpIgniteArmHost.ResolveCallerSession(null, "b-unknown"));
    }
}
