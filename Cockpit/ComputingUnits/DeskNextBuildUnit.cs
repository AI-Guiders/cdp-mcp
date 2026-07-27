#nullable enable
namespace CdpMcp.Cockpit.ComputingUnits;

/// <summary>CCU: cockpit next[] suggestions (cap 8, dedupe by go).</summary>
public sealed class DeskNextBuildUnit : ICockpitComputeUnit
{
    public const int Cap = 8;

    public readonly record struct NextCard(string Id, string Go, string Label, string Why);

    public readonly record struct Input(
        bool HasProject,
        bool DeskBookmarkExists,
        string? WorkIntentId,
        string? WorkPulse,
        bool AlertBeeping,
        string? AlertPulse,
        bool PressureArmed,
        string? PressurePulse,
        int ChkOpenRequired,
        string? ChkPulse,
        bool PhaseReviewOrVerify,
        bool PhaseIsReview,
        string? QrhHotId,
        string? QrhPulse,
        string? LayoutHint,
        string? LayoutSeatNote,
        int ProblemErrors,
        bool AnyUndo,
        bool AnyClipboard,
        bool AnyNavBack,
        bool QualityEnabled,
        int QualityFail,
        int QualityWarn,
        bool SuggestSniper,
        bool SniperHasHold,
        string? SniperPulse,
        bool ArchHasWork,
        string? ArchPulse,
        string ToolchainPulse,
        bool OnboardHasScan,
        string? OnboardPulse,
        int DiskChangedCount,
        string? FocusId,
        int BufferCount,
        int BufferDirtyCount,
        bool GitDirty,
        int TestFailed,
        bool DebugStopped,
        int ShellRunning);

    public NextCard[] Build(in Input input)
    {
        var list = new List<NextCard>(Cap);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string id, string go, string label, string why)
        {
            if (list.Count >= Cap || !seen.Add(go))
                return;
            list.Add(new NextCard(id, go, label, why));
        }

        if (!input.HasProject)
        {
            Add("n-open", "project_scene", "Project map", "No project — cdp_open / project_scene first");
            if (input.DeskBookmarkExists)
                Add("n-restore", "restore", "Restore Previous", "desk bookmark — project + buffers (not LLM chat)");
            if (input.WorkIntentId is not null)
                Add("n-plan", "plan", "Task Manager", input.WorkPulse ?? input.WorkIntentId);
            else
                Add("n-plan", "plan", "Task Manager", "no plan — feature <name>");
            Add("n-settings", "options", "Tools → Options", "IDE prefs — internet/desk/shell/mcp (not Cursor)");
            return list.ToArray();
        }

        if (input.AlertBeeping)
            Add("n-alert", "alert", "SA board", input.AlertPulse ?? "alert");
        if (input.PressureArmed)
            Add("n-pressure", "pressure", "Pressure prep", input.PressurePulse ?? "pressure");
        if (input.ChkOpenRequired > 0)
            Add("n-ecl", "ecl", "ECL", input.ChkPulse ?? "ecl");
        if (input.PhaseReviewOrVerify)
            Add("n-review", "review", "Review",
                input.PhaseIsReview ? "Judgment board" : "After verify — judgment before ship");
        if (input.QrhHotId is { Length: > 0 })
            Add("n-qrh", "qrh", "eQRH", input.QrhPulse ?? "qrh");
        if (input.LayoutHint is { Length: > 0 } layoutHint)
            Add("n-layout", "layout", $"Layout {layoutHint}",
                $"cmd=\"layout {layoutHint}\" — {input.LayoutSeatNote ?? layoutHint}");
        if (input.ProblemErrors > 0)
            Add("n-problems", "problems", "Error List", $"E×{input.ProblemErrors} — aim row, don't dump");

        Add("n-goto", "goto", "Go To (Ctrl+T)", "query= type/member/file — land on anchor");
        Add("n-editor", "editor_scene", "Editor map", "Buffer/desk loop");

        if (input.DeskBookmarkExists)
            Add("n-restore", "restore", "Restore Previous", "desk bookmark — usually auto on cold tools");
        Add("n-deploy", "deploy", "Deploy", "hard → sibling install; dry_run= to preview");

        if (input.AnyUndo)
            Add("n-undo", "undo", "Undo last edit", "buffer edit stack");
        if (input.AnyClipboard)
            Add("n-clipboard", "clipboard", "Clipboard", "frames — pick frame= + paste");
        if (input.AnyNavBack)
            Add("n-back", "back", "Nav back", "locus stack");

        if (input is { QualityEnabled: true, QualityFail: > 0 })
            Add("n-quality", "quality", "Quality gates", $"FAIL×{input.QualityFail} — harness next step");
        else if (input is { QualityEnabled: true, QualityWarn: > 0 })
            Add("n-quality", "quality", "Quality gates", $"WARN×{input.QualityWarn} — review or tune overlay");

