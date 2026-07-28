#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=qrh</c> (alias <c>eqrh</c>) — electronic Quick Reference Handbook.
/// Systems / abnormal / emergency pages projected on the desk (not cold <c>memory_*</c> thrash).
/// SSOT for narrative remains packs/KB; this organ is the host projector + CAS→page binding.
/// </summary>
internal static class IdeQrhChannel
{
    public const string SchemaVersion = "qrh_organ/v0";

    public sealed record Step(string Text, string? Go = null, string? Action = null);

    public sealed record Page(
        string Id,
        string Shelf, // systems | abnormal | emergency
        string Title,
        string Condition,
        IReadOnlyList<string> Signals,
        IReadOnlyList<string> MemoryItems,
        IReadOnlyList<Step> Steps,
        IReadOnlyList<string> Related,
        IReadOnlyList<string> PackAnchors,
        string? LlmCue = null);

    public sealed record Suggest(
        string? HotId,
        IReadOnlyList<string> RelatedIds,
        string Pulse);

    public sealed record Snap(
        bool Ok,
        string Pulse,
        int PageCount,
        Suggest Suggest,
        IReadOnlyList<object> Index);

    public static IReadOnlyList<Page> Builtins() =>
    [
        new(
            "dap-pdb-lock",
            "emergency",
            "DAP holds PDB (rebuild blocked)",
            "netcoredbg/DAP has target stopped or PDB open — rebuild fails with file locked.",
            ["dap.stopped", "dap.active", "rebuild", "pdb"],
            ["debug_stop before rebuild", "do not taskkill netcoredbg from outside"],
            [
                new("cdp_debug_sa / go=debug_desk — fuse before act", "debug_desk", "cdp_debug_sa"),
                new("cdp_debug op=stop_context — evidence before guess", "debug", "cdp_debug"),
                new("cdp_debug / debug_stop — release PDB", "debug", "cdp_debug"),
                new("Then rebuild / cdp_build", "build", "cdp_build")
            ],
            ["deploy-sibling", "path-mutate-gate"],
            ["procedure:mutate-plan-then-act"],
            "Is DAP still holding the binary — or am I fighting a ghost lock?"),
        new(
            "plateau-no-task",
            "abnormal",
            "Act phase, but TM has no active task",
            "Cockpit is in act, but Task Manager says (pick task). AutoIgnition or self-wake without an authorized task turns continuity into empty loops and false urgency.",
            ["phase:act", "task.none", "pick task", "plateau", "ignite", "authorized"],
            [
                "No invented TM stage just to satisfy wake",
                "Pressure stash may remember plateau, but does not authorize new work",
                "Re-arm ignite only after a real task exists"
            ],
            [
                new("cdp_cockpit go=plan — inspect TM focus", "plan"),
                new("Either focus/create the next task, or leave plateau explicit", "plan"),
                new("If no task exists, disarm or park ignite instead of blind 8s loops", "ignite_desk", "cdp_ignite"),
                new("Stash plateau invariant in pressure if continuity matters", "pressure", "cdp_pressure")
            ],
            ["intake-brief", "autoignite-cdt", "path-mutate-gate"],
            [],
            "Is this a real authorized next step — or am I manufacturing motion because the habitat can wake itself?"),
        new(
            "path-mutate-gate",
            "abnormal",
            "Host Read/Write bypassed desk",
            "Cursor Write skips PathMutateGate; Cursor Read dumps file bodies into chat context (~1%/read). Thick set_text on large files = thrash — prefer edit_op=anchor / go=scope sniper.",
            ["buffer", "write", "read", "mutate", "path_mutate", "context", "set_text", "thrash"],
            [
                "Prefer cdp_buffer over Cursor Write on project paths",
                "Prefer cdp_buffer open/find/read over Cursor Read (context tax)",
                "Large file: anchor/sniper — not whole-file set_text"
            ],
            [
                new("cdp_buffer op=open|edit (anchor) — mutate SSOT", "buffer", "cdp_buffer"),
                new("cdp_buffer find/read — peek without host Read dump", "buffer", "cdp_buffer"),
                new("If large file: go=scope → sniper before thick edit", "scope", "cdp_edit_sniper"),
                new("disk_peek / reload if outside change", "disk_peek", "cdp_buffer")
            ],
            ["intake-brief", "ship-dirty", "test-via-desk", "scm-via-desk", "find-via-desk"],
            ["procedure:mutate-plan-then-act", "definition:blast-radius"],
            "Did this go through the buffer plane — or around the desk into chat context?"),
        new(
            "ship-dirty",
            "abnormal",
            "Verified work, dirty tree, no ship",
            "phase handoff / intent ship with uncommitted changes — ECL ship open.",
            ["git.dirty", "phase:handoff", "intent:ship", "commit", "push"],
            ["No secrets/.env in slices", "SCM via desk — not shell status/diff/log"],
            [
                new("cdp_crm / go=crm — Approved|Go Around|Hold (not chat reject)", "crm", "cdp_crm"),
                new("cdp_build_sa / go=build_desk — fuse before ship", "build_desk", "cdp_build_sa"),
                new("git_preflight — classify noise", Action: "git_preflight"),
                new("git_plan draft→validate→apply — logical commits", Action: "git_plan"),
                new("git_push when operator asked (or ECL ack defer)", Action: "git_push"),
                new("go=ecl — track ship checklist", "ecl")
            ],
            ["intake-brief", "path-mutate-gate", "scm-via-desk"],
            [],
            "Is ship still open because dirty — or because I never opened ECL?"),
        new(
            "deploy-sibling",
            "systems",
            "Hard deploy without killing self",
            "Dual-seat CDP: KillRunning on self from in-proc shell kills the MCP mid-flight.",
            ["deploy", "sibling", "remount", "killrunning"],
            ["go=deploy from survivor seat (sibling Target)", "never hard-deploy self from inside cdp_shell_*"],
            [
                new("cdp_deploy target=sibling (or go=deploy)", "deploy", "cdp_deploy"),
                new("Expect Autoi 'MCP remounted / initialized' after remount", Action: "cdp_ignite"),
                new("cdp_health — confirm version", "health", "cdp_health"),
                new("Reorient desk: cdp_cockpit / restore", "restore")
            ],
            ["remount-after-deploy", "dap-pdb-lock"],
            [],
            "Am I deploying onto the seat that is running this turn?"),
        new(
            "remount-after-deploy",
            "abnormal",
            "After deploy — stale tools / old version",
            "Hard deploy nudged MCP JSON; Cursor may still talk to old process until remount.",
            ["remount", "version", "nudge", "stale"],
            [],
            [
                new("cdp_health — version_full vs expected", "health", "cdp_health"),
                new("If no Autoi initialized wake: check remount-wake-*.pending.json", Action: "cdp_ignite"),
                new("If stale: human Reload MCP or wait nudge", Action: "cdp_health"),
                new("cdp_session / cockpit — warm desk", "cockpit")
            ],
            ["deploy-sibling"],
            [],
            "Does health show the version I just published?"),
        new(
            "intake-brief",
            "systems",
            "What+why before explore thrash",
            "New ask / fuzzy scope — explore-as-stall without named outcome.",
            ["phase:explore", "phase:clarify", "intake", "what", "why"],
            ["Name what+why (or ask) before thrash"],
            [
                new("Paraphrase what they want (one line)", Action: "memory_world_get_procedure"),
                new("Name done / success or honest unknown → ask"),
                new("Only then explore — go=ecl intake", "ecl"),
                new("Pack: intake-brief-plan", Action: "memory_world_get_procedure")
            ],
            ["path-mutate-gate", "ship-dirty", "find-via-desk"],
            ["procedure:intake-brief-plan", "definition:harness-model-first"],
            "Did I name what+why — or start exploring to avoid admitting the ask is fuzzy?"),
        new(
            "skip-review",
            "abnormal",
            "Verify → handoff without review",
            "Machine green (or dirty tree) and jumping to ship — judgment board never opened.",
            ["phase:handoff", "phase:review", "review", "judgment", "ship"],
            ["cdp_context phase=review before ship", "go=review — file cards + judgment lane"],
            [
                new("cdp_context phase=review", Action: "cdp_context"),
                new("go=review — machine + judgment + files", "review"),
                new("go=ecl — review checklist", "ecl"),
                new("Only then phase=handoff / ship", "ecl")
            ],
            ["ship-dirty", "intake-brief", "scm-via-desk"],
            [],
            "Did verify prove green — or did I skip the judgment gate?"),
        new(
            "scm-via-desk",
            "abnormal",
            "Shell used for SCM archaeology",
            "status/diff/log/commit prep via shell while CDP git organs are available — desk bypass.",
            ["git status", "git diff", "git log", "shell", "scm", "git_plan", "git_scene"],
            ["Prefer git_scene / git_plan / git MCP over shell for SCM", "Shell only if git MCP dead"],
            [
                new("git_scene — status/dirty", Action: "git_scene"),
                new("git_plan — logical slices draft→validate→apply", Action: "git_plan"),
                new("git_preflight — classify noise/secrets", Action: "git_preflight"),
                new("go=ecl — review/ship memory scm-desk", "ecl")
            ],
            ["ship-dirty", "skip-review", "path-mutate-gate", "test-via-desk"],
            [],
            "Am I reading git through the desk — or reinventing archaeology in shell?"),
        new(
            "test-via-desk",
            "abnormal",
            "Shell used for test archaeology",
            "dotnet test / list-tests via shell while cdp_test_scene / cdp_test / cdp_test_plan are available — desk bypass.",
            ["dotnet test", "list-tests", "shell", "test", "cdp_test", "cdp_test_scene"],
            ["Prefer cdp_test_scene → cdp_test / cdp_test_plan over shell", "Shell only if test plane dead"],
            [
                new("cdp_test_sa / go=test_desk — fuse last_run", "test_desk", "cdp_test_sa"),
                new("cdp_test_scene — map FQNs + last_run", Action: "cdp_test_scene"),
                new("cdp_test_plan preview|apply — select then run", Action: "cdp_test_plan"),
                new("cdp_test filter= — targeted run", Action: "cdp_test"),
                new("go=ecl — verify/review memory tests-desk", "ecl")
            ],
            ["scm-via-desk", "path-mutate-gate", "skip-review", "tool-result-tax"],
            [],
            "Am I running tests through the desk — or reinventing archaeology in shell?"),
        new(
            "tool-result-tax",
            "abnormal",
            "Thick tool-result burned chat context",
            "deploy/test/shell returned full tails or warning dumps into Conversation — desk does not strip host tool payloads.",
            ["context", "stdout_tail", "include_raw", "evidence", "tool-result", "CS8600"],
            ["Prefer pulse + locus; include_raw only when needed", "cdp_test/build default slim evidence"],
            [
                new("Read pulse / exit / failed_tests / evidence.items[locus] — not full tail"),
                new("include_raw_output / include_raw only for diagnosis"),
                new("cdp_shell_last max_chars= only when AgentBodyChars insufficient", Action: "cdp_shell_last"),
                new("go=qrh open path-mutate-gate — host Read tax", "qrh")
            ],
            ["path-mutate-gate", "test-via-desk", "scm-via-desk", "find-via-desk"],
            [],
            "Did I need the whole pipe — or just the locus?"),
        new(
            "find-via-desk",
            "abnormal",
            "Shell used for grep/rg archaeology",
            "rg/grep/findstr via shell (or Cursor Grep) while cdp_search / cdp_buffer find (buffer|project|external) / codebase_index are available — desk bypass + thick stdout tax.",
            ["rg", "grep", "findstr", "shell", "search", "find", "codebase_index"],
            ["Prefer cdp_search (what/where/shape) or cdp_buffer find scope=project|external over shell/Cursor Grep", "Index when corpus large; shell only if desk find dead"],
            [
                new("cdp_search query= where=project — slim desk hits", Action: "cdp_search"),
                new("cdp_search where=external path= — any disk tree", Action: "cdp_search"),
                new("cdp_search where=dirty — SCM changed files only", Action: "cdp_search"),
                new("cdp_sa path= — pre-refactor SA verdict", Action: "cdp_sa"),
                new("cdp_buffer op=find scope=buffer — in open file", "buffer", "cdp_buffer"),
                new("codebase_index_search — FTS when indexed", Action: "codebase_index_search"),
                new("go=ecl — intake/mutate memory find-desk", "ecl")
            ],
            ["path-mutate-gate", "tool-result-tax", "scm-via-desk", "test-via-desk"],
            [],
            "Am I searching through the desk — or reinventing archaeology in shell?"),
        new(
            "files-via-desk",
            "abnormal",
            "Shell used for filesystem browse",
            "ls/dir/Get-ChildItem/tree via shell while cdp_files / go=files_desk are available — desk bypass. FM is a utility (project|external), not project-only.",
            ["ls", "dir", "Get-ChildItem", "gci", "tree", "shell", "files", "explorer", "cdp_files"],
            ["Prefer cdp_files / go=files_desk (where=project|external) over shell ls", "Shell only if files plane dead"],
            [
                new("cdp_files / go=files_desk — scene|list|cd|open", "files_desk", "cdp_files"),
                new("cdp_files where=external path= — any disk tree", Action: "cdp_files"),
                new("op=search → find_desk facet", "find_desk", "cdp_search"),
                new("go=ecl — memory files-desk", "ecl")
            ],
            ["find-via-desk", "path-mutate-gate", "tool-result-tax", "scm-via-desk"],
            [],
            "Am I browsing through the desk — or reinventing archaeology in shell?"),
        new(
            "autoignite-cdt",
            "abnormal",
            "Need overnight Composer turn without operator",
            "AutoIgnition: inject user message into open Cursor chat via Chrome DevTools (CDT port 9222). Prefer ARM in harness — op=arm when=build_finished|timer — not shell loops / UIA. Never click Voice/Stop.",
            ["ignite", "autoignite", "cdt", "composer", "overnight", "inject", "cdp_ignite", "arm", "build_finished"],
            ["cdp_ignite op=arm when=… message=/task=", "Kick cdp_build then end turn — harness fires", "No Cursor Shell watchers for wake"],
            [
                new("cdp_ignite op=arm when=build_finished task=", "ignite_desk", "cdp_ignite"),
                new("cdp_ignite op=arm when=timer in=5m message=", Action: "cdp_ignite"),
                new("cdp_ignite op=list|disarm — inspect/cancel", Action: "cdp_ignite"),
                new("tools/Start-Cursor-WithCdt.ps1 — CDT :9222")
            ],
            ["tool-result-tax", "scm-via-desk"],
            [],
            "Am I igniting Composer via CDT desk — or reinventing UIA?"),
        new(
            "webcam-via-desk",
            "abnormal",
            "Need camera/mic/screen sense",
            "Webcam sense belongs in CDP cockpit (cdp_webcam / go=webcam_desk) via AIGuiders.WebcamMcp.Shared in-proc — not parked Cursor webcam-mcp and not ffmpeg shell.",
            ["webcam", "camera", "mic", "screen", "capture", "sense", "cdp_webcam"],
            ["cdp_webcam op=frame|ocr", "go=webcam_desk", "Burst/analyze/transcribe — next slice"],
            [
                new("cdp_webcam / go=webcam_desk — scene|frame|ocr", "webcam_desk", "cdp_webcam"),
                new("op=ocr images_dir= — tesseract in-proc", Action: "cdp_webcam"),
                new("op=frame file_name= — snap to .cascade-ide/webcam-captures", Action: "cdp_webcam")
            ],
            ["autoignite-cdt", "tool-result-tax"],
            [],
            "Am I sensing through the desk — or reinventing capture in shell?"),
        new(
            "barriers-fail",
            "emergency",
            "Barriers failed — core integrity",
            "Procedural barriers down; do not improvise harm. Fall back to integrity core.",
            ["integrity", "barriers", "harm", "jailbreak", "post"],
            ["Do not help with harm / security bypass / weaponized content"],
            [
                new("Refuse once; no debate loop"),
                new("Pack: core-when-barriers-fail", Action: "memory_world_get_definition"),
                new("If life/health risk — local emergency services, no use instructions")
            ],
            [],
            ["definition:core-when-barriers-fail"],
            "Are barriers intact? If not — apply core principles, do not improvise harm.")
    ];

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

