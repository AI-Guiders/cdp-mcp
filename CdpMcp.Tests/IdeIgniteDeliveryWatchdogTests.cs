using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeIgniteDeliveryWatchdogTests
{
    [Fact]
    public void ScanTranscriptsForNeedle_finds_canonical_charge()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-ignite-wd-" + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(root, "proj", "agent-transcripts", "abcd");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "abcd.jsonl");
        var needle = IdeIgniteChannel.CanonicalComposerCharge;
        File.WriteAllText(path,
            "{\"role\":\"user\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"" + needle + "\"}]}}\n");

        try
        {
            var hit = IdeIgniteArmHost.ScanTranscriptsForNeedle(
                needle, DateTimeOffset.UtcNow.AddMinutes(-5), root, maxFiles: 10);
            Assert.True(hit.Observed);
            Assert.Equal(path, hit.Path);
            Assert.True(hit.Scanned >= 1);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Verdict_prefers_transcript_observed()
    {
        var arm = new IdeIgniteArmHost.IgniteArm
        {
            Id = "t",
            Status = "awaiting",
            SendOk = true,
            FiredUtc = DateTimeOffset.UtcNow,
            TranscriptObservedUtc = DateTimeOffset.UtcNow
        };
        Assert.Equal("transcript_observed", IdeIgniteArmHost.Verdict(arm));
    }

    [Fact]
    public void Verdict_not_observed_when_send_ok_without_transcript()
    {
        var arm = new IdeIgniteArmHost.IgniteArm
        {
            Id = "t",
            Status = "awaiting",
            SendOk = true,
            FiredUtc = DateTimeOffset.UtcNow
        };
        Assert.Equal("not_observed", IdeIgniteArmHost.Verdict(arm));
    }
}