        if (input.SuggestSniper && !input.SniperHasHold)
            Add("n-scope", "scope", "Sniper aim", "Large open file — aim corridor before thick edit");

        if (input.ArchHasWork)
            Add("n-arch", "arch_desk", "Arch board", input.ArchPulse ?? "arch");

        Add("n-toolchain", "toolchain", "Toolchain", input.ToolchainPulse);

        if (input.OnboardHasScan)
            Add("n-onboard", "onboard_desk", "Onboard map", input.OnboardPulse ?? "onboard");
        else
            Add("n-onboard", "onboard_desk", "Onboard scan", "op=scan — cold-start map of ProjectRoot");

        if (input.DiskChangedCount > 0)
        {
            Add("n-disk-peek", "disk_peek", "Peek disk vs memory",
                "Glance before Reload? (mtime / content)");
            Add("n-reload", "reload", "Reload from disk",
                $"{input.DiskChangedCount} file(s) changed outside — like VS Reload?");
            Add("n-keep-disk", "keep_disk", "Keep memory",
                input.FocusId is { Length: > 0 }
                    && input.FocusId.StartsWith("buffer:", StringComparison.OrdinalIgnoreCase)
                    ? $"Don't Reload — locus {input.FocusId} → path="
                    : "Don't Reload — silence all drifted (or path= / locus=buffer:…)");
        }

        if (input.SniperHasHold)
        {
            Add("n-target", "target", "Outline corridor", $"Aim {input.SniperPulse}");
            Add("n-peek", "peek", "Peek aim", "wire= optional; corridor window");
            if (input.AnyClipboard)
                Add("n-paste-sniper", "paste_sniper", "Paste frame into aim", "MRU/frame= replace hold");
            Add("n-put-sniper", "put_sniper", "Put draft into aim", "text=/frame= thick rewrite");
            Add("n-edit-draft", "edit_draft", "Shoot (draft)", "mutate/fix inside aim");
            Add("n-scope-clear", "scope_clear", "Clear aim", "drop From/Till");
        }
        else
        {
            Add("n-scope", "scope", "Sniper aim", "from=/till= corridor before outline");
            Add("n-put", "put", "Put draft file", "path= + text=/frame= — one-shot dump");
            if (input.BufferCount > 0)
            {
                Add("n-share", "share", "Share with operator", "inbox file + thin chat= (not into agent)");
                Add("n-take", "take", "Take into agent", "rare — body + chat_markdown into context");
            }
        }

        if (input.BufferCount > 0 && !input.SniperHasHold)
            Add("n-edit-draft", "edit_draft", "Edit plan draft",
                $"Open buffers={input.BufferCount} dirty={input.BufferDirtyCount}");
        else if (input.BufferCount == 0 && !input.SniperHasHold)
            Add("n-buffer", "buffer_scene", "Buffer scene", "No open buffers yet");

        Add("n-script", "script_scene", "Script habitat", "put→diags→check→run");
        Add("n-ps1", "ps1_scene", "PS ISE habitat", "put→AST→pwsh -File");

        if (input.GitDirty)
            Add("n-git-draft", "git_draft", "Git plan draft", "Dirty SCM — logical slices");
        else
            Add("n-git", "git_scene", "Git scene", "SCM map");

        if (input.TestFailed > 0)
            Add("n-test-plan", "test_plan", "Retest failed", "last_run has failures");
        else
            Add("n-test", "test_scene", "Test scene", "Discover / last_run");

        if (input.DebugStopped)
            Add("n-debug", "debug_scene", "Debug scene", "DAP stopped — stop_context via organ");
        else
            Add("n-shell", "shell_scene", "Shell habitat",
                input.ShellRunning > 0 ? "jobs running" : "tabs map");

        Add("n-settings", "options", "Tools → Options", "IDE prefs — internet/desk/shell/mcp (not Cursor)");

        if (input.FocusId is { Length: > 0 }
            && input.FocusId.StartsWith("buffer:", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(input.FocusId, "buffer:none", StringComparison.OrdinalIgnoreCase))
            Add("n-focus-editor", "editor_scene", "Focus editor context", $"locus {input.FocusId}");

        if (input.WorkIntentId is not null)
            Add("n-plan", "plan", "Task Manager", input.WorkPulse ?? input.WorkIntentId);
        else
            Add("n-plan", "plan", "Task Manager", "no plan — feature <name>");

        if (input.ChkOpenRequired == 0)
            Add("n-ecl", "ecl", "ECL", "go=ecl");

        return list.ToArray();
    }
}