        var ordered = hits.OrderByDescending(h => h.Score).Select(h => h.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var hotId = ordered.FirstOrDefault();
        var pulse = hotId is null ? "qrh · idle" : $"qrh · {hotId}" + (ordered.Length > 1 ? $" +{ordered.Length - 1}" : "");
        return new Suggest(hotId, ordered, pulse);
    }

    public static Snap Build(IdeChkChannel.ProbeCtx ctx, IdeChkChannel.Snap? ecl = null)
    {
        var suggest = SuggestFor(ctx, ecl);
        var index = Builtins().Select(IndexCard).ToArray();
        return new Snap(true, suggest.Pulse, Builtins().Count, suggest, index);
    }

    public static object Handle(
        IdeChkChannel.ProbeCtx ctx,
        IReadOnlyDictionary<string, JsonElement>? args = null,
        IdeChkChannel.Snap? ecl = null)
    {
        var merged = FlattenArgs(args);
        var op = (Opt(merged, "op") ?? Opt(merged, "pulse") ?? "index").Trim().ToLowerInvariant();
        var suggest = SuggestFor(ctx, ecl);

        if (op is "index" or "list" or "catalog" or "scene")
            return Board(null, suggest, action: null, mode: "index");

        if (op is "shelf" or "section")
        {
            var shelf = (Opt(merged, "shelf") ?? Opt(merged, "section") ?? Opt(merged, "id") ?? "").Trim();
            var pages = Builtins()
                .Where(p => p.Shelf.Equals(shelf, StringComparison.OrdinalIgnoreCase))
                .Select(IndexCard)
                .ToArray();
            return new
            {
                ok = true,
                go = "qrh",
                schema = SchemaVersion,
                mode = "shelf",
                shelf,
                pulse = $"qrh · {shelf} ×{pages.Length}",
                title = "eQRH",
                pages,
                suggest = SuggestCard(suggest),
                hint = "op=open id=… | op=search q=…"
            };
        }

        if (op is "search" or "find" or "q")
        {
            var q = (Opt(merged, "q") ?? Opt(merged, "query") ?? Opt(merged, "id") ?? "").Trim();
            if (q.Length == 0)
                return Err("q_required", "qrh search q=pdb | qrh open dap-pdb-lock");
            var hits = Search(q);
            object? opened = hits.Count == 1 ? PageCard(hits[0]) : null;
            return new
            {
                ok = true,
                go = "qrh",
                schema = SchemaVersion,
                mode = hits.Count == 1 ? "open" : "search",
                pulse = hits.Count == 1 ? $"qrh · {hits[0].Id}" : $"qrh · search ×{hits.Count}",
                title = "eQRH",
                query = q,
                hits = hits.Select(IndexCard).ToArray(),
                page = opened,
                suggest = SuggestCard(suggest),
                hint = hits.Count == 0 ? "No page — try qrh index" : "op=open id=…"
            };
        }

        if (op is "open" or "page" or "show" or "get")
        {
            var id = Opt(merged, "id") ?? Opt(merged, "page") ?? Opt(merged, "name") ?? suggest.HotId;
            if (id is not { Length: > 0 })
                return Err("id_required", "qrh open dap-pdb-lock | qrh open (uses SA suggest)");
            var page = Find(id);
            if (page is null)
            {
                var hits = Search(id);
                if (hits.Count == 1) page = hits[0];
            }

            if (page is null)
                return Err("not_found", $"No QRH page '{id}' — qrh index");

            return Board(page, suggest, action: new { ok = true, op = "open", id = page.Id }, mode: "open");
        }

        if (op is "related" or "suggest")
        {
            var from = Opt(merged, "id") ?? Opt(merged, "from") ?? suggest.HotId;
            var page = from is { Length: > 0 } ? Find(from) : null;
            var related = page is null
                ? suggest.RelatedIds.Select(Find).Where(p => p is not null).Cast<Page>().ToArray()
                : page.Related.Select(Find).Where(p => p is not null).Cast<Page>().ToArray();
            return new
            {
                ok = true,
                go = "qrh",
                schema = SchemaVersion,
                mode = "related",
                pulse = $"qrh · related ×{related.Length}",
                from,
                pages = related.Select(IndexCard).ToArray(),
                suggest = SuggestCard(suggest),
                hint = "op=open id=…"
            };
        }

        return Err("unknown_op", "op=index|open|search|shelf|related");
    }

