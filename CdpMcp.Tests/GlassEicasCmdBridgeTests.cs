#nullable enable
using Xunit;

namespace CdpMcp.Tests;

public sealed class GlassEicasCmdBridgeTests : IDisposable
{
    readonly string _root;

    public GlassEicasCmdBridgeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-eicas-cmd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        GlassEicasCmdBridge.RootOverrideForTests = _root;
        CideEclLatch.RootOverrideForTests = _root;
        GlassEicasCmdBridge.Stop();
        GlassEicasCmdBridge.ResetProcessedForTests();
    }

    public void Dispose()
    {
        GlassEicasCmdBridge.Stop();
        GlassEicasCmdBridge.RootOverrideForTests = null;
        CideEclLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void TryProcessOnce_ack_ecl_marks_done()
    {
        File.WriteAllText(
            GlassEicasCmdBridge.RequestPath,
            """
            {"schema":"glass_eicas_cmd/v0","origin":"glass","id":"abc123","op":"ack_ecl","checklist":"ship","item":"git-known"}
            """.Trim());

        Assert.True(GlassEicasCmdBridge.TryProcessOnce());
        var done = File.ReadAllText(GlassEicasCmdBridge.RequestPath);
        Assert.Contains("done", done, StringComparison.OrdinalIgnoreCase);
        Assert.False(GlassEicasCmdBridge.TryProcessOnce());
    }
}
