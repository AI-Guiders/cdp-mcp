#nullable enable
using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CdpProfileIsolation")]
public sealed class IdeScopeChannelTests
{
    [Fact]
    public void Set_markers_and_learn_inherits()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CDP_PROFILE") ?? "default",
                "default",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var iso = Path.Combine(Path.GetTempPath(), "cdp-ps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(iso);
        CdpProfile.ApplyClientRoots([iso]);
        IdeLearnChannel.Configure(null);
        try
        {
            var session = new SessionContext { ProjectRoot = iso };

            var set = IdeScopeChannel.Handle(session, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["op"] = JsonSerializer.SerializeToElement("set"),
                ["text"] = JsonSerializer.SerializeToElement("[PRIMARY:cascade-ide] [SCOPE:door-to-singularity]")
            });
            using var setDoc = JsonDocument.Parse(JsonSerializer.Serialize(set));
            Assert.True(setDoc.RootElement.GetProperty("ok").GetBoolean(), JsonSerializer.Serialize(set));
            Assert.Equal("cascade-ide", setDoc.RootElement.GetProperty("primary").GetString());
            Assert.Equal("door-to-singularity", setDoc.RootElement.GetProperty("scope").GetString());
            Assert.Contains("PRIMARY=cascade-ide", IdeScopeChannel.PulseLine(session), StringComparison.Ordinal);

            var stash = IdeLearnChannel.Handle(session, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["op"] = JsonSerializer.SerializeToElement("stash"),
                ["title"] = JsonSerializer.SerializeToElement("from latch"),
                ["body"] = JsonSerializer.SerializeToElement("Should inherit PRIMARY/SCOPE from project_switch.")
            });
            using var stashDoc = JsonDocument.Parse(JsonSerializer.Serialize(stash));
            Assert.True(stashDoc.RootElement.GetProperty("ok").GetBoolean(), JsonSerializer.Serialize(stash));
            Assert.Equal("cascade-ide", stashDoc.RootElement.GetProperty("primary").GetString());
            Assert.Equal("door-to-singularity", stashDoc.RootElement.GetProperty("scope").GetString());

            var clear = IdeScopeChannel.Handle(session, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["op"] = JsonSerializer.SerializeToElement("clear")
            });
            using var clearDoc = JsonDocument.Parse(JsonSerializer.Serialize(clear));
            Assert.True(clearDoc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Null(IdeScopeChannel.CurrentOrNull());
        }
        finally
        {
            IdeLearnChannel.Configure(null);
            CdpProfile.ApplyClientRoots([Path.Combine(Path.GetTempPath(), "cdp-ps-cleanup")]);
            try { Directory.Delete(iso, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Parse_single_bracket_markers()
    {
        IdeScopeChannel.TryParseMarkers("[PRIMARY:cdp-mcp] hello [SCOPE:door-to-singularity]", out var p, out var s);
        Assert.Equal("cdp-mcp", p);
        Assert.Equal("door-to-singularity", s);
    }
}
