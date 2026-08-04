#nullable enable
using System.Text.Json;
using System.Text.Json.Nodes;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=surface_desk</c> / Meta <c>cdp_glass</c> — agent surface parity RPC to Glass WPF
/// via surface-cmd / surface-reply latches (request/reply).
/// Contract: cascade-ide docs/design/agent-surface-parity-contract-v0.md
/// </summary>
internal static class IdeGlassSurfaceChannel
{
    public const string Schema = "agent_surface/v0";
    public const string ToolName = "cdp_glass";
    public const string GoName = "surface_desk";

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    static readonly HashSet<string> Implemented = new(StringComparer.OrdinalIgnoreCase)
    {
        "scene", "status", "caps", "layout",
        "highlight", "focus", "click", "set_text", "send_keys", "palette", "run", "action",
        "appearance", "colors", "colors_under_cursor",
        "set_control_layout", "set_panel_size", "request_confirmation"
    };

    public static string HandleJson(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args) =>
        JsonSerializer.Serialize(Handle(session, args), Pretty);

    public static object Handle(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        _ = session;
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = (Opt(args, "op") ?? Opt(args, "cmd") ?? "scene").Trim().ToLowerInvariant();
        try
        {
            return op switch
            {
                "scene" or "status" or "caps" => Scene(session),
                "layout" or "highlight" or "focus" or "click" or "set_text" or "send_keys" or "palette"
                    or "run" or "action"
                    or "appearance" or "colors" or "colors_under_cursor"
                    or "set_control_layout" or "set_panel_size" or "request_confirmation"
                    => Rpc(op, args),
                _ => Scene(session)
            };
        }
        catch (Exception ex)
        {
            return new
            {
                schema = Schema,
                ok = false,
                go = GoName,
                tool = ToolName,
                op,
                error = "surface_failed",
                detail = ex.Message
            };
        }
    }

    static object Scene(SessionContext session)
    {
        var plan = CidePlanLatch.TryRead();
        var seats = CideSeatsLatch.TryRead();
        var presentation = CidePresentationLatch.TryRead();
        var ignite = CideIgniteLatch.TryRead();
        var land = TryPeekLand();
        var landPath = TryPeekLandPath();
        var shared = TryPeekShared();
        var alert = TryPeekAlert();
        var next = plan?.Task ?? plan?.Feature;
        var why = plan?.Why ?? IdePressureChannel.CompactWhyLine(IdePressureChannel.TryPeekSealedCourse());
        var course = plan?.Feature;
        var leaf = plan?.Task;
        var mfd = seats?.MfdPage ?? presentation?.MfdPage;
        var fileSitu = BuildFileSitu(landPath, session.ProjectRoot, why, leaf, includeDiff: false);
        var autoi = FormatAutoiFace(ignite);
        var pulse = plan is { Active: true }
            ? $"glass_scene · {Truncate(mfd ?? "cabin", 16)} · NEXT · {Truncate(next, 36)} · Autoi {autoi}"
            : $"glass_scene · cabin SA · Autoi {autoi} · surface RPC ready";

        // Cabin SA omnibus (gap 2.2): same latches Glass paints — agent pulse without PNG ritual.
        var cabin = new
        {
            schema = "cabin_sa/v0",
            why,
            next,
            course,
            feature = plan?.Feature,
            active = plan?.Active ?? false,
            seats = seats?.Seats,
            mfd_page = mfd,
            topology = presentation?.Topology,
            tier = presentation?.Tier,
            land,
            shared,
            ignite = ignite is null
                ? null
                : new
                {
                    active = ignite.Active,
                    autonomous = ignite.Autonomous,
                    hild = ignite.Hild,
                    vad = ignite.Vad,
                    await_partner = ignite.AwaitPartner,
                    mode = ignite.Mode ?? "fly",
                    face = autoi,
                    pulse = ignite.Pulse,
                    course = Truncate(ignite.Course, 120)
                },
            alert,
            file_situ = fileSitu,
            stamped_utc = plan?.StampedUtc ?? seats?.StampedUtc
        };

