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
        var next = new List<object>
        {
            new { go = GoName, label = "Scene", why = "op=scene" },
            new { go = GoName, label = "Add role", why = "op=add_role role=ccu" },
            new { go = GoName, label = "Candidates", why = "op=add_candidates role=ccu anchors=…" },
            new { go = GoName, label = "Elect", why = "op=elect role=ccu candidate=…" },
            new { go = GoName, label = "Edge", why = "op=edge from=ccu to=channel kind=feeds" },
            new { go = GoName, label = "Promote", why = "op=promote role=ccu — plan only v0" }
        };
        if (primaryGo is not null)
            next.Insert(0, primaryGo);

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
            focus_role_id = focus,
            board = new
            {
                title = doc.Title,
                updated_utc = doc.UpdatedUtc,
                roles = doc.Roles.Select(r => new
                {
                    r.Id,
                    r.Role,
                    r.Status,
                    r.Note,
                    elected = r.ElectedCandidateId,
                    candidates = r.Candidates.Select(c => new
                    {
                        c.Id,
                        c.Label,
                        c.Anchor,
                        c.Path,
                        c.Symbol,
                        c.Status
                    }),
                    candidate_count = r.Candidates.Count
                }),
                edges = doc.Edges.Select(e => new
                {
                    e.Id,
                    from = e.FromRoleId,
                    to = e.ToRoleId,
                    e.Kind
                })
            },
            roles_lexicon = RoleLexicon,
            next,
            hint = "Board ≠ code. Candidates = CodeAnchor wires [F:;M:;K:]. add_role → candidates → elect → edge → promote (plan). CIDE: CCU→Channel→CDS→Compositor→Surface."
        };
    }
}
