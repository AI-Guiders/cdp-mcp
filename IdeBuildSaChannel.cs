#nullable enable
using System.Diagnostics;
using System.Text.Json;
using Cdp.Core;
using DotnetDebugMcp;
using GitMcp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=build_desk</c> / Meta <c>cdp_build_sa</c> — agent-native Build-Ship-SA (ADR-0013).
/// Not <c>go=build</c> (actuator) / <c>go=ship</c> (take) and not EICAS <c>go=sa</c>.
/// </summary>
internal static class IdeBuildSaChannel
{
    public const string SchemaVersion = "build_sa/v1";
    public const string ToolName = "cdp_build_sa";

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static string HandleJson(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args) =>
        JsonSerializer.Serialize(Handle(session, args), Pretty);

    public static object Handle(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var depth = NormDepth(Opt(args, "depth") ?? "slim");
        var scope = NormScope(Opt(args, "scope") ?? "session");
        var snap = Capture(session, args);

        var (verdict, why) = Decide(snap, scope);
        var pulse = PulseLine(snap, verdict);

        if (depth == "pulse")
        {
            return new
            {
                ok = true,
                schema = SchemaVersion,
                role = "build_desk",
                go = "build_desk",
                tool = ToolName,
                detail = "pulse",
                pulse,
                verdict,
                why,
                scope,
                next = BuildNext(snap, verdict),
                hint = "depth=slim for dirty/DAP card. go=build = actuator; go=ship ≠ this organ."
            };
        }

        object? dirty = null;
        if (depth == "full" || scope == "ship" || verdict is "ship" or "preflight")
        {
            dirty = snap.Dirty.Take(depth == "full" ? 40 : 12).Select(f => new
            {
                path = f.Path,
                status = f.Status,
                risk = f.Risk,
                why = f.Why
            }).ToArray();
        }

        return new
        {
            ok = true,
            schema = SchemaVersion,
            role = "build_desk",
            go = "build_desk",
            tool = ToolName,
            detail = depth,
            pulse,
            verdict,
            why,
            scope,
            depth,
            build = new
            {
                target = snap.Target,
                target_ok = snap.TargetOk,
                active_dap = snap.ActiveDap,
                stopped = snap.Stopped
            },
            scm = new
            {
                root = snap.ScmRoot,
                branch = snap.Branch,
                dirty = snap.Dirty.Count > 0,
                dirty_count = snap.Dirty.Count,
                secret_hits = snap.SecretHits,
                ahead = snap.Ahead,
                behind = snap.Behind
            },
            dirty_files = dirty,
            next = BuildNext(snap, verdict),
            hint = depth == "full"
                ? "Full dirty list. Act via cdp_build / git_plan / git_push — not shell archaeology."
                : "Slim Build-Ship-SA. depth=full for dirty paths; stop DAP before rebuild."
        };
    }

    static string PulseLine(Snap snap, string verdict)
    {
        var dap = snap.ActiveDap ? (snap.Stopped ? "DAP STOPPED" : "DAP active") : "dap idle";
        return $"build_desk · {verdict} · {dap} · dirty={snap.Dirty.Count} · ahead={snap.Ahead?.ToString() ?? "?"}";
    }

    static (string Verdict, string Why) Decide(Snap snap, string scope)
    {
        if (snap.ScmRoot is not { Length: > 0 } && snap.Target is not { Length: > 0 })
            return ("need_more", "No project/scm — cdp_open before build/ship.");

        if (snap.ActiveDap && scope is "session" or "build")
            return ("stop_rebuild", "DAP holds PDB — debug_stop before cdp_build.");

        if (scope is "session" or "ship")
        {
            if (snap.SecretHits > 0)
                return ("preflight", "Dirty includes secret-risk paths — git_preflight before commit.");
            if (snap.Dirty.Count > 0)
                return ("ship", "Dirty tree — git_plan logical slices (standing allow push after).");
            if (snap.Ahead is > 0)
                return ("push", "Clean but ahead of upstream — git_push when ready.");
        }

        if (scope == "build" || scope == "session")
        {
            if (!snap.TargetOk)
                return ("need_more", "No build target — cdp_open / path=.");
            if (snap.ActiveDap)
                return ("stop_rebuild", "DAP holds PDB — debug_stop before cdp_build.");
            return ("build", "Ready to cdp_build (no last-build cache in v0)." );
        }

        return ("clean", "Clean tree, not ahead — nothing to ship.");
    }

