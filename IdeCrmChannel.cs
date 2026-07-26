#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=crm</c> / Meta <c>cdp_crm</c> — CRM callout panel (ADR-0014).
/// Closed codes: approved|stabilized|go_around|hold|unable|negative|say_again|continue|roger|wilco.
/// Operator act writes SSOT; agent reads slim pulse (no reject essays in chat).
/// </summary>
internal static class IdeCrmChannel
{
    public const string SchemaVersion = "crm/v1";
    public const string ToolName = "cdp_crm";
    public const string Awaiting = "awaiting";

    public static readonly string[] Lexicon =
    [
        "approved", "stabilized", "go_around", "hold", "unable",
        "negative", "say_again", "continue", "roger", "wilco"
    ];

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static string HandleJson(
        SessionContext session,
        IntentWorkspaceStore? store,
        IntentWorkspaceState? state,
        IReadOnlyDictionary<string, JsonElement>? args) =>
        JsonSerializer.Serialize(Handle(session, store, state, args), Pretty);

    public static object Handle(
        SessionContext session,
        IntentWorkspaceStore? store = null,
        IntentWorkspaceState? state = null,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        args = FlattenGoArgs(args);
        var op = (Opt(args, "op") ?? Opt(args, "cmd") ?? "scene").Trim().ToLowerInvariant();
        return op switch
        {
            "call" or "ask" or "open" => Call(session, args),
            "respond" or "reply" or "say" => Respond(session, store, state, args),
            "last" => Last(session),
            "clear" => Clear(session),
            "lexicon" => new { ok = true, schema = SchemaVersion, go = "crm", lexicon = Lexicon },
            _ => Scene(session)
        };
    }

    static object Scene(SessionContext session)
    {
        var snap = Read(session);
        var pulse = PulseLine(snap);
        return new
        {
            ok = true,
            schema = SchemaVersion,
            role = "crm",
            go = "crm",
            tool = ToolName,
            detail = "slim",
            pulse,
            status = snap?.Status ?? "idle",
            call = snap is null ? null : Card(snap),
            lexicon = Lexicon,
            next = BuildNext(snap),
            hint = "Operator: cmd=approved|stabilized|go around|hold|…. Agent: op=call then poll scene — no reject essays."
        };
    }

    static object Call(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var ask = Opt(args, "ask") ?? Opt(args, "what") ?? Opt(args, "text") ?? "Confirm approach";
        var kind = Opt(args, "kind") ?? Opt(args, "ref_kind") ?? "general";
        var refId = Opt(args, "ref") ?? Opt(args, "ref_id") ?? Opt(args, "plan_id")
                    ?? Guid.NewGuid().ToString("N")[..12];
        var snap = new CrmSnap(
            SchemaVersion,
            Guid.NewGuid().ToString("N")[..12],
            Awaiting,
            null,
            kind,
            refId,
            ask.Trim(),
            DateTime.UtcNow,
            null,
            null);
        Write(session, snap);
        return new
        {
            ok = true,
            schema = SchemaVersion,
            op = "call",
            pulse = PulseLine(snap),
            call = Card(snap),
            chat = $"CRM awaiting: {ask.Trim()}",
            next = BuildNext(snap),
            hint = "Human responds via cockpit/REPL CRM codes — do not negotiate in chat."
        };
    }

    static object Respond(
        SessionContext session,
        IntentWorkspaceStore? store,
        IntentWorkspaceState? state,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var code = NormCode(Opt(args, "code") ?? Opt(args, "callout") ?? Opt(args, "response") ?? Opt(args, "say"));
        if (code is null)
            return Err("code_required", "crm respond code=approved|go_around|stabilized|hold|…");

        var why = Opt(args, "why"); // short code only — not an essay
        if (why is { Length: > 80 })
            why = why[..80];

        var prev = Read(session);
        var snap = (prev ?? new CrmSnap(
            SchemaVersion,
            Guid.NewGuid().ToString("N")[..12],
            Awaiting,
            null,
            "general",
            "adhoc",
            "callout",
            DateTime.UtcNow,
            null,
            null)) with
        {
            Status = code,
            Callout = code,
            Why = why,
            ResolvedUtc = DateTime.UtcNow
        };
        Write(session, snap);

        object? planBridge = null;
        if (code is "approved" or "go_around" or "negative" or "unable")
            planBridge = TryBridgePlan(session, store, state, reject: code is not "approved");

        return new
        {
            ok = true,
            schema = SchemaVersion,
            op = "respond",
            pulse = PulseLine(snap),
            call = Card(snap),
            plan = planBridge,
            chat = $"CRM {code}" + (why is { Length: > 0 } ? $" · {why}" : ""),
            next = BuildNext(snap),
            hint = "Gate speech done in SSOT — continue from pulse, not chat negotiation."
        };
    }

    static object Last(SessionContext session)
    {
        var snap = Read(session);
        return new
        {
            ok = snap is not null,
            schema = SchemaVersion,
            op = "last",
            pulse = PulseLine(snap),
            call = snap is null ? null : Card(snap)
        };
    }

