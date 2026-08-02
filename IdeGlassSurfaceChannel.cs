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
        "highlight", "focus", "click", "set_text", "send_keys",
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
                "scene" or "status" or "caps" => Scene(),
                "layout" or "highlight" or "focus" or "click" or "set_text" or "send_keys"
                    or "appearance" or "colors" or "colors_under_cursor"
                    or "set_control_layout" or "set_panel_size" or "request_confirmation"
                    => Rpc(op, args),
                _ => Scene()
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

    static object Scene() => new
    {
        schema = Schema,
        ok = true,
        go = GoName,
        tool = ToolName,
        op = "scene",
        pulse = "surface · glass RPC · full debt live (sense+aim+drive+confirm)",
        ipc = new
        {
            cmd = GlassSurfaceIpc.CmdPath,
            reply = GlassSurfaceIpc.ReplyPath,
            habitat = GlassSurfaceIpc.StateRoot
        },
        implemented = Implemented.OrderBy(x => x).ToArray(),
        planned = Array.Empty<string>(),
        hint =
            "op=layout|highlight|focus|click|set_text|send_keys|appearance|colors|set_control_layout|set_panel_size|request_confirmation. Glass host required."
    };

    static object Rpc(string op, IReadOnlyDictionary<string, JsonElement> args)
    {
        // Human modal can wait; other ops stay snappy.
        var timeoutMs = string.Equals(op, "request_confirmation", StringComparison.OrdinalIgnoreCase)
            ? 120_000
            : 8000;
        if (args.TryGetValue("timeout_ms", out var t) && t.TryGetInt32(out var ti) && ti > 0)
            timeoutMs = Math.Clamp(ti, 500, 300_000);

        var rpcArgs = new JsonObject();
        foreach (var key in new[] { "name", "text", "keys", "layout", "panel", "width", "height", "message" })
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
