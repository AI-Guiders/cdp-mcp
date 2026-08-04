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
        var land = TryPeekLand();
        var landPath = TryPeekLandPath();
        var next = plan?.Task ?? plan?.Feature;
        var why = plan?.Why ?? IdePressureChannel.CompactWhyLine(IdePressureChannel.TryPeekSealedCourse());
        var leaf = plan?.Task;
        var fileSitu = BuildFileSitu(landPath, session.ProjectRoot, why, leaf);
        var pulse = plan is { Active: true }
            ? $"glass · NEXT · {Truncate(next, 40)} · WHY · {Truncate(why, 40)}"
            : "surface · glass RPC · full debt live (sense+aim+drive+confirm)";

        return new
        {
            schema = Schema,
            ok = true,
            go = GoName,
            tool = ToolName,
            op = "scene",
            pulse,
            // Shared-SSOT A-side: same situation human sees on Plan/HDG + Editor FILE WHY/BLAST.
            shared_ssot = new
            {
                next,
                why,
                feature = plan?.Feature,
                active = plan?.Active ?? false,
                land = land,
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
                "shared_ssot = Plan NEXT+WHY + file_situ (path/why_this_file/blast) (+ land). RPC: op=layout|highlight|focus|click|set_text|send_keys|palette|run|appearance|colors|set_control_layout|set_panel_size|request_confirmation. Glass host required for RPC."
        };
    }

    static object BuildFileSitu(string? editorPath, string? workspaceRoot, string? why, string? leaf)
    {
        if (string.IsNullOrWhiteSpace(editorPath))
        {
            return new
            {
                path = (string?)null,
                why_this_file = (string?)null,
                blast = Array.Empty<string>()
            };
        }

        var whyBits = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(leaf))
            whyBits.Add(Truncate(leaf!.Trim(), 48)!);
        if (!string.IsNullOrWhiteSpace(why))
            whyBits.Add(Truncate(why!.Trim(), 72)!);
        var whyThisFile = whyBits.Count > 0 ? string.Join(" · ", whyBits) : null;

        return new
        {
            path = editorPath,
            why_this_file = whyThisFile,
            blast = CollectSameStemBlast(workspaceRoot, editorPath, max: 3)
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
