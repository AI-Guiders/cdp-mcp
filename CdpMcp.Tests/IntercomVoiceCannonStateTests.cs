#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public class IntercomVoiceCannonStateTests : IDisposable
{
    readonly string _root;

    public IntercomVoiceCannonStateTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-cannon-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        IntercomVoiceCannonState.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        IntercomVoiceCannonState.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* temp */ }
    }

    [Fact]
    public void TryMarkFired_once_then_WasFired()
    {
        Assert.False(IntercomVoiceCannonState.WasFired("abc123"));
        Assert.True(IntercomVoiceCannonState.TryMarkFired("abc123"));
        Assert.True(IntercomVoiceCannonState.WasFired("abc123"));
        Assert.False(IntercomVoiceCannonState.TryMarkFired("abc123"));
        Assert.True(File.Exists(IntercomVoiceCannonState.StatePath));
    }

    [Fact]
    public void ArmIdFor_stable_prefix()
    {
        Assert.Equal("intercom-pf-deadbeef", IntercomVoiceCannonState.ArmIdFor("deadbeef"));
    }

    [Fact]
    public void Cap_keeps_last_MaxIds()
    {
        for (var i = 0; i < IntercomVoiceCannonState.MaxIds + 5; i++)
            Assert.True(IntercomVoiceCannonState.TryMarkFired("id" + i));

        Assert.False(IntercomVoiceCannonState.WasFired("id0"));
        Assert.True(IntercomVoiceCannonState.WasFired("id" + (IntercomVoiceCannonState.MaxIds + 4)));

        using var json = JsonDocument.Parse(File.ReadAllText(IntercomVoiceCannonState.StatePath));
        Assert.Equal(IntercomVoiceCannonState.MaxIds, json.RootElement.GetProperty("fired_ids").GetArrayLength());
    }
}
