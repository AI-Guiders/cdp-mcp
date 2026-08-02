#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Desk organ for citizen completions host — <c>cdp_citizen</c> / go=citizen.
/// op=scene|keys|turn (dry_run=true builds messages without provider).
/// </summary>
internal static class IdeCitizenChannel
{
    public const string ToolName = "cdp_citizen";
    public const string Schema = "citizen_host/v0";

    public static string HandleJson(IReadOnlyDictionary<string, JsonElement> args)
    {
        var op = Arg(args, "op") ?? "scene";
        return op.Trim().ToLowerInvariant() switch
        {
            "scene" or "get" => Scene(),
            "keys" or "keyring" => Keys(),
            "turn" or "chat" or "complete" => Turn(args),
            _ => Fail("unknown_op", "op=scene|keys|turn  message= board= dry_run=")
        };
    }

    static string Scene()
    {
        var keys = CitizenAiKeys.Load();
        var invite = InviteReady(keys);
        var pulse = invite.Ready
            ? "citizen · invite=ready · keys=set · tea"
            : "citizen · invite=blocked · keys=" + (keys.FileExists ? "empty" : "missing");
        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "scene",
            pulse,
            persona_chars = CitizenPersona.SystemPrompt.Length,
            inject_default = true,
            model_default = keys.HasOpenAi
                ? keys.ResolvedOpenAiModel
                : CitizenCompletions.DefaultModel,
            provider_default = keys.HasOpenAi
                ? CitizenCompletions.ProviderOpenAiCompat
                : CitizenCompletions.ProviderAnthropic,
            keys = keys.ToPublicPulse(),
            invite_ready = invite,
            hint = invite.Ready
                ? "invite ready — op=turn message=… pours tea (OpenAI-compat Cloud.ru FM or Anthropic). dry_run= still free."
                : "not invite-ready — copy docs/design/ai-keys.example.toml → CascadeIDE ai-keys.toml; fill open_ai_api_key (Cloud.ru) or anthropic_api_key. dry_run= explains without keys.",
            next = invite.Ready
                ? new object[]
                {
                    new { go = "citizen", label = "Pour tea", why = "op=turn message=hello" },
                    new { go = "citizen", label = "Dry turn", why = "op=turn dry_run=true message=hello" },
                    new { go = "citizen", label = "Keys", why = "op=keys" }
                }
                : new object[]
                {
                    new { go = "citizen", label = "Dry turn (explain)", why = "op=turn dry_run=true message=hello" },
                    new { go = "citizen", label = "Keys", why = "op=keys" },
                    new { go = "citizen", label = "Scene", why = "op=scene" }
                }
        });
    }

    /// <summary>Hospitality gate: persona+wire always; live invite needs OpenAI-compat or Anthropic key.</summary>
    sealed record InviteGate(bool Ready, string Status, string[] Checklist, string? Blocker);

    static InviteGate InviteReady(CitizenAiKeys.Snapshot keys)
    {
        var checklist = new List<string>
        {
            "persona · ok",
            "wire inject · ok",
            keys.HasAny
                ? "ai-keys.toml · set"
                : keys.FileExists
                    ? "ai-keys.toml · file empty"
                    : "ai-keys.toml · missing"
        };
        if (keys.HasOpenAi)
            checklist.Add("provider · openai_compat");
        else if (keys.HasAnthropic)
            checklist.Add("provider · anthropic");

        if (keys.HasLiveProvider)
            return new InviteGate(true, "ready", checklist.ToArray(), null);

        var blocker = keys.FileExists
            ? "open_ai_api_key / anthropic_api_key empty in CascadeIDE ai-keys.toml"
            : "missing %LocalAppData%/CascadeIDE/ai-keys.toml (see docs/design/ai-keys.example.toml)";
        checklist.Add("live turn · blocked");
        return new InviteGate(false, "blocked", checklist.ToArray(), blocker);
    }

    static string Keys()
    {
        var keys = CitizenAiKeys.Load();
        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "keys",
            keys = keys.ToPublicPulse(),
            hint = keys.HasLiveProvider
                ? "keyring ready — turn prefers open_ai (Cloud.ru FM) then Anthropic"
                : "missing keys — write open_ai_api_key or anthropic_api_key to CascadeIDE ai-keys.toml"
        });
    }

    static string Turn(IReadOnlyDictionary<string, JsonElement> args)
    {
        var message = Arg(args, "message") ?? Arg(args, "body") ?? Arg(args, "text") ?? Arg(args, "msg");
        if (string.IsNullOrWhiteSpace(message))
            return Fail("message_required", "turn message=… [board=] [dry_run=true] [execute=]");

        var dryRun = Bool(args, "dry_run") || Bool(args, "dry");
        var inject = !args.ContainsKey("inject") || Bool(args, "inject", defaultTrue: true);
        var execute = WantExecute(args, dryRun);
        var (board, tm, liveBound) = ResolveBoardAndTm(args, inject);

        var result = CitizenCompletions.Turn(
            message!,
            boardLines: board,
            sa: Arg(args, "sa"),
            peer: Arg(args, "peer"),
            next: Arg(args, "next"),
            tm: tm,
            model: Arg(args, "model"),
            dryRun: dryRun,
            inject: inject);

        IReadOnlyList<CitizenRouteHost.Applied>? executed = null;
        IReadOnlyList<CitizenIntentRouter.Route>? routes = result.Routes;
        if (execute && result.Ok)
        {
            // dry_run has no provider text — allow host dogfood from user @intent lines.
            if ((routes is null || routes.Count == 0) && dryRun)
                routes = CitizenIntentRouter.RouteAll(CitizenWireParser.Parse(message!));
            if (routes is { Count: > 0 })
                executed = CitizenRouteHost.Execute(routes);
        }

        return SerializeTurn(result, liveBound, execute, executed, routes);
    }

    /// <summary>Live turns execute routes by default; dry_run skips unless execute=true.</summary>
    static bool WantExecute(IReadOnlyDictionary<string, JsonElement> args, bool dryRun)
    {
        if (args.ContainsKey("execute"))
            return Bool(args, "execute");
        return !dryRun;
    }

    static (IEnumerable<string>? Board, string? Tm, bool LiveBound) ResolveBoardAndTm(
        IReadOnlyDictionary<string, JsonElement> args,
        bool inject)
    {
        var boardRaw = Arg(args, "board");
        string? tm = Arg(args, "tm");
        if (!string.IsNullOrWhiteSpace(boardRaw))
        {
            var board = boardRaw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return (board, tm, false);
        }

        if (!inject)
            return (null, tm, false);

        var live = CitizenLiveDesk.TryCaptureLive();
        if (live.BoardLines.Length == 0)
            return (null, tm, false);

        return (live.BoardLines, tm ?? live.TmPulse, live.FromLive);
    }

    static string SerializeTurn(
        CitizenCompletions.TurnResult result,
        bool liveBound,
        bool execute,
        IReadOnlyList<CitizenRouteHost.Applied>? executed,
        IReadOnlyList<CitizenIntentRouter.Route>? routesOverride)
    {
        var hint = result.Hint;
        if (liveBound && result.Ok)
            hint = (hint is { Length: > 0 } ? hint + " · " : "") + "live desk bound (board/tm)";
        if (execute && executed is { Count: > 0 })
            hint = (hint is { Length: > 0 } ? hint + " · " : "") + "host executed " + executed.Count + " route(s)";

        var routes = routesOverride ?? result.Routes;
        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = result.Ok,
            op = "turn",
            dry_run = result.DryRun,
            execute,
            error = result.Error,
            hint,
            provider = result.Provider,
            model = result.Model,
            text = result.Text,
            injected = result.Built?.Injected,
            live_desk = liveBound,
            afferent = result.Built?.AfferentPulse,
            message_count = result.Built?.Messages.Count,
            system_chars = result.Built?.System.Length,
            wire_intents = result.WireIntents?.Select(m => new
            {
                kind = m.Kind.ToString(),
                type = m.Type,
                intent = m.IntentText,
                fields = m.Fields
            }).ToArray(),
            routes = routes?.Select(r => new
            {
                verb = r.Verb.ToString(),
                raw = r.Raw,
                ok = r.Ok,
                go = r.Go,
                organ = r.Organ,
                path = r.Path,
                detail = r.Detail,
                scene = r.Scene,
                reason = r.Reason
            }).ToArray(),
            executed = executed?.Select(a => new
            {
                verb = a.Verb,
                raw = a.Raw,
                ok = a.Ok,
                action = a.Action,
                seat = a.Seat,
                go = a.Go,
                path = a.Path,
                doc_id = a.DocId,
                reason = a.Reason
            }).ToArray()
        });
    }

    static bool Bool(IReadOnlyDictionary<string, JsonElement> args, string key, bool defaultTrue = false)
    {
        if (!args.TryGetValue(key, out var el))
            return defaultTrue;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => el.GetString() is "1" or "true" or "yes" or "on",
            JsonValueKind.Number => el.TryGetInt32(out var n) && n != 0,
            _ => defaultTrue
        };
    }

    static string? Arg(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    static string Fail(string error, string hint) =>
        JsonSerializer.Serialize(new { schema = Schema, ok = false, error, hint });
}
