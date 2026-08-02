#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public sealed class CitizenGlassDialogBridgeTests : IDisposable
{
    readonly string _root;

    public CitizenGlassDialogBridgeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-citizen-glass-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CitizenGlassDialogBridge.RootOverrideForTests = _root;
        CideIntercomVoiceLatch.RootOverrideForTests = _root;
        CitizenGlassDialogBridge.TurnOverrideForTests = body => (true, "citizen-echo:" + body, null);
        // Reset processed-id between tests via Stop + fresh override path.
        CitizenGlassDialogBridge.Stop();
        CitizenGlassDialogBridge.ResetProcessedForTests();
    }

    public void Dispose()
    {
        CitizenGlassDialogBridge.Stop();
        CitizenGlassDialogBridge.TurnOverrideForTests = null;
        CitizenGlassDialogBridge.RootOverrideForTests = null;
        CideIntercomVoiceLatch.RootOverrideForTests = null;
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            /* ignore */
        }
    }

    [Fact]
    public void TryProcessOnce_pending_publishes_citizen_intercom_and_marks_done()
    {
        var id = "abc123def456"[..12];
        var req = new
        {
            schema = CitizenGlassDialogBridge.Schema,
            id,
            body = "привет citizen",
            status = "pending",
            stamped_utc = DateTimeOffset.UtcNow
        };
        File.WriteAllText(
            CitizenGlassDialogBridge.RequestPath,
            JsonSerializer.Serialize(req, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));

        Assert.True(CitizenGlassDialogBridge.TryProcessOnce());

        using var latch = JsonDocument.Parse(File.ReadAllText(CideIntercomVoiceLatch.LatchPath));
        Assert.Equal("citizen-echo:привет citizen", latch.RootElement.GetProperty("body").GetString());
        Assert.Equal(CideIntercomVoiceLatch.KindCitizen, latch.RootElement.GetProperty("kind").GetString());
        Assert.Equal(CideIntercomVoiceLatch.DefaultNameCitizen, latch.RootElement.GetProperty("name").GetString());
        Assert.Equal("pf", latch.RootElement.GetProperty("from_seat").GetString());
        Assert.Equal("pm", latch.RootElement.GetProperty("to_seat").GetString());

        using var status = JsonDocument.Parse(File.ReadAllText(CitizenGlassDialogBridge.RequestPath));
        Assert.Equal("done", status.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void TryProcessOnce_skips_done_and_duplicate_id()
    {
        var id = "dupid0000001";
        WritePending(id, "once");
        Assert.True(CitizenGlassDialogBridge.TryProcessOnce());
        Assert.False(CitizenGlassDialogBridge.TryProcessOnce());

        // Same id again while LastProcessedId still set — must not re-run.
        WritePending(id, "again-same-id", resetProcessed: false);
        Assert.False(CitizenGlassDialogBridge.TryProcessOnce());
    }

    [Fact]
    public void TryProcessOnce_turn_fail_marks_error_without_unread_for_pf()
    {
        CitizenGlassDialogBridge.TurnOverrideForTests = _ => (false, null, "boom");
        WritePending("errid0000001", "nope");
        Assert.True(CitizenGlassDialogBridge.TryProcessOnce());

        using var status = JsonDocument.Parse(File.ReadAllText(CitizenGlassDialogBridge.RequestPath));
        Assert.Equal("error", status.RootElement.GetProperty("status").GetString());
        Assert.Equal("boom", status.RootElement.GetProperty("error").GetString());
        Assert.Null(CideIntercomVoiceLatch.TryUnreadForPf());
    }

    void WritePending(string id, string body, bool resetProcessed = true)
    {
        var req = new
        {
            schema = CitizenGlassDialogBridge.Schema,
            id,
            body,
            status = "pending",
            stamped_utc = DateTimeOffset.UtcNow
        };
        File.WriteAllText(
            CitizenGlassDialogBridge.RequestPath,
            JsonSerializer.Serialize(req, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
        if (resetProcessed)
            CitizenGlassDialogBridge.ResetProcessedForTests();
    }
}
