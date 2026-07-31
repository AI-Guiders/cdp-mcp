#nullable enable
using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CdpProfileIsolation")]
public sealed class IdePressureMemoLineTests
{
    [Fact]
    public void Stash_appends_memo_line_and_line_returns_tail()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CDP_PROFILE") ?? "default",
                "default",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var iso = $"D:\\tmp\\cdp-pressure-memo-{Guid.NewGuid():N}";
        CdpProfile.ApplyClientRoots([iso]);
        try
        {
            var session = new SessionContext { ProjectRoot = iso, Phase = CdpPhase.Act, Object = CdpObjectKind.Code };

            using var stash1 = JsonDocument.Parse(JsonSerializer.Serialize(
                IdePressureChannel.Handle(session, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["op"] = JsonSerializer.SerializeToElement("stash"),
                    ["body"] = JsonSerializer.SerializeToElement("memo-a · axes intact"),
                    ["plan"] = JsonSerializer.SerializeToElement("anti-compaction")
                })));
            Assert.True(stash1.RootElement.GetProperty("ok").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(stash1.RootElement.GetProperty("memo_id").GetString()));

            _ = IdePressureChannel.Handle(session, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["op"] = JsonSerializer.SerializeToElement("memo"),
                ["body"] = JsonSerializer.SerializeToElement("memo-b · second konspekt")
            });

            // Identical body as last → dedup (count stays 2).
            _ = IdePressureChannel.Handle(session, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["op"] = JsonSerializer.SerializeToElement("memo"),
                ["body"] = JsonSerializer.SerializeToElement("memo-b · second konspekt")
            });

            using var line = JsonDocument.Parse(JsonSerializer.Serialize(
                IdePressureChannel.Handle(session, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["op"] = JsonSerializer.SerializeToElement("line"),
                    ["limit"] = JsonSerializer.SerializeToElement(5)
                })));

            Assert.Equal(2, line.RootElement.GetProperty("total").GetInt32());
            Assert.Equal(2, line.RootElement.GetProperty("count").GetInt32());
            Assert.True(File.Exists(IdePressureChannel.MemoPath));
            Assert.True(File.Exists(IdePressureChannel.MemoLatestMdPath));
        }
        finally
        {
            CdpProfile.ApplyClientRoots(["D:\\tmp\\cdp-pressure-memo-cleanup"]);
            try { Directory.Delete(iso, recursive: true); } catch { /* best-effort */ }
        }
    }
}
