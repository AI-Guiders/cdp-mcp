#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Thin ICM discovery / invoke surface for future on-demand GUI CDP client.
/// Meta <c>cdp_icm</c> / <c>go=icm</c>. Does not mutate Intent Melody.
/// </summary>
internal static class IdeIcmChannel
{
    public const string SchemaVersion = "icm_channel/v1";
    public const string ToolName = "cdp_icm";
    public const string GoName = "icm_desk";

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task<string> HandleJsonAsync(
        IReadOnlyDictionary<string, JsonElement>? args,
        CancellationToken cancellationToken)
    {
        var result = await HandleAsync(args, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(result, JsonOpts);
    }

    public static async Task<object> HandleAsync(
        IReadOnlyDictionary<string, JsonElement>? args,
        CancellationToken cancellationToken)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = (Opt(args, "op") ?? Opt(args, "cmd") ?? "scene").Trim().ToLowerInvariant();
        return op switch
        {
            "aliases" or "list" or "map" => Aliases(),
            "resolve" => Resolve(args),
            "invoke" or "exec" or "run" => await InvokeAsync(args, cancellationToken).ConfigureAwait(false),
            _ => Scene()
        };
    }

    static object Scene() => new
    {
        ok = true,
        schema = SchemaVersion,
        go = GoName,
        tool = ToolName,
        pulse = $"icm · bound={IdeCommandModule.IsBound} · aliases={IdeCommandAliasMap.Count}",
        bound = IdeCommandModule.IsBound,
        alias_count = IdeCommandAliasMap.Count,
        host_profile = "agent-only",
        gui_host = "down",
        hint = "op=aliases|resolve command_id=|invoke command_id= — GUI client discovery; Melody catalog untouched. Start/Stop host later.",
        next = new object[]
        {
            new { go = "icm", label = "Aliases", why = "op=aliases" },
            new { go = "land", label = "Nav Anchor", why = "cdp_land — GUI parity" },
            new { go = "plan", label = "Task Manager", why = "ICM stage focus" }
        }
    };

    static object Aliases() => new
    {
        ok = true,
        schema = SchemaVersion,
        op = "aliases",
        count = IdeCommandAliasMap.Count,
        entries = IdeCommandAliasMap.ListEntries(),
        hint = "Bucket A only. Unknown Melody ids pass through ExecuteAliasedAsync unchanged."
    };

    static object Resolve(IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = Opt(args, "command_id") ?? Opt(args, "id") ?? Opt(args, "command");
        if (string.IsNullOrWhiteSpace(id))
        {
            return new
            {
                ok = false,
                schema = SchemaVersion,
                op = "resolve",
                error = "command_id required"
            };
        }

        if (!IdeCommandAliasMap.TryResolve(id, out var r))
        {
            return new
            {
                ok = true,
                schema = SchemaVersion,
                op = "resolve",
                command_id = id,
                mapped = false,
                tool = id,
                hint = "passthrough — treat as native CDP command_id"
            };
        }

        object? defaults = null;
        if (r.Defaults is { Count: > 0 })
        {
            var d = new Dictionary<string, JsonElement>(r.Defaults, StringComparer.Ordinal);
            defaults = d;
        }

        return new
        {
            ok = true,
            schema = SchemaVersion,
            op = "resolve",
            command_id = id,
            mapped = true,
            tool = r.Tool,
            identity = r.Identity,
            defaults,
            hint = "GUI → IdeCommandModule.ExecuteAliasedAsync or CallTool(tool)"
        };
    }

    static async Task<object> InvokeAsync(
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        var id = Opt(args, "command_id") ?? Opt(args, "id") ?? Opt(args, "command");
        if (string.IsNullOrWhiteSpace(id))
        {
            return new
            {
                ok = false,
                schema = SchemaVersion,
                op = "invoke",
                error = "command_id required"
            };
        }

        if (!IdeCommandModule.IsBound)
        {
            return new
            {
                ok = false,
                schema = SchemaVersion,
                op = "invoke",
                error = "IdeCommandModule not bound"
            };
        }

        // Strip control keys; remainder = tool args
        var callArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (k, v) in args)
        {
            if (k is "op" or "cmd" or "command_id" or "id" or "command")
                continue;
            callArgs[k] = v;
        }

        string tool = id;
        if (IdeCommandAliasMap.TryResolve(id, out var resolved))
            tool = resolved.Tool;

        var text = await IdeCommandModule.ExecuteAliasedAsync(id, callArgs, cancellationToken)
            .ConfigureAwait(false);
        return new
        {
            ok = true,
            schema = SchemaVersion,
            op = "invoke",
            command_id = id,
            tool,
            result_text = text
        };
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }
}