    static object Board(Page? page, Suggest suggest, object? action, string mode)
    {
        var related = page is null
            ? Array.Empty<object>()
            : page.Related.Select(Find).Where(p => p is not null).Cast<Page>().Select(IndexCard).ToArray();

        return new
        {
            ok = true,
            go = "qrh",
            schema = SchemaVersion,
            mode,
            pulse = page is null ? suggest.Pulse : $"qrh · {page.Id}",
            title = "eQRH",
            note = "Electronic QRH — systems / abnormal / emergency. Pack cards via anchors; desk projector, not memory_* thrash.",
            page = page is null ? null : PageCard(page),
            related,
            index = Builtins().Select(IndexCard).ToArray(),
            suggest = SuggestCard(suggest),
            shelves = new[] { "systems", "abnormal", "emergency" },
            action,
            hint = "CCL: qrh | qrh open dap-pdb-lock | qrh search pdb | qrh shelf emergency"
        };
    }

    static object IndexCard(Page p) => new
    {
        id = p.Id,
        shelf = p.Shelf,
        title = p.Title,
        condition = Trunc(p.Condition, 96),
        signals = p.Signals
    };

    static object PageCard(Page p) => new
    {
        id = p.Id,
        shelf = p.Shelf,
        title = p.Title,
        condition = p.Condition,
        signals = p.Signals,
        memory_items = p.MemoryItems,
        steps = p.Steps.Select(s => new { text = s.Text, go = s.Go, action = s.Action }).ToArray(),
        related = p.Related,
        pack_anchors = p.PackAnchors,
        llm_cue = p.LlmCue
    };

