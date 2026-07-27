#nullable enable
using Cdp.Core;

namespace CdpMcp;

internal static partial class IdeArchBoardChannel
{
    static string Pulse(BoardDoc doc)
    {
        if (doc.Roles.Count == 0)
            return "arch_board · empty";
        var open = doc.Roles.Count(r => r.Status == "open");
        var elected = doc.Roles.Count(r => r.Status == "elected");
        var promoted = doc.Roles.Count(r => r.Status == "promoted");
        return $"arch_board · {doc.Roles.Count} roles · open={open} elect={elected} promo={promoted} · edges={doc.Edges.Count}";
    }

    static object OkCard(
        SessionContext session,
        BoardDoc doc,
        string op,
        string pulse,
        string? focus = null,
        object? primaryGo = null)
    {
        if (focus is { Length: > 0 })
            doc.FocusRoleId = focus;

        var next = BuildNextHints(doc, focus, primaryGo);

        return new
        {
            ok = true,
            schema = SchemaVersion,
            go = GoName,
            tool = ToolName,
            op,
            pulse,
            detail = "full",
            board_path = LatestPath(session),
            focus_role_id = focus ?? doc.FocusRoleId,
            board = new
            {
                title = doc.Title,
                updated_utc = doc.UpdatedUtc,
                focus_role_id = doc.FocusRoleId,
                roles = doc.Roles.Select(r => new
                {
                    id = r.Id,
                    role = r.Role,
                    status = r.Status,
                    note = r.Note,
                    elected = r.ElectedCandidateId,
                    candidates = r.Candidates.Select(c => new
                    {
                        id = c.Id,
                        label = c.Label,
                        anchor = c.Anchor,
                        path = c.Path,
                        symbol = c.Symbol,
                        status = c.Status
                    }),
                    candidate_count = r.Candidates.Count
                }),
                edges = doc.Edges.Select(e => new
                {
                    id = e.Id,
                    from = e.FromRoleId,
                    to = e.ToRoleId,
                    kind = e.Kind
                })
            },
            roles_lexicon = RoleLexicon,
            next,
            hint = "Board ≠ code. Soft organ: go=arch_desk / layout=arch (M seat). Candidates = [F:;M:;K:]. elect → op=promote (focus). CIDE: CCU→Channel→CDS|IDS→Compositor→Surface."
        };
    }

    static List<object> BuildNextHints(BoardDoc doc, string? focus, object? primaryGo)
    {
        var next = new List<object>();
        if (primaryGo is not null)
            next.Add(primaryGo);

        var slot = focus is { Length: > 0 }
            ? FindRoleByKey(doc, focus)
            : (doc.FocusRoleId is { Length: > 0 } ? FindRoleByKey(doc, doc.FocusRoleId) : null);

        if (slot is { Status: "elected" })
        {
            next.Add(new
            {
                go = GoName,
                label = $"Promote {slot.Id}",
                why = "op=promote — focus role (no role= needed)"
            });
        }
        else if (slot is { Status: "open", Candidates.Count: > 0 })
        {
            next.Add(new
            {
                go = GoName,
                label = $"Elect on {slot.Id}",
                why = $"op=elect role={slot.Id} candidate=IdOrLabel"
            });
        }

        next.Add(new { go = GoName, label = "Scene", why = "op=scene" });
        next.Add(new { go = GoName, label = "Add role", why = "op=add_role role=ccu|channel|cds|ids|…" });
        next.Add(new { go = GoName, label = "Candidates", why = "op=add_candidates role=… anchors=…" });
        next.Add(new { go = "layout", label = "Layout arch", why = "cmd=\"layout arch\" — M=arch_desk" });
        return next;
    }
}
