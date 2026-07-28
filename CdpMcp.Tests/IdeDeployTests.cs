using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeDeployTests
{
    [Fact]
    public void ResolveTarget_hard_defaults_to_sibling()
    {
        var d = IdeDeploy.ResolveTarget(
            IdeDeploy.ReleaseTarget,
            "cdp",
            targetRaw: null,
            mode: "hard",
            force: false);
        Assert.True(d.Ok);
        Assert.Equal(IdeDeploy.DebugTarget, d.Target);
        Assert.Equal(IdeDeploy.DebugTarget, d.Sibling);
    }

    [Fact]
    public void ResolveTarget_hard_self_refused_without_force()
    {
        var d = IdeDeploy.ResolveTarget(
            IdeDeploy.DebugTarget,
            "cdp-debug",
            targetRaw: "self",
            mode: "hard",
            force: false);
        Assert.False(d.Ok);
        Assert.Equal("refuse_hard_self", d.Error);
    }

    [Fact]
    public void ResolveTarget_hard_self_allowed_with_force()
    {
        var d = IdeDeploy.ResolveTarget(
            IdeDeploy.DebugTarget,
            "cdp-debug",
            targetRaw: "self",
            mode: "hard",
            force: true);
        Assert.True(d.Ok);
        Assert.Equal(IdeDeploy.DebugTarget, d.Target);
    }

    [Fact]
    public void ResolveScript_finds_repo_script_from_project_root()
    {
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", ".."));
        while (root is not null
               && !File.Exists(Path.Combine(root, "CdpMcp.csproj"))
               && Directory.GetParent(root) is { } parent)
            root = parent.FullName;

        Assert.NotNull(root);
        Assert.True(File.Exists(Path.Combine(root, "publish-and-deploy.ps1")));
        var session = new SessionContext { ProjectRoot = root };
        var script = IdeDeploy.ResolveScript(session, null);
        Assert.NotNull(script);
        Assert.True(File.Exists(script));
    }

    [Fact]
    public void DeskWarm_skips_explicit_open_path()
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["path"] = JsonSerializer.SerializeToElement(@"D:\tmp\foo")
        };
        Assert.True(DeskWarm.ShouldSkipForTool("cdp_open", args));
        Assert.False(DeskWarm.ShouldSkipForTool("cdp_build", args));
        Assert.True(DeskWarm.ShouldSkipForTool("cdp_restore", args));
    }

    [Fact]
    public void Remount_explain_cards_use_desk_remount_source()
    {
        var auto = IdeExplainability.New(
            "desk.remount",
            "auto_warm",
            "cold tool cdp_session hydrated desk from bookmark once/process",
            "cdp_cockpit");
        var fail = IdeExplainability.New(
            "desk.remount",
            "auto_warm_failed",
            "cold auto-warm failed: boom",
            "cdp_restore");
        var restore = IdeExplainability.New(
            "desk.remount",
            "explicit_restore",
            "desk bookmark restored project + buffers after MCP reload (not LLM chat)",
            "cdp_cockpit");

        using var autoObj = JsonDocument.Parse(JsonSerializer.Serialize(IdeExplainability.ToObject(auto)));
        Assert.Equal("desk.remount", autoObj.RootElement.GetProperty("source").GetString());
        Assert.Equal("auto_warm", autoObj.RootElement.GetProperty("reason").GetString());
        Assert.Contains("desk.remount · auto_warm", auto.WhyLine, StringComparison.Ordinal);
        Assert.Contains("desk.remount · auto_warm_failed", fail.WhyLine, StringComparison.Ordinal);
        Assert.Contains("desk.remount · explicit_restore", restore.WhyLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Repl_deploy_dry_routes_go_deploy()
    {
        var empty = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var applied = IdeRepl.Apply("deploy dry sibling", empty);
        Assert.NotNull(applied);
        Assert.True(applied.Value.Args.TryGetValue("go", out var go));
        Assert.Equal("deploy", go.GetString());
        Assert.True(applied.Value.Args.TryGetValue("go_args", out var ga));
        Assert.Equal("hard", ga.GetProperty("mode").GetString());
        Assert.Equal("sibling", ga.GetProperty("target").GetString());
        Assert.True(ga.GetProperty("dry_run").GetBoolean());
    }
}
