#nullable enable
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>Candidate parse + promote hints (≤ADX soft-warn peel).</summary>
internal static partial class IdeArchBoardChannel
{
    static Candidate ParseCandidate(string raw)
    {
        var s = raw.Trim();
        // Canonical: CodeAnchor bracket wire via BracketLocate (same as buffer/sniper).
        if (TryAsCodeAnchorWire(s, out var wire, out var path, out var member, out var label))
        {
            return new Candidate
            {
                Id = ShortId("c"),
                Label = label,
                Anchor = wire,
                Path = path,
                Symbol = member,
                Status = "candidate"
            };
        }

        // Shorthand → normalize into CodeAnchor wire (still not "bare path as SSOT").
        if (s.Contains("::", StringComparison.Ordinal))
        {
            var parts = s.Split("::", 2, StringSplitOptions.TrimEntries);
            var f = parts[0].Replace('\\', '/');
            var m = parts.Length > 1 ? parts[1] : null;
            wire = m is { Length: > 0 }
                ? BracketLocate.Format(new BracketLocate.Span(f, m, null, null))
                : BracketLocate.Format(new BracketLocate.Span(f, null, null, null));
            return new Candidate
            {
                Id = ShortId("c"),
                Label = m is { Length: > 0 } ? m : Path.GetFileName(f),
                Anchor = wire,
                Path = f,
                Symbol = m,
                Status = "candidate"
            };
        }

        // Bare symbol — label only until agent supplies a real CodeAnchor.
        return new Candidate
        {
            Id = ShortId("c"),
            Label = s,
            Anchor = null,
            Path = null,
            Symbol = s,
            Status = "candidate"
        };
    }

    static bool TryAsCodeAnchorWire(
        string raw,
        out string wire,
        out string? path,
        out string? member,
        out string label)
    {
        wire = raw;
        path = null;
        member = null;
        label = raw;
        if (!raw.Contains("[F:", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var span = BracketLocate.Parse(raw);
            wire = BracketLocate.Format(span);
            path = span.File;
            member = span.MemberKey;
            label = member is { Length: > 0 }
                ? member
                : Path.GetFileName(path ?? raw);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static string NormEdge(string raw)
    {
        var s = raw.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return s switch
        {
            "feed" or "input" => "feeds",
            "mount" or "slot" => "mounts",
            "project" or "projection" => "projects",
            "wire" or "connect" => "wires",
            _ => s
        };
    }

    static object PromoteGo(RoleSlot slot, Candidate? elected)
    {
        var label = elected?.Label ?? slot.Id;
        return slot.Role switch
        {
            "ccu" => new { go = "scope", label = $"Sniper extract {label}", why = "CCU promote → corridors, not thick set_text" },
            "compositor" => new { go = "goto", label = $"Land {label}", why = "compositor seat/pane seams" },
            "channel" => new { go = "buffer", label = $"Open {elected?.Path ?? label}", why = "channel DTO / organ file" },
            "surface" => new { go = "refactor_plan", label = "Recommend next cut", why = "surface assembly still mixed?" },
            _ => new { go = "goto", label = $"Land {label}", why = "promote is plan-only in v0" }
        };
    }
}
