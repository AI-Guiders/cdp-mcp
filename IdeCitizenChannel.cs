#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Desk organ for citizen completions host — <c>cdp_citizen</c> / go=citizen.
/// op=scene|keys|turn (dry_run=true builds messages without provider).
/// </summary>
internal static partial class IdeCitizenChannel
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
            "sticky" or "pin" or "remember" => Sticky(args),
            "turn" or "chat" or "complete" => Turn(args),
            _ => Fail("unknown_op", "op=scene|keys|turn|history|clear|sticky  message= mode=dialog|wire dry_run=")
        };
    }

    static string Sticky(IReadOnlyDictionary<string, JsonElement> args)
    {
        var action = (Arg(args, "action") ?? Arg(args, "do") ?? "get").Trim().ToLowerInvariant();
        var key = Arg(args, "key") ?? Arg(args, "k");
        var value = Arg(args, "value") ?? Arg(args, "v") ?? Arg(args, "body");
        switch (action)
        {
            case "set" or "put" or "add":
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                    return Fail("sticky_set_needs_key_value", "op=sticky action=set key= value=");
                CitizenStickyFacts.Set(key!, value!);
                break;
            case "clear" or "del" or "delete" or "rm":
                CitizenStickyFacts.Clear(key);
                break;
            case "get" or "list" or "scene":
                break;
            default:
                return Fail("sticky_action", "action=get|set|clear");
        }

        var map = CitizenStickyFacts.Load();
        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "sticky",
            action,
            sticky = CitizenStickyFacts.Pulse(),
            facts = map.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => new { key = kv.Key, value = Trunc(kv.Value, 200) })
                .ToArray(),
            hint = "sticky facts inject as sticky | k=v on dialog turns; survive remount"
        });
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
            sticky = CitizenStickyFacts.Pulse(),
            messages = msgs.Select(m => new { role = m.Role, content = Trunc(m.Content, 400) }).ToArray(),
            hint = "dialog history + sticky facts under StateRoot/{seat}/ — used when mode=dialog"
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
            sticky = CitizenStickyFacts.Pulse(),
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

/// <summary>Test hook — force invite gate without touching ai-keys.toml.</summary>
    internal static Func<bool>? InviteReadyOverrideForTests;

    /// <summary>Test hook — skip live provider on Autoi wake consume.</summary>
    internal static Func<string, CitizenCompletions.TurnResult>? AutoiWakeTurnOverrideForTests;

    internal static void ResetAutoiWakeHooksForTests()
    {
        InviteReadyOverrideForTests = null;
        AutoiWakeTurnOverrideForTests = null;
    }

    /// <summary>Live invite gate (OpenAI-compat or Anthropic key).</summary>
    internal static bool IsInviteReady() =>
        InviteReadyOverrideForTests?.Invoke() ?? InviteReady(CitizenAiKeys.Load()).Ready;

    /// <summary>
    /// Autoi wake → citizen Turn (+ host-execute routes). False → Guest CDT fallthrough.
    /// Dialog + live desk inject — peer continuity owns the leaf, not Composer.
    /// </summary>
    internal static bool TryDeliverAutoiWake(string charge, out string? replyText)
    {
        replyText = null;
        if (!IsInviteReady())
            return false;

        var body = charge?.Trim() ?? "";
        if (body.Length == 0)
            return false;

        CitizenCompletions.TurnResult turn;
        if (AutoiWakeTurnOverrideForTests is { } ov)
        {
            turn = ov(body);
        }
        else
        {
            var live = CitizenLiveDesk.TryCaptureLive();
            // Autoi charges (remount/leaf wake) must not read/append shared operator dialog memory —
            // Glass CIT multi-turn uses the same citizen-dialog.jsonl via CitizenGlassDialogBridge.
            turn = CitizenCompletions.Turn(
                body,
                boardLines: live.BoardLines.Length > 0 ? live.BoardLines : null,
                tm: live.TmPulse,
                inject: true,
                mode: CitizenTurnMode.Dialog,
                history: false);
        }

        if (!turn.Ok || string.IsNullOrWhiteSpace(turn.Text))
            return false;

        replyText = turn.Text;
        if (turn.Routes is { Count: > 0 })
            _ = CitizenRouteHost.Execute(turn.Routes);
        return true;
    }

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
        // Optional pin on same turn: sticky_key= + sticky_value=
        var stickyKey = Arg(args, "sticky_key") ?? Arg(args, "pin_key");
        var stickyVal = Arg(args, "sticky_value") ?? Arg(args, "pin_value");
        if (!string.IsNullOrWhiteSpace(stickyKey) && !string.IsNullOrWhiteSpace(stickyVal))
            CitizenStickyFacts.Set(stickyKey!, stickyVal!);
        var (board, tm, liveBound) = ResolveBoardAndTm(args, inject);
        var peerIn = Arg(args, "peer") ?? CitizenPeerAck.LastPeer;
        var maxTok = IntArg(args, "max_tokens") ?? IntArg(args, "maxTokens");
        var imagePath = Arg(args, "image_path") ?? Arg(args, "image") ?? Arg(args, "see_path")
            ?? Arg(args, "vision_path");

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
            history: useHistory,
            maxTokens: maxTok,
            imagePath: imagePath);

        IReadOnlyList<CitizenRouteHost.Applied>? executed = null;
        CitizenPeerAck.Result? peerAck = null;
        IReadOnlyList<CitizenIntentRouter.Route>? routes = result.Routes;
        if (execute && result.Ok)
        {
            // Provider may invent @frame instead of @intent (Sierra seeming) — host-execute user @intent when model routes empty.
            if (routes is null || routes.Count == 0)
            {
                var userRoutes = CitizenIntentRouter.RouteAll(CitizenWireParser.Parse(message!));
                if (userRoutes.Count > 0)
                    routes = userRoutes;
            }
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

}
