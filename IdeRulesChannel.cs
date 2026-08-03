#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=rules</c> / Meta <c>cdp_rules</c> — healthy-agent standing [A].
/// Cards from <c>.cdp/rules/*.md</c>. Ops: scene|pulse|list|card. Not eQRH; not W-essay.
/// </summary>
internal static class IdeRulesChannel
{
    public const string SchemaVersion = "rules_channel/v0";
    public const string ToolName = "cdp_rules";
    public const string GoName = "rules";

    public static string HandleJson(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null) =>
        JsonSerializer.Serialize(Handle(session, args), new JsonSerializerOptions { WriteIndented = true });

    public static object Handle(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = (Opt(args, "op") ?? Opt(args, "cmd") ?? "scene").Trim().ToLowerInvariant();
        return op switch
        {
            "list" or "ids" => List(session),
            "card" or "get" or "one" => Card(session, args),
            "pulse" or "a" => Pulse(session, args),
            _ => Scene(session, args)
        };
    }

    public static string PulseLine(SessionContext? session = null)
    {
        var cards = IdeStandingPulse.LoadCards(session?.ProjectRoot);
        if (cards.Count == 0)
            return "rules · empty · .cdp/rules";
        var hint = IdeDomainPulse.FocusHintFromPlanLatch();
        var pulse = IdeStandingPulse.FormatPulseA(cards, hint);
        var first = pulse.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return cards.Count == 1
            ? $"rules · {cards[0].Id} · go=rules"
            : $"rules · {cards.Count} cards · {Trim(first, 48)}";
    }

    static object Scene(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var hint = FocusHint(args);
        var cards = IdeStandingPulse.LoadCards(session.ProjectRoot);
        var pulse = IdeStandingPulse.FormatPulseA(cards, hint);
        var picked = IdeDomainPulse.SelectCards(cards, hint ?? "healthy-agent", IdeStandingPulse.MaxCardsA);
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "scene",
            go = GoName,
            tool = ToolName,
            dir = IdeStandingPulse.ResolveDir(session.ProjectRoot),
            focus_hint = hint,
            card_count = cards.Count,
            pulse,
            cards = picked.Select(c => new { id = c.Id, title = c.Title }).ToArray(),
            ops = new[] { "scene", "pulse", "list", "card" },
            next = new object[]
            {
                new { go = "rules", label = "Pulse [A]", why = "op=pulse" },
                new { go = "rules", label = "One card [C]", why = "op=card id=healthy-agent" },
                new { go = "qrh", label = "eQRH abnormal", why = "standing ≠ abnormal shelf" }
            },
            hint =
                "Standing healthy-agent rules [A] (.cdp/rules). ε body recall — dig/parallel, not biped serial. " +
                "Remount Autoi appends Standing appendix. Not Cursor alwaysApply dump; not eQRH."
        };
    }

    static object Pulse(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var hint = FocusHint(args);
        var cards = IdeStandingPulse.LoadCards(session.ProjectRoot);
        var pulse = IdeStandingPulse.FormatPulseA(cards, hint);
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "pulse",
            go = GoName,
            focus_hint = hint,
            pulse,
            empty = pulse.Length == 0
        };
    }

    static object List(SessionContext session)
    {
        var cards = IdeStandingPulse.LoadCards(session.ProjectRoot);
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "list",
            go = GoName,
            dir = IdeStandingPulse.ResolveDir(session.ProjectRoot),
            cards = cards.Select(c => new { id = c.Id, title = c.Title }).ToArray()
        };
    }

    static object Card(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = (Opt(args, "id") ?? Opt(args, "card") ?? Opt(args, "name") ?? "").Trim();
        if (id.Length == 0)
            return Fail("need_id", "op=card id=healthy-agent");

        var cards = IdeStandingPulse.LoadCards(session.ProjectRoot);
        var card = cards.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (card is null)
            return Fail("unknown_card", $"no .cdp/rules card id={id}");

        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "card",
            go = GoName,
            detail = "C",
            id = card.Id,
            title = card.Title,
            invariants = card.Invariants,
            entry = card.Entry,
            antipatterns = card.Antipatterns,
            chain = IdeDomainPulse.FormatChainC(card),
            hint = "One-card [C] standing chain — recall ε before biped mask."
        };
    }

    static string? FocusHint(IReadOnlyDictionary<string, JsonElement> args)
    {
        var fromArgs = Opt(args, "focus") ?? Opt(args, "hint") ?? Opt(args, "q");
        if (fromArgs is { Length: > 0 })
            return fromArgs.Trim();
        return IdeDomainPulse.FocusHintFromPlanLatch();
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
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

    static object Fail(string error, string hint) => new
    {
        schema = SchemaVersion,
        ok = false,
        go = GoName,
        error,
        hint
    };

    static string Trim(string s, int max)
    {
        var t = s.Trim();
        return t.Length <= max ? t : t[..max].TrimEnd() + "…";
    }
}
