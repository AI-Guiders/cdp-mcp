using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public class IdeShipChannelTests
{
    [Fact]
    public void Pulse_without_scm_reports_no_scm()
    {
        var session = new SessionContext();
        var json = IdeShipChannel.HandleJson(session, new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("pulse")
        });

        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("no_scm", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void Run_requires_message_without_slices()
    {
        var session = new SessionContext { ScmRoot = Path.GetTempPath(), ProjectRoot = Path.GetTempPath() };
        var json = IdeShipChannel.HandleJson(session, null);

        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("message_required", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void Dry_run_validate_with_override()
    {
        var session = new SessionContext { ScmRoot = @"C:\repo", ProjectRoot = @"C:\repo" };
        var calls = new List<string>();
        IdeShipChannel.GitCallOverride = (tool, args) =>
        {
            calls.Add(tool + ":" + (args.TryGetValue("op", out var op) ? op.GetString() : "?"));
            if (tool == "git_plan" && args.TryGetValue("op", out var o) && o.GetString() == "validate")
            {
                return """{"schema":"git_plan/v0","op":"validate","ok":true,"slice_count":1,"slices":[]}""";
            }

            return """{"schema":"git_plan/v0","op":"draft","ok":true,"roots":[{"path":"C:\\repo","ok":true,"dirty":true,"paths":["a.cs"]}]}""";
        };

        try
        {
            var json = IdeShipChannel.HandleJson(session, new Dictionary<string, JsonElement>
            {
                ["message"] = JsonSerializer.SerializeToElement("feat: test ship"),
                ["dry_run"] = JsonSerializer.SerializeToElement(true)
            });

            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), json);
            Assert.Equal("dry_run", doc.RootElement.GetProperty("op").GetString());
            Assert.Contains("git_plan:validate", calls);
        }
        finally
        {
            IdeShipChannel.GitCallOverride = null;
            IdeSettingsStore.Unset(IdeShipChannel.LastKey);
        }
    }

    [Fact]
    public void Apply_push_with_override()
    {
        var session = new SessionContext { ScmRoot = @"C:\repo", ProjectRoot = @"C:\repo" };
        IdeShipChannel.GitCallOverride = (tool, args) =>
        {
            if (tool == "git_plan" && args.TryGetValue("op", out var o) && o.GetString() == "apply")
            {
                Assert.True(args.TryGetValue("push", out var push) && push.GetBoolean());
                return """{"schema":"git_plan/v0","op":"apply","ok":true,"commit":{"ok":true}}""";
            }

            return """{"schema":"git_plan/v0","op":"draft","ok":true,"roots":[{"path":"C:\\repo","ok":true,"dirty":true,"paths":["b.cs"]}]}""";
        };

        try
        {
            var json = IdeShipChannel.HandleJson(session, new Dictionary<string, JsonElement>
            {
                ["message"] = JsonSerializer.SerializeToElement("fix: ship apply"),
                ["push"] = JsonSerializer.SerializeToElement(true)
            });

            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), json);
            Assert.False(doc.RootElement.GetProperty("deploy").GetBoolean());
        }
        finally
        {
            IdeShipChannel.GitCallOverride = null;
            IdeSettingsStore.Unset(IdeShipChannel.LastKey);
        }
    }

    [Fact]
    public void Tool_name_and_schema()
    {
        Assert.Equal("cdp_ship", IdeShipChannel.ToolName);
        Assert.Equal("ship/v1", IdeShipChannel.SchemaVersion);
    }
}
