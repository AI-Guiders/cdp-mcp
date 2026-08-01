#nullable enable
using System.Text;
using System.Text.Json;
using AgentFailures.Core;
using AgentFindings.Core;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Draft/record/list ops for go=postmortem (≤ADX soft-warn peel).</summary>
internal static partial class IdePostmortemChannel
{
    static object Draft(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        bool persist)
    {
        var workspace = ResolveWorkspace(session, args);
        if (workspace is null)
            return Fail("workspace_required", "cdp_open project first, or workspace_path=");

        var draft = BuildDraft(args);
        var scrub = ScrubDraft(draft);
        if (scrub.Refused is { } refuse)
            return new
            {
                schema = SchemaVersion,
                ok = false,
                op = persist ? "record" : "draft",
                go = GoName,
                reason = "ethics_refuse",
                refuse,
                hint = "Integrity exit — rewrite without blame/secrets/chat dump; honesty over silence-as-cover."
            };

        var body = FormatBody(scrub.Draft);
        var fingerprint = scrub.Draft.Fingerprint;
        object? failure = null;
        object? finding = null;
        string? fdrKind = null;

        if (persist)
        {
            var category = ResolveCategory(Opt(args, "category"));
            var view = WorkspaceFailuresStore.Record(
                workspace,
                tool: FailureToolTag,
                errorOrMiss: Truncate(scrub.Draft.Happened, 480),
                argsTried: Truncate(scrub.Draft.WhyRepeated, 480),
                resolution: Truncate(scrub.Draft.Fix, 480),
                correctArgs: Truncate(scrub.Draft.DoNot, 480),
                why: Truncate(scrub.Draft.SystemRoot, 480),
                fingerprint: fingerprint,
                taskId: Opt(args, "task_id"),
                category: category,
                projectId: Opt(args, "project_id"),
                app: "cdp",
                suggestedNext: Truncate(scrub.Draft.DoNot, 240));

            failure = new
            {
                id = view.Record.Id,
                fingerprint = view.Record.Fingerprint,
                deduped = view.Deduped,
                seen_count = view.Record.SeenCount,
                path = WorkspaceFailuresStore.FileForTool(workspace, FailureToolTag)
            };

            var findingPath = FindingPathPrefix + fingerprint + ".md";
            var absFinding = Path.Combine(workspace, findingPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(absFinding)!);
            File.WriteAllText(absFinding, body, Encoding.UTF8);

            var memo = WorkspaceFindingsStore.UpsertMemo(
                workspace,
                path: findingPath,
                contentHash: Sha256Hex(body),
                relevance: "on_task",
                disposition: "leave",
                summary: Truncate(scrub.Draft.Title ?? scrub.Draft.Happened, 200),
                anchors: scrub.Draft.FdrCallId is { Length: > 0 } cid ? "fdr:" + cid : null,
                dependsOnPaths: null,
                taskIds: null,
                status: "active",
                sessionId: null);

            finding = new
            {
                id = memo.Id,
                path = findingPath,
                summary = memo.Summary
            };

            IdeFlightDataRecorder.RecordWake(
                "postmortem",
                scrub.Draft.FdrCallId ?? fingerprint,
                scrub.Draft.Tool ?? FailureToolTag,
                Truncate(scrub.Draft.Title ?? scrub.Draft.Happened, 160));
            fdrKind = "postmortem";
        }

        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = persist ? "record" : "draft",
            go = GoName,
            persisted = persist,
            fingerprint,
            title = scrub.Draft.Title,
            axes = new
            {
                happened = scrub.Draft.Happened,
                system_root = scrub.Draft.SystemRoot,
                why_repeated = scrub.Draft.WhyRepeated,
                fix = scrub.Draft.Fix,
                do_not = scrub.Draft.DoNot
            },
            fdr_call_id = scrub.Draft.FdrCallId,
            tool = scrub.Draft.Tool,
            body_preview = Truncate(body, 1200),
            scrub_notes = scrub.Notes,
            failure,
            finding,
            fdr_kind = fdrKind,
            hint = persist
                ? "Persisted blameless postmortem — failure store + finding memo + FDR. Prefer pulse; do not dump chat."
                : "Draft only — op=record to persist. Review scrub_notes first."
        };
    }

    static object List(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var workspace = ResolveWorkspace(session, args);
        if (workspace is null)
            return Fail("workspace_required", "cdp_open project first, or workspace_path=");

        var limit = OptInt(args, "limit") ?? 20;
        var list = WorkspaceFailuresStore.List(
            workspace,
            tool: FailureToolTag,
            fingerprint: Opt(args, "fingerprint"),
            category: Opt(args, "category") is { Length: > 0 } cat ? ResolveCategory(cat) : null,
            projectId: null,
            app: "cdp",
            taskId: null,
            latestOnly: true,
            limit: Math.Clamp(limit, 1, 100));

        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "list",
            go = GoName,
            count = list.Count,
            entries = list.Select(v => new
            {
                id = v.Record.Id,
                at = v.Record.AtUtc,
                fingerprint = v.Record.Fingerprint,
                happened = Truncate(v.Record.ErrorOrMiss, 160),
                system_root = Truncate(v.Record.Why, 120),
                fix = Truncate(v.Record.Resolution, 120),
                do_not = Truncate(v.Record.CorrectArgs, 120),
                seen = v.Record.SeenCount
            }).ToArray(),
            hint = "Latest postmortems from failure store (tool=cdp_postmortem)."
        };
    }
}
