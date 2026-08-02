#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>cdp_context mutate/get for MetaDispatch.Core (method_lines peel).</summary>
internal static partial class MetaDispatch
{
    static string ContextJson(MetaDispatchDeps d, IReadOnlyDictionary<string, JsonElement> callArgs)
    {
        var session = d.Session;
        var settings = d.Settings;
        var NotifyListChanged = d.NotifyListChanged;
        var EnsureWorkspaceDb = d.EnsureWorkspaceDb;

        if (callArgs.TryGetValue("get", out var g) && g.ValueKind == JsonValueKind.True)
            return session.ToJson();

        var changed = false;
        string? layoutApplied = null;
        if (callArgs.TryGetValue("phase", out var ph) && CdpEnumParse.TryParsePhase(ph.GetString(), out var newPhase))
        {
            var oldPhaseWire = CdpEnumParse.ToWire(session.Phase);
            var phaseChanged = newPhase != session.Phase;
            session.Phase = newPhase;
            changed = true;
            if (phaseChanged)
            {
                EnsureWorkspaceDb();
                IdeStageCycle.TryPhaseTransition(oldPhaseWire, CdpEnumParse.ToWire(newPhase));
                layoutApplied = IdePhaseLayout.TryApplyForPhase(newPhase, callArgs);
            }
        }

        if (callArgs.TryGetValue("object", out var ob) && CdpEnumParse.TryParseObject(ob.GetString(), out var newObj))
        {
            session.Object = newObj;
            changed = true;
        }

        if (callArgs.TryGetValue("intent", out var it))
        {
            var s = it.GetString();
            if (string.IsNullOrWhiteSpace(s))
                session.Intent = null;
            else if (CdpEnumParse.TryParseIntent(s, out var newIntent))
                session.Intent = newIntent;
            changed = true;
        }

        if (callArgs.TryGetValue("language", out var langEl))
        {
            var ls = langEl.GetString();
            if (string.IsNullOrWhiteSpace(ls))
                session.Language = null;
            else if (settings.Languages.TryNormalize(ls, out var newLang))
                session.Language = CdpLanguages.IsAny(newLang) ? null : newLang;
            changed = true;
        }

        if (changed)
            NotifyListChanged();

        var ctxTail = changed ? "\n# list_changed: shortlist refreshed for new context" : "";
        if (layoutApplied is { Length: > 0 })
            ctxTail += $"\n# desk_layout: {layoutApplied} (phase SA; hold=layout_hold|desk.layout.hold)";
        else if (changed
                 && callArgs.ContainsKey("phase")
                 && IdePhaseLayout.IsHold(callArgs))
            ctxTail += "\n# desk_layout: held";

        return session.ToJson() + ctxTail;
    }
}
