#nullable enable
using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

internal static partial class IdeArchBoardChannel
{
    static object Scene(SessionContext session, IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var view = (Opt(args, "view") ?? Opt(args, "board") ?? Opt(args, "mode") ?? "plan")
            .Trim().ToLowerInvariant();
        var asBuilt = view is "as_built" or "asbuilt" or "built";
        var doc = asBuilt ? LoadAsBuilt(session) : Load(session);
        var path = asBuilt ? AsBuiltPath(session) : LatestPath(session);
        return OkCard(session, doc, "scene", pulse: Pulse(doc), boardPath: path);
    }

    static object AddRole(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var roleRaw = Opt(args, "role") ?? Opt(args, "kind");
        if (roleRaw is null or { Length: 0 })
            return Err("role_required", "op=add_role role=ccu|channel|cds|ids|compositor|surface|…");

        var role = NormRole(roleRaw);
        if (!RoleLexicon.Contains(role))
            return Err("unknown_role", $"role={role} — lexicon: {string.Join('|', RoleLexicon)}");

        var idHint = Opt(args, "id") ?? Opt(args, "role_id");
        var note = Opt(args, "note") ?? Opt(args, "why");

        return Mutate(session, doc =>
        {
            var id = idHint ?? ShortId(role);
            if (doc.Roles.Any(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                return (false, Err("role_id_exists", $"id={id} already on board — pick another id="));

            var slot = new RoleSlot
            {
                Id = id,
                Role = role,
                Note = note,
                Status = "open"
            };
            doc.Roles.Add(slot);
            doc.FocusRoleId = slot.Id;
            return (true, OkCard(session, doc, "add_role", pulse: $"arch_board · +{role} · {id}", focus: slot.Id));
        });
    }

    static object AddCandidates(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var doc = Load(session);
        var slot = FindRole(doc, args);
        if (slot is null)
            return Err("role_not_found", "op=add_candidates role=ccu|role_id=… — add_role first");

        var raw = OptList(args, "anchors", "candidates", "candidate", "items");
        if (raw.Count == 0)
            return Err("candidates_required", "anchors=[F:IdeCockpit.Build.cs;M:BuildAsync] — CodeAnchor wire, not bare path");

        var added = new List<string>();
        foreach (var item in raw)
        {
            var c = ParseCandidate(item);
            if (slot.Candidates.Any(x =>
                    x.Label.Equals(c.Label, StringComparison.OrdinalIgnoreCase) ||
                    (c.Anchor is { Length: > 0 } && x.Anchor is { Length: > 0 } &&
                     x.Anchor.Equals(c.Anchor, StringComparison.OrdinalIgnoreCase))))
                continue;
            slot.Candidates.Add(c);
            added.Add(c.Id);
        }

        if (added.Count == 0)
            return OkCard(session, doc, "add_candidates", pulse: $"arch_board · {slot.Id} · no new candidates", focus: slot.Id);

        Save(session, doc);
        return OkCard(session, doc, "add_candidates",
            pulse: $"arch_board · {slot.Role}/{slot.Id} · +{added.Count} candidates",
            focus: slot.Id);
    }

    static object Elect(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var candKey = Opt(args, "candidate") ?? Opt(args, "candidate_id") ?? Opt(args, "anchor") ?? Opt(args, "label");
        if (candKey is null or { Length: 0 })
            return Err("candidate_required", "op=elect role=ccu candidate=IdOrLabel");

        return Mutate(session, doc =>
        {
            var slot = FindRole(doc, args);
            if (slot is null)
                return (false, Err("role_not_found", "op=elect role=… candidate=…"));

            var cand = slot.Candidates.FirstOrDefault(c =>
                c.Id.Equals(candKey, StringComparison.OrdinalIgnoreCase) ||
                c.Label.Equals(candKey, StringComparison.OrdinalIgnoreCase) ||
                (c.Anchor?.Equals(candKey, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (c.Symbol?.Equals(candKey, StringComparison.OrdinalIgnoreCase) ?? false));
            if (cand is null)
                return (false, Err("candidate_not_found", $"no candidate matching '{candKey}' on {slot.Id}"));

            foreach (var c in slot.Candidates)
                c.Status = c.Id == cand.Id ? "elected" : c.Status == "elected" ? "candidate" : c.Status;

            slot.ElectedCandidateId = cand.Id;
            slot.Status = "elected";
            doc.FocusRoleId = slot.Id;
            return (true, OkCard(session, doc, "elect",
                pulse: $"arch_board · {slot.Role} elect {cand.Label}",
                focus: slot.Id));
        });
    }

    static object Reject(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var doc = Load(session);
        var slot = FindRole(doc, args);
        if (slot is null)
            return Err("role_not_found", "op=reject role=… candidate=…");

        var candKey = Opt(args, "candidate") ?? Opt(args, "candidate_id") ?? Opt(args, "anchor") ?? Opt(args, "label");
        if (candKey is null or { Length: 0 })
            return Err("candidate_required", "op=reject role=ccu candidate=IdOrLabel");

        var cand = slot.Candidates.FirstOrDefault(c =>
            c.Id.Equals(candKey, StringComparison.OrdinalIgnoreCase) ||
            c.Label.Equals(candKey, StringComparison.OrdinalIgnoreCase) ||
            (c.Anchor?.Equals(candKey, StringComparison.OrdinalIgnoreCase) ?? false));
        if (cand is null)
            return Err("candidate_not_found", $"no candidate matching '{candKey}' on {slot.Id}");

        cand.Status = "rejected";
        if (slot.ElectedCandidateId == cand.Id)
        {
            slot.ElectedCandidateId = null;
            slot.Status = slot.Candidates.Any(c => c.Status == "candidate") ? "open" : "open";
        }

        Save(session, doc);
        return OkCard(session, doc, "reject",
            pulse: $"arch_board · {slot.Role} reject {cand.Label}",
            focus: slot.Id);
    }

    static object AddEdge(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var fromKey = Opt(args, "from") ?? Opt(args, "from_role") ?? Opt(args, "from_id");
        var toKey = Opt(args, "to") ?? Opt(args, "to_role") ?? Opt(args, "to_id");
        if (fromKey is null or { Length: 0 } || toKey is null or { Length: 0 })
            return Err("edge_ends_required", "op=edge from=ccu to=channel kind=feeds");

        var kind = NormEdge(Opt(args, "kind") ?? Opt(args, "edge") ?? "feeds");
        if (!EdgeKinds.Contains(kind))
            return Err("unknown_edge_kind", $"kind={kind} — {string.Join('|', EdgeKinds)}");

        var doc = Load(session);
        var from = FindRoleByKey(doc, fromKey);
        var to = FindRoleByKey(doc, toKey);
        if (from is null || to is null)
            return Err("edge_role_missing", "from/to must match role id or role kind already on board");

        var edge = new BoardEdge
        {
            Id = ShortId("e"),
            FromRoleId = from.Id,
            ToRoleId = to.Id,
            Kind = kind
        };
        doc.Edges.Add(edge);
        Save(session, doc);
        return OkCard(session, doc, "edge",
            pulse: $"arch_board · {from.Id} -{kind}→ {to.Id}",
            focus: from.Id);
    }

    static object Promote(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        return Mutate(session, doc =>
        {
            var slot = FindRole(doc, args);
            if (slot is null)
                return (false, Err("role_not_found", "op=promote role=… — or elect first (uses focus)"));

            if (slot.ElectedCandidateId is null)
                return (false, Err("not_elected", "elect a candidate before promote (v0 does not mutate code)"));

            var elected = slot.Candidates.FirstOrDefault(c => c.Id == slot.ElectedCandidateId);
            slot.Status = "promoted";
            doc.FocusRoleId = slot.Id;
            var primary = PromoteGo(slot, elected);
            return (true, OkCard(session, doc, "promote",
                pulse: $"arch_board · promote {slot.Role}/{elected?.Label} (plan only)",
                focus: slot.Id,
                primaryGo: primary));
        });
    }

    static object Clear(SessionContext session)
    {
        return Mutate(session, doc =>
        {
            doc.Roles.Clear();
            doc.Edges.Clear();
            doc.FocusRoleId = null;
            return (true, OkCard(session, doc, "clear", pulse: "arch_board · cleared"));
        });
    }

    static RoleSlot? FindRole(BoardDoc doc, IReadOnlyDictionary<string, JsonElement> args)
    {
        var key = Opt(args, "role_id") ?? Opt(args, "id") ?? Opt(args, "role") ?? Opt(args, "kind");
        if (key is { Length: > 0 })
            return FindRoleByKey(doc, key);

        // Focus / last elected — op=promote with no role= after elect.
        if (doc.FocusRoleId is { Length: > 0 } f
            && FindRoleByKey(doc, f) is { } focused)
            return focused;

        return doc.Roles.LastOrDefault(r => r.Status == "elected")
               ?? doc.Roles.LastOrDefault(r => r.Status == "open");
    }

    static RoleSlot? FindRoleByKey(BoardDoc doc, string key)
    {
        var byId = doc.Roles.FirstOrDefault(r => r.Id.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (byId is not null)
            return byId;

        var role = NormRole(key);
        return doc.Roles.LastOrDefault(r => r.Role.Equals(role, StringComparison.OrdinalIgnoreCase));
    }

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
