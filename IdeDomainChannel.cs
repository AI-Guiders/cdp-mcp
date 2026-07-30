#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=domain</c> / Meta <c>cdp_domain</c> — domain ownership pulse [A].
/// Reconstruction chains from <c>.cdp/domain/*.md</c>: name → edges → entry → antipattern.
/// Ops: scene|pulse|list|card. Dig-before-ask surface; not W-essay.
/// </summary>
internal static class IdeDomainChannel
{
    public const string SchemaVersion = "domain_channel/v0";
    public const string ToolName = "cdp_domain";
    public const string GoName = "domain";

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
        var result = op switch
        {
            "list" or "ids" => List(session),
            "card" or "get" or "one" => Card(session, args),
            "pulse" or "a" => Pulse(session, args),
            _ => Scene(session, args)
        };
        PublishGlass(session);
        return result;
    }

    public static string PulseLine(SessionContext? session = null)
    {
        var cards = IdeDomainPulse.LoadCards(session?.ProjectRoot);
        if (cards.Count == 0)
            return "domain · empty · .cdp/domain";
        var hint = IdeDomainPulse.FocusHintFromPlanLatch();
        var pulse = IdeDomainPulse.FormatPulseA(cards, hint);
        var first = pulse.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return cards.Count == 1
            ? $"domain · {cards[0].Id} · go=domain"
            : $"domain · {cards.Count} cards · {Trim(first, 48)}";
    }

    /// <summary>Mirror domain pulse to flat CIDE chrome latch (not EICAS).</summary>
    public static void PublishGlass(SessionContext? session = null)
    {
        try
        {
            var cards = IdeDomainPulse.LoadCards(session?.ProjectRoot);
            var pulse = PulseLine(session);
            // Dark Cockpit: chrome only while domain cards exist.
            CideDomainLatch.Publish(active: cards.Count > 0, pulse, cards.Count);
        }
        catch
        {
            /* best-effort */
        }
    }

    static object Scene(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var hint = FocusHint(args);
        var cards = IdeDomainPulse.LoadCards(session.ProjectRoot);
        var pulse = IdeDomainPulse.FormatPulseA(cards, hint);
        var picked = IdeDomainPulse.SelectCards(cards, hint, IdeDomainPulse.MaxCardsA);
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "scene",
            go = GoName,
            tool = ToolName,
            dir = IdeDomainPulse.ResolveDir(session.ProjectRoot),
            focus_hint = hint,
            card_count = cards.Count,
            pulse,
            cards = picked.Select(c => new { id = c.Id, title = c.Title }).ToArray(),
            ops = new[] { "scene", "pulse", "list", "card" },
            next = new object[]
            {
                new { go = "domain", label = "Pulse [A]", why = "op=pulse focus=" },
                new { go = "domain", label = "One card [C]", why = "op=card id=tm" },
                new { go = "pressure_desk", label = "Pressure Domain axis", why = "stash Domain chains" }
            },
            hint =
                "Domain ownership [A]: reconstruction chains (name→edges→entry→≠). " +
                "Dig here before asking operator. Stamp .cdp/domain after ship. " +
                "op=card id= for one-card [C]; W dump forbidden as default."
        };
    }

    static object Pulse(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var hint = FocusHint(args);
        var cards = IdeDomainPulse.LoadCards(session.ProjectRoot);
        var pulse = IdeDomainPulse.FormatPulseA(cards, hint);
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
        var cards = IdeDomainPulse.LoadCards(session.ProjectRoot);
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "list",
            go = GoName,
            dir = IdeDomainPulse.ResolveDir(session.ProjectRoot),
            cards = cards.Select(c => new { id = c.Id, title = c.Title }).ToArray()
        };
    }

    static object Card(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = (Opt(args, "id") ?? Opt(args, "card") ?? Opt(args, "name") ?? "").Trim();
        if (id.Length == 0)
            return Fail("need_id", "op=card id=tm|ignite|cockpit|pressure");

        var cards = IdeDomainPulse.LoadCards(session.ProjectRoot);
        var card = cards.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (card is null)
            return Fail("unknown_card", $"no .cdp/domain card id={id}");

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
            hint = "One-card [C] reconstruction chain — dig before ask."
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
