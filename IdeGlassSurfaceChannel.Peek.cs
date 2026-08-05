#nullable enable
using System.Text.Json;
using System.Text.Json.Nodes;
using Cdp.Core;

namespace CdpMcp;
internal static partial class IdeGlassSurfaceChannel
{
    /// <summary>Test hook — cabin ROLE human-face parity with GlassEditorSituRibbon.</summary>
    internal static object BuildRoleInGraphForTests(string? workspaceRoot, string editorPath) => BuildRoleInGraph(workspaceRoot, editorPath);
    static object BuildRoleInGraph(string? workspaceRoot, string editorPath)
    {
        // Human face parity with GlassEditorSituRibbon — ROLE membership only;
        // HOPS = neighborhood; LOOK = map pointer (not Trunc rebus packing).
        var hop1 = CollectSameStemBlast(workspaceRoot, editorPath, max: 12);
        var orphan = hop1.Length == 0;
        var nodes = orphan ? 0 : hop1.Length + 1; // focus + companions (MCP same-stem stand-in)
        var edges = hop1.Length;
        var role = orphan ? "сирота" : "в карте";
        var hops = nodes <= 0 && edges <= 0 ? "" : $"{nodes} узлов · {edges} связей";
        const string look = "карта → MFD";
        return new
        {
            orphan,
            nodes,
            edges,
            map_locus = "semantic_map_mfd",
            role,
            hops,
            look,
            line = role
        };
    }

    static string[] CollectSameStemBlast(string? workspaceRoot, string editorPath, int max)
    {
        var names = new List<string>(max);
        try
        {
            if (!File.Exists(editorPath))
                return[];
            var dir = Path.GetDirectoryName(editorPath);
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                return[];
            var stem = Path.GetFileNameWithoutExtension(editorPath);
            var root = string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot) ? null : Path.GetFullPath(workspaceRoot.Trim());
            foreach (var f in Directory.EnumerateFiles(dir))
            {
                if (names.Count >= max)
                    break;
                if (string.Equals(f, editorPath, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.Equals(Path.GetFileNameWithoutExtension(f), stem, StringComparison.OrdinalIgnoreCase))
                    continue;
                var rel = root is null ? Path.GetFileName(f) : Path.GetRelativePath(root, f).Replace('\\', '/');
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
                line = root.TryGetProperty("line", out var l) && l.TryGetInt32(out var li) ? li : (int? )null,
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
        var timeoutMs = string.Equals(op, "request_confirmation", StringComparison.OrdinalIgnoreCase) ? 120_000 : 8000;
        if (args.TryGetValue("timeout_ms", out var t) && t.TryGetInt32(out var ti) && ti > 0)
            timeoutMs = Math.Clamp(ti, 500, 300_000);
        var rpcArgs = new JsonObject();
        foreach (var key in new[]
        {
            "name",
            "text",
            "keys",
            "layout",
            "panel",
            "width",
            "height",
            "message",
            "query",
            "execute",
            "action",
            "id",
            "command_id",
            "command",
            "args",
            "start",
            "end",
            "line"
        }

        )
        {
            var v = Opt(args, key);
            if (v is not null)
                rpcArgs[key] = v;
        }

        var(ok, reply, error) = GlassSurfaceIpc.Call(op, rpcArgs.Count > 0 ? rpcArgs : null, timeoutMs);
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
                ipc = new
                {
                    cmd = GlassSurfaceIpc.CmdPath,
                    reply = GlassSurfaceIpc.ReplyPath
                }
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
                _ => JsonNode.Parse(resultEl.GetRawText())};
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