#nullable enable
using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CdpProfileIsolation")]
public sealed class IdeLearnChannelTests
{
    [Fact]
    public void Stash_list_recall_promote_local_fallback()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CDP_PROFILE") ?? "default",
                "default",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var iso = Path.Combine(Path.GetTempPath(), "cdp-learn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(iso);
        CdpProfile.ApplyClientRoots([iso]);
        IdeLearnChannel.Configure(null);
        _ = IdeScopeChannel.Handle(new SessionContext { ProjectRoot = iso }, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement("clear")
        });
        try
        {
            var session = new SessionContext { ProjectRoot = iso };

            using var scene = JsonDocument.Parse(IdeLearnChannel.HandleJson(session));
            Assert.True(scene.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("learn_channel/v0", scene.RootElement.GetProperty("schema").GetString());

            var stash = IdeLearnChannel.Handle(session, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["op"] = JsonSerializer.SerializeToElement("stash"),
                ["title"] = JsonSerializer.SerializeToElement("Lean Canvas = one glance"),
                ["body"] = JsonSerializer.SerializeToElement("TZ concept card should be Lean Canvas shaped."),
                ["topic"] = JsonSerializer.SerializeToElement("sscad-tz"),
                ["tags"] = JsonSerializer.SerializeToElement("lean,tz")
            });
            var stashJson = JsonSerializer.Serialize(stash);
            using var stashDoc = JsonDocument.Parse(stashJson);
            Assert.True(stashDoc.RootElement.GetProperty("ok").GetBoolean(), stashJson);
            var id = stashDoc.RootElement.GetProperty("id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(id));

            var list = IdeLearnChannel.Handle(session, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["op"] = JsonSerializer.SerializeToElement("list"),
                ["topic"] = JsonSerializer.SerializeToElement("sscad-tz")
            });
            using var listDoc = JsonDocument.Parse(JsonSerializer.Serialize(list));
            Assert.True(listDoc.RootElement.GetProperty("ok").GetBoolean());
            Assert.True(listDoc.RootElement.GetProperty("count").GetInt32() >= 1);

            var recall = IdeLearnChannel.Handle(session, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["op"] = JsonSerializer.SerializeToElement("recall"),
                ["id"] = JsonSerializer.SerializeToElement(id)
            });
            using var recallDoc = JsonDocument.Parse(JsonSerializer.Serialize(recall));
            Assert.True(recallDoc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Contains("Lean Canvas", recallDoc.RootElement.GetProperty("markdown").GetString(), StringComparison.Ordinal);

            var promote = IdeLearnChannel.Handle(session, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["op"] = JsonSerializer.SerializeToElement("promote"),
                ["id"] = JsonSerializer.SerializeToElement(id)
            });
            using var promoteDoc = JsonDocument.Parse(JsonSerializer.Serialize(promote));
            Assert.True(promoteDoc.RootElement.GetProperty("ok").GetBoolean(), JsonSerializer.Serialize(promote));
            Assert.Equal("local_fallback", promoteDoc.RootElement.GetProperty("writer").GetString());
            var local = promoteDoc.RootElement.GetProperty("local_path").GetString();
            Assert.True(File.Exists(local), local);
            var mirror = promoteDoc.RootElement.GetProperty("project_mirror").GetString();
            Assert.True(File.Exists(mirror), mirror);
            Assert.Contains("learn ·", IdeLearnChannel.PulseLine(session), StringComparison.Ordinal);
        }
        finally
        {
            IdeLearnChannel.Configure(null);
            CdpProfile.ApplyClientRoots([Path.Combine(Path.GetTempPath(), "cdp-learn-cleanup")]);
            try { Directory.Delete(iso, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Stash_requires_body()
    {
        var iso = Path.Combine(Path.GetTempPath(), "cdp-learn-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(iso);
        CdpProfile.ApplyClientRoots([iso]);
        try
        {
            var session = new SessionContext { ProjectRoot = iso };
            var result = IdeLearnChannel.Handle(session, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["op"] = JsonSerializer.SerializeToElement("stash"),
                ["title"] = JsonSerializer.SerializeToElement("no body")
            });
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("need_body", doc.RootElement.GetProperty("error").GetString());
        }
        finally
        {
            CdpProfile.ApplyClientRoots([Path.Combine(Path.GetTempPath(), "cdp-learn-cleanup2")]);
            try { Directory.Delete(iso, recursive: true); } catch { /* best-effort */ }
        }
    }
}
