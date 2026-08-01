#nullable enable
using System.Text.Json;
using System.Text.Json.Nodes;
using Cdp.Core;
using Cdp.Evidence;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>Meta tool switch peeled from Program.DispatchMetaAsync (soft-warn).</summary>
internal static partial class MetaDispatch
{
    public static async Task<string> DispatchAsync(
        MetaDispatchDeps d,
        string name,
        IReadOnlyDictionary<string, JsonElement> callArgs,
        CancellationToken cancellationToken,
        object? warm = null)
    {
        var hit = await CoreAsync(d, name, callArgs, cancellationToken, warm).ConfigureAwait(false);
        if (hit is not null) return hit;
        hit = await IdeAsync(d, name, callArgs, cancellationToken, warm).ConfigureAwait(false);
        if (hit is not null) return hit;
        hit = await HubAsync(d, name, callArgs, cancellationToken, warm).ConfigureAwait(false);
        if (hit is not null) return hit;
        hit = await HubCsxAsync(d, name, callArgs, cancellationToken, warm).ConfigureAwait(false);
        if (hit is not null) return hit;
        hit = await HubShellAsync(d, name, callArgs, cancellationToken, warm).ConfigureAwait(false);
        if (hit is not null) return hit;
        throw new ArgumentException($"Unknown meta tool: {name}");
    }

    static TerminalMcp.Core.ShellCwdDefaults ShellDefaults(SessionContext s) => new()
    {
        ProjectRoot = s.ProjectRoot,
        ScmRoot = s.ScmRoot
    };

    static object FacetCap(MemoryFacetSettings f) => new { enabled = f.Enabled, roots = f.Roots };
    static object ToggleCap(MemoryToggleSettings t) => new { enabled = t.Enabled };

    static string? OptionalPath(IReadOnlyDictionary<string, JsonElement> callArgs)
    {
        if (callArgs.TryGetValue("path", out var p) && p.GetString() is { Length: > 0 } path)
            return path;
        if (callArgs.TryGetValue("solution_path", out var s) && s.GetString() is { Length: > 0 } sol)
            return sol;
        return null;
    }

    static (ScriptToolBus bus, PlanContext plan) PackageSession(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> callArgs)
    {
        _ = callArgs;
        var root = session.ProjectRoot is { Length: > 0 } pr
            ? pr
            : Environment.CurrentDirectory;
        var plan = new PlanContext
        {
            PrimaryRoot = root,
            WorkRoot = root,
            PlanId = "",
            SolutionOrProjectPath = session.SolutionOrProjectPath ?? session.TsConfigPath,
            Language = session.Language
        };
        ProjectSettingsLoader.Hydrate(plan);
        var bus = new ScriptToolBus { IsDryRun = false };
        return (bus, plan);
    }

    static async Task<string> ResolveCsxSourceAsync(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> callArgs) =>
        await IdeCsxSource.ResolveAsync(
            callArgs,
            session.ProjectRoot,
            session.SolutionOrProjectPath).ConfigureAwait(false);

    static string AttachShellEvidence(JsonSerializerOptions pretty, string json, SessionContext s)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var failed = (root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.False)
                || (root.TryGetProperty("exit_code", out var exEl) && exEl.TryGetInt32(out var code) && code != 0);
            if (!failed)
                return json;

            var stdout = root.TryGetProperty("stdout", out var so) ? so.GetString() ?? "" : "";
            var stderr = root.TryGetProperty("stderr", out var se) ? se.GetString() ?? "" : "";
            var text = (stdout + "\n" + stderr).Trim();
            if (text.Length == 0)
                return json;

            var evidence = EvidencePreprocess.Project(
                "shell",
                text,
                new EvidenceContext(ProjectRoot: s.ProjectRoot, SolutionOrProjectPath: s.SolutionOrProjectPath));
            if (evidence.ItemCount == 0)
                return json;

            var node = JsonNode.Parse(json)!.AsObject();
            node["evidence"] = JsonNode.Parse(EvidencePreprocess.ToJson(evidence));
            return node.ToJsonString(pretty);
        }
        catch
        {
            return json;
        }
    }
}