        return new
        {
            schema = Schema,
            ok = true,
            go = GoName,
            go_alias = "glass_scene",
            tool = ToolName,
            op = "scene",
            pulse,
            cabin,
            // Compat: prior shared_ssot shape (Plan + file_situ). Prefer cabin=.
            shared_ssot = new
            {
                next,
                why,
                feature = plan?.Feature,
                active = plan?.Active ?? false,
                land,
                file_situ = fileSitu,
                stamped_utc = plan?.StampedUtc
            },
            ipc = new
            {
                cmd = GlassSurfaceIpc.CmdPath,
                reply = GlassSurfaceIpc.ReplyPath,
                habitat = GlassSurfaceIpc.StateRoot
            },
            implemented = Implemented.OrderBy(x => x).ToArray(),
            planned = Array.Empty<string>(),
            hint =
                "cabin = glass_scene SA pulse (why/next/course/seats/mfd/land/shared/ignite/alert/file_situ) — no PNG. RPC: op=layout|highlight|focus|click|…. Glass host for drive; cabin works from latches alone."
        };
    }

    static object BuildFileSitu(
        string? editorPath,
        string? workspaceRoot,
        string? why,
        string? leaf,
        bool includeDiff = true)
    {
        if (string.IsNullOrWhiteSpace(editorPath))
        {
            return new
            {
                path = (string?)null,
                why_this_file = (string?)null,
                blast = Array.Empty<string>(),
                role_in_graph = (object?)null,
                diff_intent = (object?)null,
                applies_on_locus = (object?)null
            };
        }

        var whyBits = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(leaf))
            whyBits.Add(Truncate(leaf!.Trim(), 48)!);
        if (!string.IsNullOrWhiteSpace(why))
            whyBits.Add(Truncate(why!.Trim(), 72)!);
        var whyThisFile = whyBits.Count > 0 ? string.Join(" · ", whyBits) : null;

        var blast = CollectSameStemBlast(workspaceRoot, editorPath, max: 3);
        var role = BuildRoleInGraph(workspaceRoot, editorPath);
        // Cabin/glass_scene pulse must stay latch-fast (FDR: sync git ReadToEnd hung 230s).
        var diff = includeDiff
            ? BuildDiffIntent(workspaceRoot, editorPath)
            : new { line = "DIFF · on demand", added = 0, deleted = 0, hunks = 0, clean = true, untracked = false };
        var applies = BuildAppliesOnLocus(editorPath);

