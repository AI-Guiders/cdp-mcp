using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

var exe = args.ElementAtOrDefault(0) ?? @"D:\cdp-mcp\CdpMcp.exe";
var config = args.ElementAtOrDefault(1) ?? @"D:\cdp-mcp\cdp-mcp.toml";

var transport = new StdioClientTransport(new StdioClientTransportOptions
{
    Name = "cdp-probe",
    Command = exe,
    Arguments = ["--config", config],
});

await using var client = await McpClient.CreateAsync(transport);
var tools = await client.ListToolsAsync();
var names = tools.Select(t => t.Name).ToArray();
var meta = names.Where(n => n.StartsWith("cdp_", StringComparison.Ordinal)).ToArray();
var domain = names.Where(n => !n.StartsWith("cdp_", StringComparison.Ordinal)).ToArray();

async Task<string> Call(string name, Dictionary<string, object?> arguments)
{
    var result = await client.CallToolAsync(name, arguments);
    return string.Join("\n", result.Content.OfType<TextContentBlock>().Select(t => t.Text));
}

var health = await Call("cdp_health", new()
{
    ["explain_tool"] = "roslyn_roslyn_get_diagnostics"
});
var session = await Call("cdp_session", new()
{
    ["include_debug"] = false,
    ["include_pack"] = true,
    ["pack_id"] = "epistemic-scene",
    ["explain_tool"] = "memory_task_task_upsert"
});
var packDef = await Call("memory_world_get_definition", new()
{
    ["definition_id"] = "debug-radius",
    ["pack_id"] = "epistemic-scene"
});
var blast = await Call("memory_world_get_definition", new()
{
    ["definition_id"] = "blast-radius",
    ["pack_id"] = "epistemic-scene"
});
var gate = await Call("memory_world_radius_gate_check", new()
{
    ["delta_radius"] = -1,
    ["open_hypothesis_count"] = 2
});
var caps = await Call("cdp_capabilities", new());
var roslynPing = await Call("roslyn_roslyn_ping", new());
var gitStatus = await Call("git_git_status", new()
{
    ["workspace_path"] = @"d:\Experiments\Personal Cursor Folder"
});
var hciVersion = await Call("codebase_index_codebase_index_version", new());
var anuiAdapters = await Call("anui_anui_list_adapters", new());
var shortlist = await Call("cdp_tools", new()
{
    ["phase"] = "recall",
    ["object"] = "kb",
    ["intent"] = "cite"
});
var ctx = await Call("cdp_context", new()
{
    ["phase"] = "act",
    ["object"] = "task",
    ["intent"] = "change"
});
var tools2 = await client.ListToolsAsync();
var names2 = tools2.Select(t => t.Name).ToArray();

var csxCheck = await Call("cdp_csx_check", new()
{
    ["code"] = """
        var p = await Roslyn.PingAsync();
        return p;
        """
});
var csxRun = await Call("cdp_csx_run", new()
{
    ["mode"] = "run",
    ["code"] = """
        var p = await Roslyn.PingAsync();
        var g = await Git.StatusAsync(@"d:\Experiments\Personal Cursor Folder");
        return Mutate.Submit();
        """
});
var csxDry = await Call("cdp_csx_run", new()
{
    ["mode"] = "dry_run",
    ["code"] = """
        await Debug.SetBreakpointOnLineAsync(@"d:\tmp", @"d:\tmp\app.dll", @"d:\tmp\Program.cs", 10);
        return Mutate.Submit();
        """
});

var execConfigPath = Path.Combine(Path.GetTempPath(), "cdp-exec-probe-" + Guid.NewGuid().ToString("N")[..8] + ".toml");
await File.WriteAllTextAsync(execConfigPath, """
[steps.ping]
executable = "git"
arguments = ["--version"]
""");
var csxExec = await Call("cdp_csx_run", new()
{
    ["mode"] = "run",
    ["code"] = $$"""
        ExecEnvironment.Configuration.Set(@"{{execConfigPath}}");
        await Execution.PreConditionAsync("ping");
        await Execution.PostConditionAsync("ping");
        return Mutate.Submit();
        """
});
try { File.Delete(execConfigPath); } catch { /* temp */ }

