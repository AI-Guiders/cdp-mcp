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
            "history" or "log" => History(),
            "clear" or "reset" => ClearHistory(),
            "turn" or "chat" or "complete" => Turn(args),
            _ => Fail("unknown_op", "op=scene|keys|turn|history|clear  message= mode=dialog|wire dry_run=")
        };
    }

    static string History()
    {
        var msgs = CitizenDialogHistory.Load();
        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "history",
            dialog = CitizenDialogHistory.Pulse(),
            messages = msgs.Select(m => new { role = m.Role, content = Trunc(m.Content, 400) }).ToArray(),
            hint = "dialog history under StateRoot/{seat}/citizen-dialog.jsonl — used when mode=dialog"
        });
    }

    static string ClearHistory()
    {
        CitizenDialogHistory.Clear();
        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "clear",
            dialog = CitizenDialogHistory.Pulse(),
            hint = "dialog history cleared"
        });
    }

    static string Trunc(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

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
            persona_chars = CitizenPersona.WireSystemPrompt.Length,
            dialog_persona_chars = CitizenPersona.DialogSystemPrompt.Length,
            modes = new[] { "wire", "dialog" },
            mode_default = "wire",
            dialog = CitizenDialogHistory.Pulse(),
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
                ? "invite ready — op=turn mode=dialog message=… for peer prose; mode=wire for hands. dry_run= still free."
                : "not invite-ready — copy docs/design/ai-keys.example.toml → CascadeIDE ai-keys.toml; fill open_ai_api_key (Cloud.ru) or anthropic_api_key. dry_run= explains without keys.",
            next = invite.Ready
                ? new object[]
                {
                    new { go = "citizen", label = "Dialog tea", why = "op=turn mode=dialog message=привет" },
                    new { go = "citizen", label = "Wire hands", why = "op=turn mode=wire message=…" },
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
        var mode = ParseMode(args);
        var useHistory = mode == CitizenTurnMode.Dialog
            && (!args.ContainsKey("history") || Bool(args, "history", defaultTrue: true));
        if (Bool(args, "reset") || Bool(args, "clear_history"))
            CitizenDialogHistory.Clear();
        var (board, tm, liveBound) = ResolveBoardAndTm(args, inject);
        var peerIn = Arg(args, "peer") ?? CitizenPeerAck.LastPeer;

        var result = CitizenCompletions.Turn(
            message!,
            boardLines: board,
            sa: Arg(args, "sa"),
            peer: peerIn,
            next: Arg(args, "next"),
            tm: tm,
            model: Arg(args, "model"),
            dryRun: dryRun,
            inject: inject,
            mode: mode,
            history: useHistory);

        IReadOnlyList<CitizenRouteHost.Applied>? executed = null;
        CitizenPeerAck.Result? peerAck = null;
        IReadOnlyList<CitizenIntentRouter.Route>? routes = result.Routes;
        if (execute && result.Ok)
        {
            // dry_run has no provider text — allow host dogfood from user @intent lines.
            if ((routes is null || routes.Count == 0) && dryRun)
                routes = CitizenIntentRouter.RouteAll(CitizenWireParser.Parse(message!));
            if (routes is { Count: > 0 })
            {
                executed = CitizenRouteHost.Execute(routes);
                peerAck = CitizenPeerAck.FromExecuted(executed);
            }
        }

        return SerializeTurn(result, mode, liveBound, execute, executed, routes, peerAck);
    }

    static CitizenTurnMode ParseMode(IReadOnlyDictionary<string, JsonElement> args)
    {
        var raw = Arg(args, "mode") ?? Arg(args, "register");
        if (string.IsNullOrWhiteSpace(raw))
            return CitizenTurnMode.Wire;
        return raw.Trim().ToLowerInvariant() switch
        {
            "dialog" or "prose" or "chat" or "talk" or "peer" => CitizenTurnMode.Dialog,
            "wire" or "hands" or "intent" => CitizenTurnMode.Wire,
            _ => CitizenTurnMode.Wire
        };
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
        CitizenTurnMode mode,
        bool liveBound,
        bool execute,
        IReadOnlyList<CitizenRouteHost.Applied>? executed,
        IReadOnlyList<CitizenIntentRouter.Route>? routesOverride,
        CitizenPeerAck.Result? peerAck = null)
    {
        var hint = result.Hint;
        if (liveBound && result.Ok)
            hint = (hint is { Length: > 0 } ? hint + " · " : "") + "live desk bound (board/tm)";
        if (execute && executed is { Count: > 0 })
            hint = (hint is { Length: > 0 } ? hint + " · " : "") + "host executed " + executed.Count + " route(s)";
        if (peerAck is not null)
            hint = (hint is { Length: > 0 } ? hint + " · " : "") + "peer ack " + peerAck.Applied + "/" + (peerAck.Applied + peerAck.Dropped);

        var routes = routesOverride ?? result.Routes;
        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = result.Ok,
            op = "turn",
            mode = mode == CitizenTurnMode.Dialog ? "dialog" : "wire",
            dry_run = result.DryRun,
            execute,
            dialog = mode == CitizenTurnMode.Dialog ? CitizenDialogHistory.Pulse() : null,
            error = result.Error,
            hint,
            provider = result.Provider,
            model = result.Model,
            text = result.Text,
            injected = result.Built?.Injected,
            live_desk = liveBound,
            peer = peerAck?.Peer,
            peer_event = peerAck?.Event,
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
                cmd = r.Cmd,
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
                cmd = a.Cmd,
                pulse = a.Pulse,
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