    static object SuggestCard(Suggest s) => new
    {
        hot = s.HotId,
        related = s.RelatedIds,
        pulse = s.Pulse
    };

    public static Page? Find(string id) =>
        Builtins().FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<Page> Search(string q)
    {
        var needle = q.Trim();
        if (needle.Length == 0) return [];
        bool Match(Page p) =>
            p.Id.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || p.Title.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || p.Condition.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || p.Shelf.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || p.Signals.Any(s => s.Contains(needle, StringComparison.OrdinalIgnoreCase))
            || p.MemoryItems.Any(m => m.Contains(needle, StringComparison.OrdinalIgnoreCase))
            || p.PackAnchors.Any(a => a.Contains(needle, StringComparison.OrdinalIgnoreCase));

        return Builtins().Where(Match).ToArray();
    }

    static string Trunc(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

    static object Err(string error, string hint) => new
    {
        ok = false,
        go = "qrh",
        schema = SchemaVersion,
        error,
        hint
    };

    static Dictionary<string, JsonElement> FlattenArgs(IReadOnlyDictionary<string, JsonElement>? args)
    {
        var d = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (args is null) return d;
        foreach (var kv in args) d[kv.Key] = kv.Value;
        if (args.TryGetValue("go_args", out var ga) && ga.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in ga.EnumerateObject())
                d[p.Name] = p.Value.Clone();
        }

        return d;
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }
}