        return new
        {
            path = editorPath,
            why_this_file = whyThisFile,
            blast,
            role_in_graph = role,
            diff_intent = diff,
            applies_on_locus = applies
        };
    }

    static object BuildDiffIntent(string? workspaceRoot, string editorPath)
    {
        // Human-face summary only — not raw unified dump (raw_diff_as_primary).
        try
        {
            if (!File.Exists(editorPath))
                return new { line = "", added = 0, deleted = 0, hunks = 0, clean = true, untracked = false };

            var root = FindGitRoot(editorPath) ?? (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot)
                ? Path.GetDirectoryName(editorPath)
                : Path.GetFullPath(workspaceRoot.Trim()));
            if (string.IsNullOrWhiteSpace(root))
                return new { line = "NO-GIT", added = 0, deleted = 0, hunks = 0, clean = true, untracked = false };

            string rel;
            try { rel = Path.GetRelativePath(root, editorPath).Replace('\\', '/'); }
            catch { return new { line = "NO-GIT", added = 0, deleted = 0, hunks = 0, clean = true, untracked = false }; }

            // Lightweight: porcelain + word-count style via git diff --numstat
            var porcelain = RunGit(root, "status", "--porcelain=v1", "--", rel);
            if (porcelain.StartsWith("??", StringComparison.Ordinal))
                return new { line = "UNTRACKED", added = 0, deleted = 0, hunks = 0, clean = false, untracked = true };

            var numstat = RunGit(root, "diff", "HEAD", "--numstat", "--", rel);
            if (string.IsNullOrWhiteSpace(numstat))
                return new { line = "CLEAN", added = 0, deleted = 0, hunks = 0, clean = true, untracked = false };

            var parts = numstat.Trim().Split('\t', StringSplitOptions.RemoveEmptyEntries);
            var added = parts.Length > 0 && int.TryParse(parts[0], out var a) ? a : 0;
            var deleted = parts.Length > 1 && int.TryParse(parts[1], out var d) ? d : 0;
            var line = $"+{added} −{deleted}";
            return new { line, added, deleted, hunks = added + deleted > 0 ? 1 : 0, clean = added == 0 && deleted == 0, untracked = false };
        }
        catch
        {
            return new { line = "DIFF-ERR", added = 0, deleted = 0, hunks = 0, clean = true, untracked = false };
        }
    }
    static object BuildAppliesOnLocus(string editorPath)
    {
        // Agent pulse stub — Glass ECAM APPLIES (Roslyn locus) is human SSOT.
        try
        {
            if (string.IsNullOrWhiteSpace(editorPath))
                return new { line = "", errors = 0, warnings = 0, test_fails = 0, clean = true };

            return new { line = "CLEAN · problems on MFD", errors = 0, warnings = 0, test_fails = 0, clean = true };
        }
        catch
        {
            return new { line = "APPLIES-ERR", errors = 0, warnings = 0, test_fails = 0, clean = true };
        }
    }


    static string? FindGitRoot(string editorPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(editorPath));
            while (!string.IsNullOrWhiteSpace(dir))
            {
                if (Directory.Exists(Path.Combine(dir, ".git")) || File.Exists(Path.Combine(dir, ".git")))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }
        }
        catch
        {
            /* ignore */
        }

        return null;
    }

    static string RunGit(string root, params string[] args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-C");
            psi.ArgumentList.Add(root);
            foreach (var a in args)
                psi.ArgumentList.Add(a);
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null)
                return "";

            // ReadToEnd before WaitForExit can block forever on hung git (FDR surface_desk 230s).
            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(3_000))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* best-effort */ }
                try { p.WaitForExit(1_000); } catch { /* ignore */ }
                return "";
            }

            _ = Task.WhenAll(stdoutTask, stderrTask).Wait(500);
            return stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result.Trim() : "";
        }
        catch
        {
            return "";
        }
    }

    static object BuildRoleInGraph(string? workspaceRoot, string editorPath)
    {
        // ROLE glance ≠ BLAST twin: orphan|IN-MAP + counts only. Full map = MFD SemanticMap.
        var hop1 = CollectSameStemBlast(workspaceRoot, editorPath, max: 12);
        var orphan = hop1.Length == 0;
        var nodes = orphan ? 0 : hop1.Length + 1; // focus + companions (MCP same-stem stand-in)
        var edges = hop1.Length;
        return new
        {
            orphan,
            nodes,
            edges,
            map_locus = "semantic_map_mfd",
            line = orphan
                ? "ORPHAN · map on MFD"
                : $"IN-MAP · {nodes}n/{edges}e · map on MFD"
        };
    }

    static string[] CollectSameStemBlast(string? workspaceRoot, string editorPath, int max)
    {
        var names = new List<string>(max);
        try
        {
            if (!File.Exists(editorPath))
                return [];
            var dir = Path.GetDirectoryName(editorPath);
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                return [];
            var stem = Path.GetFileNameWithoutExtension(editorPath);
            var root = string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot)
                ? null
                : Path.GetFullPath(workspaceRoot.Trim());
            foreach (var f in Directory.EnumerateFiles(dir))
            {
                if (names.Count >= max)
                    break;
                if (string.Equals(f, editorPath, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.Equals(Path.GetFileNameWithoutExtension(f), stem, StringComparison.OrdinalIgnoreCase))
                    continue;
                var rel = root is null
                    ? Path.GetFileName(f)
                    : Path.GetRelativePath(root, f).Replace('\\', '/');
                names.Add(Path.GetFileName(rel));
            }
        }
        catch
        {
            /* skip */
        }

        return names.ToArray();
    }

    static string? TryPeekLandPath()
    {
        try
        {
            var path = NavigationLandLatch.LatchPath;
            if (!File.Exists(path))
                return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("path", out var p) ? p.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    static object? TryPeekShared()
    {
        try
        {
            var path = SharedFileIndication.LatchPath;
            if (!File.Exists(path))
                return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            return new
            {
                path = root.TryGetProperty("path", out var p) ? p.GetString() : null,
                shared = root.TryGetProperty("shared", out var s) && s.ValueKind is JsonValueKind.True,
                stamped_utc = root.TryGetProperty("stamped_utc", out var t) ? t.GetString() : null
            };
        }
        catch
        {
            return null;
        }
    }

    static object? TryPeekAlert()
    {
        try
        {
            var path = Path.Combine(GlassSurfaceIpc.StateRoot, "alert-LATEST.json");
            if (!File.Exists(path))
                return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            return new
            {
                level = root.TryGetProperty("level", out var l) ? l.GetString() : null,
                ok = !root.TryGetProperty("ok", out var o) || o.ValueKind is not JsonValueKind.False,
                pulse = root.TryGetProperty("pulse", out var p) ? p.GetString() : null
            };
        }
        catch
        {
            return null;
        }
    }

    static object? TryPeekLand()
    {
        try
        {
            var path = NavigationLandLatch.LatchPath;
            if (!File.Exists(path))
                return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            return new
            {
                path = root.TryGetProperty("path", out var p) ? p.GetString() : null,
                line = root.TryGetProperty("line", out var l) && l.TryGetInt32(out var li) ? li : (int?)null,
                command = root.TryGetProperty("command", out var c) ? c.GetString() : null
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Human Autoi face: TALK/HALT · ON · ARMED · OFF (not active=false ⇒ off while autonomous).</summary>
    internal static string FormatAutoiFace(CideIgniteLatch.IgniteLatchDoc? ignite)
    {
        if (ignite is null)
            return "—";
        var mode = CideIgniteLatch.NormalizeMode(ignite.Mode, ignite.AwaitPartner, ignite.AwaitingCount);
        if (mode is "halt")
            return "HALT";
        if (mode is "talk" || ignite.AwaitPartner)
            return "TALK";
        if (ignite.Autonomous && ignite.Active)
            return "ON";
        if (ignite.Autonomous)
            return "ARMED";
        return "OFF";
    }

    static string? Truncate(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s))
            return s;
        s = s.Trim();
        return s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";
    }

    static object Rpc(string op, IReadOnlyDictionary<string, JsonElement> args)
    {
        // Human modal can wait; other ops stay snappy.
        var timeoutMs = string.Equals(op, "request_confirmation", StringComparison.OrdinalIgnoreCase)
            ? 120_000
            : 8000;
        if (args.TryGetValue("timeout_ms", out var t) && t.TryGetInt32(out var ti) && ti > 0)
            timeoutMs = Math.Clamp(ti, 500, 300_000);

        var rpcArgs = new JsonObject();
        foreach (var key in new[]
                 {
                     "name", "text", "keys", "layout", "panel", "width", "height", "message",
                     "query", "execute", "action", "id", "command_id", "command", "args",
                     "start", "end", "line"
                 })
        {
            var v = Opt(args, key);
            if (v is not null)
                rpcArgs[key] = v;
        }

        var (ok, reply, error) = GlassSurfaceIpc.Call(op, rpcArgs.Count > 0 ? rpcArgs : null, timeoutMs);
        if (!ok || reply is null)
        {
            return new
            {
                schema = Schema,
                ok = false,
                go = GoName,
                tool = ToolName,
                op,
                error = error ?? "surface_rpc_failed",
                detail = "Glass host down or timeout — start via cdp_cockpit_host.",
                ipc = new { cmd = GlassSurfaceIpc.CmdPath, reply = GlassSurfaceIpc.ReplyPath }
            };
        }

        var root = reply.Value;
        object? result = null;
        if (root.TryGetProperty("result", out var resultEl))
        {
            result = resultEl.ValueKind switch
            {
                JsonValueKind.String => resultEl.GetString(),
                JsonValueKind.Null => null,
                _ => JsonNode.Parse(resultEl.GetRawText())
            };
        }

        return new
        {
            schema = Schema,
            ok = root.TryGetProperty("ok", out var okEl) && okEl.GetBoolean(),
            go = GoName,
            tool = ToolName,
            op,
            id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null,
            result,
            error = root.TryGetProperty("error", out var errEl) ? errEl.GetString() : null,
            detail = root.TryGetProperty("detail", out var detEl) ? detEl.GetString() : null
        };
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }
}