    static object[] BuildNext(Snap snap, string verdict)
    {
        var list = new List<object>();
        switch (verdict)
        {
            case "stop_rebuild":
                list.Add(new { go = "debug_desk", label = "Debug-SA", why = "fuse before stop" });
                list.Add(new { go = "debug", label = "debug_stop", why = "op=stop — release PDB" });
                list.Add(new { go = "build", label = "Rebuild", why = "cdp_build after stop" });
                list.Add(new { go = "qrh", label = "QRH dap-pdb-lock", why = "procedure" });
                break;
            case "preflight":
                list.Add(new { go = "git_scene", label = "Git scene", why = "confirm dirty" });
                list.Add(new { go = "git_draft", label = "git_preflight", why = "exclude secrets" });
                break;
            case "ship":
                list.Add(new { go = "git_draft", label = "git_plan", why = "logical commits" });
                list.Add(new { go = "ecl", label = "ECL ship", why = "checklist" });
                list.Add(new { go = "qrh", label = "QRH ship-dirty", why = "procedure" });
                break;
            case "push":
                list.Add(new { go = "git_scene", label = "Git scene", why = "ahead/behind" });
                list.Add(new { go = "ecl", label = "ECL ship push", why = "standing allow" });
                break;
            case "build":
                list.Add(new { go = "build", label = "cdp_build", why = "session project" });
                list.Add(new { go = "test_desk", label = "Test-SA", why = "after build" });
                break;
            case "clean":
                list.Add(new { go = "git_scene", label = "Git scene", why = "confirm clean" });
                break;
            default:
                list.Add(new { go = "open", label = "cdp_open", why = "root project" });
                break;
        }

        list.Add(new { go = "alert", label = "EICAS", why = "attention SA" });
        return Dedup(list);
    }

    static object[] Dedup(List<object> list)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var outList = new List<object>();
        foreach (var item in list)
        {
            var t = item.GetType();
            var key = (t.GetProperty("label")?.GetValue(item) as string ?? "") + "\0" +
                      (t.GetProperty("why")?.GetValue(item) as string ?? "");
            if (!seen.Add(key)) continue;
            outList.Add(item);
        }

        return outList.ToArray();
    }

    static Snap Capture(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var targetOk = IdeSessionLifecycle.TryResolveTarget(session, args, out var target, out _);
        var scm = session.ScmRoot ?? session.ProjectRoot;
        var dirty = scm is { Length: > 0 }
            ? IdeReviewChannel.ListDirtyFiles(scm)
            : Array.Empty<IdeReviewChannel.FileCard>();
        var secretHits = dirty.Count(d => d.Risk.Equals("secret", StringComparison.OrdinalIgnoreCase));

        string? branch = null;
        int? ahead = null;
        int? behind = null;
        if (scm is { Length: > 0 })
        {
            branch = RunGit(scm, "rev-parse --abbrev-ref HEAD")?.Trim();
            var lr = RunGit(scm, "rev-list --left-right --count @{u}...HEAD");
            if (lr is { Length: > 0 })
            {
                var parsed = GitScene.ParseLeftRightCount(lr);
                if (parsed is { } ab)
                {
                    // left = upstream ahead of us (behind), right = we ahead of upstream
                    // git rev-list --left-right --count @{u}...HEAD → behind\tahead typically
                    // ParseLeftRightCount returns (Ahead, Behind) as (left, right) from docs:
                    // "ahead (left), behind (right)" for A...B — with @{u}...HEAD left=commits on upstream not in HEAD = behind us
                    behind = ab.Ahead;
                    ahead = ab.Behind;
                }
            }
        }

        return new Snap(
            targetOk ? target : null,
            targetOk,
            scm,
            branch,
            DebugSession.CurrentClient is not null,
            DebugSession.LastStoppedThreadId > 0,
            dirty,
            secretHits,
            ahead,
            behind);
    }

    static string? RunGit(string cwd, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = args,
                WorkingDirectory = cwd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var stdout = p.StandardOutput.ReadToEnd();
            _ = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(8000))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return null;
            }

            return p.ExitCode == 0 ? stdout : null;
        }
        catch
        {
            return null;
        }
    }

    static string NormDepth(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "pulse" or "p" => "pulse",
        "full" or "raw" or "deep" => "full",
        _ => "slim"
    };

    static string NormScope(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "build" or "rebuild" => "build",
        "ship" or "scm" or "git" => "ship",
        _ => "session"
    };

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    sealed record Snap(
        string? Target,
        bool TargetOk,
        string? ScmRoot,
        string? Branch,
        bool ActiveDap,
        bool Stopped,
        IReadOnlyList<IdeReviewChannel.FileCard> Dirty,
        int SecretHits,
        int? Ahead,
        int? Behind);
}