// E2E: temp git repo → run_plan writes in worktree → primary clean → discard
var e2eRoot = Path.Combine(Path.GetTempPath(), "cdp-csx-e2e-" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(e2eRoot);
static void Git(string cwd, params string[] a)
{
    var psi = new System.Diagnostics.ProcessStartInfo
    {
        FileName = "git",
        WorkingDirectory = cwd,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        RedirectStandardInput = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    foreach (var x in a) psi.ArgumentList.Add(x);
    using var p = System.Diagnostics.Process.Start(psi)!;
    p.StandardInput.Close();
    var so = p.StandardOutput.ReadToEndAsync();
    var se = p.StandardError.ReadToEndAsync();
    if (!p.WaitForExit(30_000))
    {
        try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
        throw new TimeoutException($"git {string.Join(' ', a)} timed out");
    }
    var err = se.GetAwaiter().GetResult();
    _ = so.GetAwaiter().GetResult();
    if (p.ExitCode != 0)
        throw new InvalidOperationException($"git {string.Join(' ', a)} → {p.ExitCode}: {err}");
}
Git(e2eRoot, "init");
Git(e2eRoot, "config", "user.email", "cdp-probe@local");
Git(e2eRoot, "config", "user.name", "cdp-probe");
await File.WriteAllTextAsync(Path.Combine(e2eRoot, "seed.txt"), "seed\n");
Git(e2eRoot, "add", "seed.txt");
Git(e2eRoot, "commit", "-m", "seed");
var marker = "csx-worktree-marker-" + Guid.NewGuid().ToString("N")[..6];
var planRun = await Call("cdp_csx_run_plan", new()
{
    ["workspace_path"] = e2eRoot,
    ["code"] = $$"""
        await Mutate.Fs.WriteTextAsync("from-csx.txt", "{{marker}}");
        return Mutate.Submit();
        """
});
using var planDoc = JsonDocument.Parse(planRun);
var planRoot = planDoc.RootElement;
var planId = planRoot.TryGetProperty("PlanId", out var pidEl) ? pidEl.GetString() : null;
var workRoot = planRoot.TryGetProperty("WorkRoot", out var wrEl) ? wrEl.GetString() : null;
var primaryCleanAfterPlan = planRoot.TryGetProperty("PrimaryClean", out var pcEl) && pcEl.ValueKind == JsonValueKind.True;
var workHasFile = workRoot is not null && File.Exists(Path.Combine(workRoot, "from-csx.txt"));
var primaryHasFileAfterPlan = File.Exists(Path.Combine(e2eRoot, "from-csx.txt"));
var discardOut = planId is { Length: > 0 }
    ? await Call("cdp_csx_discard", new() { ["plan_id"] = planId })
    : "{\"Ok\":false,\"Error\":\"no plan_id\"}";
var primaryHasFileAfterDiscard = File.Exists(Path.Combine(e2eRoot, "from-csx.txt"));
var e2eOk = planRun.Contains("\"Ok\": true", StringComparison.Ordinal)
    && primaryCleanAfterPlan
    && workHasFile
    && !primaryHasFileAfterPlan
    && discardOut.Contains("\"Ok\": true", StringComparison.Ordinal)
    && !primaryHasFileAfterDiscard
    && names.Contains("cdp_csx_run_plan")
    && names.Contains("cdp_csx_promote")
    && names.Contains("cdp_csx_discard");
try { Directory.Delete(e2eRoot, recursive: true); } catch { /* temp */ }

var packOk = packDef.Contains("debug-radius", StringComparison.Ordinal)
    && blast.Contains("blast-radius", StringComparison.Ordinal)
    && gate.Contains("\"ok\": true", StringComparison.Ordinal)
    && session.Contains("bug-radius-shrink", StringComparison.Ordinal);
var recallOk = shortlist.Contains("get_definition", StringComparison.Ordinal)
    && !shortlist.Contains("list_knowledge_files", StringComparison.Ordinal);
var roslynOk = caps.Contains("\"roslyn\"", StringComparison.Ordinal)
    && roslynPing.Contains("OK", StringComparison.Ordinal);
var gitOk = caps.Contains("\"git\"", StringComparison.Ordinal)
    && !gitStatus.StartsWith("Error:", StringComparison.Ordinal);
var hciOk = caps.Contains("codebase_index", StringComparison.Ordinal)
    && !hciVersion.StartsWith("Error:", StringComparison.Ordinal);
var anuiOk = caps.Contains("\"anui\"", StringComparison.Ordinal)
    && !anuiAdapters.StartsWith("Error:", StringComparison.Ordinal);
var csxOk = csxCheck.Contains("\"Ok\": true", StringComparison.Ordinal)
    && csxRun.Contains("\"Ok\": true", StringComparison.Ordinal)
    && csxRun.Contains("roslyn_ping", StringComparison.Ordinal)
    && csxDry.Contains("dry_run", StringComparison.Ordinal)
    && csxExec.Contains("\"Ok\": true", StringComparison.Ordinal)
    && csxExec.Contains("pre_condition", StringComparison.Ordinal);
var wireOk = roslynOk && gitOk && hciOk && anuiOk && csxOk && e2eOk;
var ok = meta.Length == 11 && names.Contains("cdp_session") && names.Contains("cdp_csx_run_plan") && names.Length <= 25 && packOk && recallOk && wireOk;
var payload = new
{
    ok,
    pack_ok = packOk,
    recall_ok = recallOk,
    roslyn_ok = roslynOk,
    git_ok = gitOk,
    hci_ok = hciOk,
    anui_ok = anuiOk,
    csx_ok = csxOk,
    e2e_ok = e2eOk,
    server = client.ServerInfo,
    list_tools_count = names.Length,
    meta,
    domain_sample = domain.Take(15).ToArray(),
    after_context_count = names2.Length,
    after_context_has_memory_task_upsert = names2.Contains("memory_task_task_upsert"),
    health,
    session,
    pack_def = packDef,
    blast,
    gate,
    caps,
    roslyn_ping = roslynPing,
    git_status = gitStatus.Length > 400 ? gitStatus[..400] + "…" : gitStatus,
    hci_version = hciVersion.Length > 400 ? hciVersion[..400] + "…" : hciVersion,
    anui_adapters = anuiAdapters.Length > 400 ? anuiAdapters[..400] + "…" : anuiAdapters,
    csx_check = csxCheck.Length > 600 ? csxCheck[..600] + "…" : csxCheck,
    csx_run = csxRun.Length > 800 ? csxRun[..800] + "…" : csxRun,
    csx_dry = csxDry.Length > 600 ? csxDry[..600] + "…" : csxDry,
    csx_exec = csxExec.Length > 800 ? csxExec[..800] + "…" : csxExec,
    e2e_plan = planRun.Length > 900 ? planRun[..900] + "…" : planRun,
    e2e_discard = discardOut.Length > 400 ? discardOut[..400] + "…" : discardOut,
    e2e_work_had_file = workHasFile,
    e2e_primary_had_file_after_plan = primaryHasFileAfterPlan,
    shortlist,
    ctx
};
Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
return ok ? 0 : 1;