    static object Clear(SessionContext session)
    {
        var path = LatestPath(session);
        if (File.Exists(path))
            File.Delete(path);
        return new { ok = true, schema = SchemaVersion, op = "clear", pulse = "crm · idle" };
    }

    static object? TryBridgePlan(
        SessionContext session,
        IntentWorkspaceStore? store,
        IntentWorkspaceState? state,
        bool reject)
    {
        if (store is null || state is null)
            return null;
        try
        {
            return IdePlanPromote.Confirm(store, state, session.ProjectRoot, null, null, reject);
        }
        catch
        {
            return null;
        }
    }

    static object[] BuildNext(CrmSnap? snap)
    {
        if (snap is null || !string.Equals(snap.Status, Awaiting, StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new { go = "crm", label = "Call", why = "op=call ask=…" },
                new { go = "plan", label = "Share plan", why = "ask=confirm → CRM awaiting" }
            ];
        }

        // Operator panel strip (desk) — closed codes only.
        return
        [
            new { go = "crm", label = "Approved", why = "op=respond code=approved" },
            new { go = "crm", label = "Stabilized", why = "op=respond code=stabilized" },
            new { go = "crm", label = "Go Around", why = "op=respond code=go_around" },
            new { go = "crm", label = "Hold", why = "op=respond code=hold" },
            new { go = "crm", label = "Unable", why = "op=respond code=unable" },
            new { go = "crm", label = "Say Again", why = "op=respond code=say_again" }
        ];
    }

    static string PulseLine(CrmSnap? snap)
    {
        if (snap is null)
            return "crm · idle";
        if (string.Equals(snap.Status, Awaiting, StringComparison.OrdinalIgnoreCase))
            return $"crm · AWAITING · {snap.Kind}:{snap.RefId}";
        return $"crm · {snap.Callout ?? snap.Status} · {snap.Kind}:{snap.RefId}";
    }

    static object Card(CrmSnap snap) => new
    {
        call_id = snap.CallId,
        status = snap.Status,
        callout = snap.Callout,
        kind = snap.Kind,
        ref_id = snap.RefId,
        ask = snap.Ask,
        why = snap.Why,
        opened_utc = snap.OpenedUtc,
        resolved_utc = snap.ResolvedUtc
    };

    static CrmSnap? Read(SessionContext session)
    {
        var path = LatestPath(session);
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<CrmSnap>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    static void Write(SessionContext session, CrmSnap snap)
    {
        var dir = InboxDir(session);
        Directory.CreateDirectory(dir);
        var latest = Path.Combine(dir, "LATEST.json");
        var stamped = Path.Combine(dir, $"crm-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{snap.CallId}.json");
        var json = JsonSerializer.Serialize(snap, Pretty);
        File.WriteAllText(latest, json);
        File.WriteAllText(stamped, json);
    }

    static string LatestPath(SessionContext session) => Path.Combine(InboxDir(session), "LATEST.json");

    static string InboxDir(SessionContext session)
    {
        var root = session.ProjectRoot ?? session.ScmRoot;
        if (root is { Length: > 0 })
            return Path.GetFullPath(Path.Combine(root, ".cdp", "crm"));
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp",
            "crm");
    }

    public static string? NormCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var s = raw.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return s switch
        {
            "approved" or "approve" or "cleared" or "clear" or "confirm" or "yes" => "approved",
            "stabilized" or "stable" or "on_path" or "onpath" => "stabilized",
            "go_around" or "goaround" or "reject" or "denied" or "abort" => "go_around",
            "hold" or "standby" or "stand_by" or "wait" => "hold",
            "unable" => "unable",
            "negative" or "no" or "nop" => "negative",
            "say_again" or "sayagain" or "repeat" => "say_again",
            "continue" or "cont" => "continue",
            "roger" or "ack" => "roger",
            "wilco" => "wilco",
            _ => Lexicon.Contains(s) ? s : null
        };
    }

    static object Err(string error, string hint) => new
    {
        ok = false,
        schema = SchemaVersion,
        error,
        hint,
        lexicon = Lexicon
    };

    static IReadOnlyDictionary<string, JsonElement> FlattenGoArgs(IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!args.TryGetValue("go_args", out var ga) || ga.ValueKind != JsonValueKind.Object)
            return args;
        var flat = new Dictionary<string, JsonElement>(args, StringComparer.Ordinal);
        foreach (var p in ga.EnumerateObject())
        {
            if (!flat.ContainsKey(p.Name))
                flat[p.Name] = p.Value.Clone();
        }

        return flat;
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString()?.Trim(),
            JsonValueKind.Number => el.GetRawText(),
            _ => null
        };
    }

    public sealed record CrmSnap(
        string Schema,
        string CallId,
        string Status,
        string? Callout,
        string Kind,
        string RefId,
        string Ask,
        DateTimeOffset OpenedUtc,
        DateTimeOffset? ResolvedUtc,
        string? Why);
}
