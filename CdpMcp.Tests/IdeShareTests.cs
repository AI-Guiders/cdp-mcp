using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeShareTests
{
    [Fact]
    public void ResolveShareInbox_prefers_project_dot_cdp_share()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-share-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var inbox = IdeShare.ResolveShareInbox(root, null);
            Assert.Equal(Path.Combine(root, ".cdp", "share"), inbox);
            var shelf = IdeShare.ResolveShareInbox(root, null, "self");
            Assert.Equal(Path.Combine(root, ".cdp", "share-self"), shelf);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void SharePut_ShareFrom_roundtrip_self_shelf()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-shelf-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["what"] = JsonSerializer.SerializeToElement("note"),
                ["title"] = JsonSerializer.SerializeToElement("solo-flight"),
                ["dir"] = JsonSerializer.SerializeToElement(Path.Combine(root, "shelf"))
            };
            var putJson = IdeShare.SharePut(root, "self", "# hello shelf\naxis=1\n", args);
            using (var putDoc = JsonDocument.Parse(putJson))
            {
                Assert.True(putDoc.RootElement.GetProperty("ok").GetBoolean());
                Assert.Equal("self", putDoc.RootElement.GetProperty("with").GetString());
                Assert.Equal("shelved", putDoc.RootElement.GetProperty("status").GetString());
            }

            var fromArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["from"] = JsonSerializer.SerializeToElement("self"),
                ["dir"] = JsonSerializer.SerializeToElement(Path.Combine(root, "shelf"))
            };
            var takeJson = IdeShare.ShareFrom(root, fromArgs);
            using var takeDoc = JsonDocument.Parse(takeJson);
            Assert.True(takeDoc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("taken", takeDoc.RootElement.GetProperty("status").GetString());
            Assert.Contains("hello shelf", takeDoc.RootElement.GetProperty("body").GetString());
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Repl_share_from_self_routes_go_share()
    {
        var empty = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var applied = IdeRepl.Apply("share from self", empty);
        Assert.NotNull(applied);
        Assert.True(applied.Value.Args.TryGetValue("go", out var go));
        Assert.Equal("share", go.GetString());
        Assert.True(applied.Value.Args.TryGetValue("go_args", out var ga));
        Assert.Equal("self", ga.GetProperty("from").GetString());
    }

    [Fact]
    public void Repl_share_with_self_body_routes_go_share()
    {
        var empty = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var applied = IdeRepl.Apply("share with self cockpit fixes peel-0", empty);
        Assert.NotNull(applied);
        Assert.True(applied.Value.Args.TryGetValue("go", out var go));
        Assert.Equal("share", go.GetString());
        Assert.True(applied.Value.Args.TryGetValue("go_args", out var ga));
        Assert.Equal("self", ga.GetProperty("with").GetString());
        Assert.Equal("cockpit fixes peel-0", ga.GetProperty("body").GetString());
    }

    [Fact]
    public void Repl_share_buffer_routes_go_share()
    {
        var empty = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var applied = IdeRepl.Apply("share with operator", empty);
        Assert.NotNull(applied);
        Assert.Null(applied.Value.Direct);
        Assert.True(applied.Value.Args.TryGetValue("go", out var go));
        Assert.Equal("share", go.GetString());
        Assert.True(applied.Value.Args.TryGetValue("go_args", out var ga));
        Assert.Equal(JsonValueKind.Object, ga.ValueKind);
        Assert.Equal("operator", ga.GetProperty("with").GetString());
        Assert.Equal("buffer", ga.GetProperty("what").GetString());
    }

    [Fact]
    public void Repl_share_plan_routes_tm_share()
    {
        var empty = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var applied = IdeRepl.Apply("share plan ask=confirm", empty);
        Assert.NotNull(applied);
        Assert.Null(applied.Value.Direct);
        Assert.True(applied.Value.Args.TryGetValue("go", out var go));
        Assert.Equal("plan", go.GetString());
        Assert.True(applied.Value.Args.TryGetValue("tm_op", out var tm));
        Assert.Equal("share", tm.GetString());
        Assert.True(applied.Value.Args.TryGetValue("go_args", out var ga));
        Assert.Equal("plan", ga.GetProperty("what").GetString());
        Assert.Equal("confirm", ga.GetProperty("ask").GetString());
    }

    [Fact]
    public void Repl_share_report_routes_tm_report()
    {
        var empty = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var applied = IdeRepl.Apply("share report", empty);
        Assert.NotNull(applied);
        Assert.Null(applied.Value.Direct);
        Assert.True(applied.Value.Args.TryGetValue("go", out var go));
        Assert.Equal("plan", go.GetString());
        Assert.True(applied.Value.Args.TryGetValue("tm_op", out var tm));
        Assert.Equal("report", tm.GetString());
        Assert.True(applied.Value.Args.TryGetValue("go_args", out var ga));
        Assert.Equal("report", ga.GetProperty("what").GetString());
        Assert.Equal("none", ga.GetProperty("ask").GetString());
    }

    [Fact]
    public void Repl_share_digest_alias_routes_tm_report()
    {
        var empty = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var applied = IdeRepl.Apply("share digest", empty);
        Assert.NotNull(applied);
        Assert.True(applied.Value.Args.TryGetValue("tm_op", out var tm));
        Assert.Equal("report", tm.GetString());
    }

    [Fact]
    public void ResolveOperatorInboxes_habitat_then_project()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-op-inboxes-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var list = IdeShare.ResolveOperatorInboxes(root);
            Assert.Equal(2, list.Count);
            Assert.Contains(Path.Combine("cdp-mcp", "share"), list[0], StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Path.Combine(root, ".cdp", "share"), list[1]);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void WriteOperatorShareFiles_mirrors_project_inbox()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-op-write-" + Guid.NewGuid().ToString("N"));
        var stampName = "note-axb-" + Guid.NewGuid().ToString("N")[..8] + ".md";
        try
        {
            Directory.CreateDirectory(root);
            var written = IdeShare.WriteOperatorShareFiles(
                root,
                dirOverride: null,
                fileName: stampName,
                body: "# hello operator\n",
                metaForPath: p => new { schema = "share/v1", path = p, with = "operator", status = "shared" });
            Assert.True(File.Exists(written.Path));
            Assert.True(File.Exists(written.LatestJson));
            Assert.Contains("cdp-mcp", written.Inbox, StringComparison.OrdinalIgnoreCase);
            var projectLatest = Path.Combine(root, ".cdp", "share", "LATEST.json");
            Assert.True(File.Exists(projectLatest), "project .cdp/share must mirror");
            using var doc = JsonDocument.Parse(File.ReadAllText(projectLatest));
            Assert.Equal("share/v1", doc.RootElement.GetProperty("schema").GetString());
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
            try
            {
                var habitatShare = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "cdp-mcp", "share");
                var junk = Path.Combine(habitatShare, stampName);
                if (File.Exists(junk)) File.Delete(junk);
                // leave LATEST.* — live dogfood may overwrite next
            }
            catch { /* ignore */ }
        }
    }
}
