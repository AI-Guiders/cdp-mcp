#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=qrh</c> (alias <c>eqrh</c>) — electronic Quick Reference Handbook.
/// Systems / abnormal / emergency pages projected on the desk (not cold <c>memory_*</c> thrash).
/// SSOT for narrative remains packs/KB; this organ is the host projector + CAS→page binding.
/// Operator/agent pages: <c>qrh.overlay</c> via <c>qrh add|remove</c> — do not extend Builtins for lessons.
/// Partials: Models · Builtins · Ops · Overlay.
/// </summary>
internal static partial class IdeQrhChannel
{
    public const string SchemaVersion = "qrh_organ/v0";

    public static Suggest SuggestFor(IdeChkChannel.ProbeCtx ctx, IdeChkChannel.Snap? ecl = null)
    {
        var hits = new List<(string Id, int Score)>();
        void Hit(string id, int score)
        {
            var i = hits.FindIndex(h => h.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (i < 0) hits.Add((id, score));
            else if (hits[i].Score < score) hits[i] = (id, score);
        }

        if (ctx.DapStopped) Hit("dap-pdb-lock", 90);
        else if (ctx.DapActive) Hit("dap-pdb-lock", 40);

        if (ctx.GitDirty && (ctx.Phase.Equals("handoff", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(ctx.Intent, "ship", StringComparison.OrdinalIgnoreCase)))
            Hit("ship-dirty", 85);
        else if (ctx.GitDirty) Hit("ship-dirty", 35);

        if (ctx.Phase is "explore" or "clarify" or "recall")
        {
            Hit("intake-brief", 50);
            Hit("find-via-desk", 35);
        }
        if (ctx.Phase is "act")
        {
            // Intentional plateau (ignite idle) stays quiet — Agent Dark Cockpit.
            // Blind Autoi on empty focus is the real deviation.
            if (!ctx.TaskOpen && !ctx.IgniteIdle) Hit("plateau-no-task", 88);
            Hit("path-mutate-gate", 45);
            Hit("find-via-desk", 40);
        }
        if (ctx.Phase is "verify") Hit("test-via-desk", 50);
        if (ctx.Phase is "handoff") Hit("skip-review", 70);
        if (ctx.Phase is "review")
        {
            Hit("skip-review", 20);
            Hit("scm-via-desk", 45);
            Hit("test-via-desk", 40);
        }

        if (ecl is { HotId: { } hot })
        {
            if (hot.Equals("ship", StringComparison.OrdinalIgnoreCase))
            {
                Hit("ship-dirty", 95);
                Hit("scm-via-desk", 55);
            }
            if (hot.Equals("review", StringComparison.OrdinalIgnoreCase))
            {
                Hit("skip-review", 90);
                Hit("scm-via-desk", 60);
                Hit("test-via-desk", 50);
            }
            if (hot.Equals("verify", StringComparison.OrdinalIgnoreCase)) Hit("test-via-desk", 85);
            if (hot.Equals("dap-hold", StringComparison.OrdinalIgnoreCase)) Hit("dap-pdb-lock", 95);
            if (hot.Equals("intake", StringComparison.OrdinalIgnoreCase))
            {
                Hit("intake-brief", 80);
                Hit("find-via-desk", 55);
            }
            if (hot.Equals("mutate", StringComparison.OrdinalIgnoreCase))
            {
                Hit("path-mutate-gate", 80);
                Hit("find-via-desk", 60);
            }
            if (hot.Equals("plateau", StringComparison.OrdinalIgnoreCase)
                && (!ctx.IgniteIdle || ecl.OpenRequired > 0))
            {
                Hit("plateau-no-task", 95);
                Hit("autoignite-cdt", 45);
            }
        }

        ApplyOverlaySuggest(hits, ctx, ecl);

        var ordered = hits.OrderByDescending(h => h.Score).Select(h => h.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var hotId = ordered.FirstOrDefault();
        var pulse = hotId is null ? "qrh · idle" : $"qrh · {hotId}" + (ordered.Length > 1 ? $" +{ordered.Length - 1}" : "");
        return new Suggest(hotId, ordered, pulse);
    }

    public static Snap Build(IdeChkChannel.ProbeCtx ctx, IdeChkChannel.Snap? ecl = null)
    {
        var suggest = SuggestFor(ctx, ecl);
        var pages = AllPages();
        var index = pages.Select(IndexCard).ToArray();
        return new Snap(true, suggest.Pulse, pages.Count, suggest, index);
    }
}

