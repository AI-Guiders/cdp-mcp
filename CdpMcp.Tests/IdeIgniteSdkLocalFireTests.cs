using System.Reflection;
using Cdp.Config;
using Xunit;
using static CdpMcp.IdeIgniteArmHost;

namespace CdpMcp.Tests;

public sealed class IdeIgniteSdkLocalFireTests : IDisposable
{
    readonly string _root;

    public IdeIgniteSdkLocalFireTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sdk-fire-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        IdeIgniteSdkLocalFire.ResetTestHooks();
        IdeIgniteWakeLatch.BootRefreshEnabled = false;
        IdeIgniteWakeLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        IdeIgniteSdkLocalFire.ResetTestHooks();
        IdeIgniteWakeLatch.BootRefreshEnabled = true;
        IdeIgniteWakeLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    static IgniteArm Arm(Action<IgniteArm>? configure = null)
    {
        var a = new IgniteArm
        {
            Id = "arm-sdk-test",
            Message = "wake charge",
            Harness = "cursor"
        };
        configure?.Invoke(a);
        return a;
    }

    static bool TryGetOk(object result)
    {
        var ok = result.GetType().GetProperty("ok")?.GetValue(result);
        return ok is true;
    }

    static string? TryGetString(object result, string name) =>
        result.GetType().GetProperty(name)?.GetValue(result) as string;

    [Fact]
    public async Task ForceCdt_returns_null()
    {
        IdeIgniteSdkLocalFire.ApiKeyPresentOverride = true;
        IdeIgniteSdkLocalFire.BridgeOverride = (_, _) =>
            Task.FromResult(new IdeIgniteSdkLocalFire.SdkFireResult(true, AgentId: "agent-force"));

        var result = await IdeIgniteSdkLocalFire.TryDeliverAsync(
            Arm(a => a.ForceCdt = true), "hello", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task BridgeOverride_ok_stamps_delivery_path_and_agent_id()
    {
        IdeIgniteSdkLocalFire.BridgeOverride = (_, _) =>
            Task.FromResult(new IdeIgniteSdkLocalFire.SdkFireResult(
                true, AgentId: "agent-bridge-1", Detail: "test_ok"));

        var arm = Arm();
        var result = await IdeIgniteSdkLocalFire.TryDeliverAsync(arm, "hello", CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(TryGetOk(result!));
        Assert.Equal(IdeIgniteSdkLocalFire.DeliveryPathSdkLocal, TryGetString(result!, "delivery_path"));
        Assert.Equal("agent-bridge-1", arm.SdkAgentId);
        Assert.Equal(IdeIgniteSdkLocalFire.DeliveryPathSdkLocal, arm.DeliveryPath);

        var latch = IdeIgniteWakeLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(IdeIgniteWakeLatch.ChannelSdkLocal, latch!.Channel);
        Assert.Equal("agent-bridge-1", latch.SdkAgentId);
    }

    [Fact]
    public async Task ForceUnavailable_returns_null_for_cdt_escape()
    {
        IdeIgniteSdkLocalFire.ForceUnavailableForTests = true;

        var result = await IdeIgniteSdkLocalFire.TryDeliverAsync(Arm(), "hello", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ConfigDisabled_returns_null_for_cdt_escape()
    {
        var prev = CdpOpsConfig.Current;
        CdpOpsConfig.Bind(new CdpConfigDocument
        {
            Ops = new CdpConfigOpsSection { CursorSdkLocal = false }
        });
        try
        {
            IdeIgniteSdkLocalFire.ApiKeyPresentOverride = true;

            var result = await IdeIgniteSdkLocalFire.TryDeliverAsync(Arm(), "hello", CancellationToken.None);

            Assert.Null(result);
        }
        finally
        {
            CdpOpsConfig.RestoreForTests(prev);
        }
    }

    [Fact]
    public void IsHabitatSubmitKind_includes_sdk_local()
    {
        var host = Default;
        var method = typeof(CdpIgniteArmHost).GetMethod(
            "IsHabitatSubmitKind",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.True((bool)method!.Invoke(host, [IdeIgniteSdkLocalFire.SubmitKindSdkLocal])!);
    }

    [Fact]
    public void NormalizeChannel_maps_sdk_aliases()
    {
        Assert.Equal(IdeIgniteWakeLatch.ChannelSdkLocal, IdeIgniteWakeLatch.NormalizeChannel("sdk_local"));
        Assert.Equal(IdeIgniteWakeLatch.ChannelSdkLocal, IdeIgniteWakeLatch.NormalizeChannel("sdk"));
        Assert.Equal(IdeIgniteWakeLatch.ChannelSdkLocal, IdeIgniteWakeLatch.NormalizeChannel("cursor_sdk"));
    }
}
