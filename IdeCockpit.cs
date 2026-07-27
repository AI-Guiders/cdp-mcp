using System.Text.Json;
using Cdp.Core;
using CdpMcp.Cockpit.ComputingUnits;
using CdpMcp.Cockpit.Surface;
using CdpMcp.IntentWorkspace;
using DotNetBuildTest.Core;
using DotnetDebug.Core;
using DotnetDebugMcp;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>
/// Agent IDE cockpit — seats desk + soft organs (ADR 0191/0193).
/// Legacy MFD aliases: go=sys|chk|gates; desk_detail=nav. <c>cmd=</c> REPL; <c>go=</c> places organ in seat.
/// </summary>
internal static partial class IdeCockpit
{
    public const string SchemaVersion = "cockpit/v1.20";
    public const int GoResultCapChars = 24_000;
    public const int GoPulseCapChars = 1_200;
    public const int MaxTiles = 4;

    /// <summary>Exposed for Tools → Options desk.default_layout choices.</summary>
    public static string[] LayoutPresetIds =>
        DeskLayouts.Ids
            .Concat(IdeDeskSeats.PresetIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static bool IsKnownGoVerb(string verb) => GoMap.ContainsKey(verb);
    public static bool IsKnownPinAlias(string alias) => DeskPins.Contains(alias);

    /// <summary>Canonical seat organ pin (aliases → plan/editor_scene/…).</summary>
    public static string CanonicalOrganPin(string organPin) => DeskPins.Canonical(organPin);

    static readonly object PinGate = new();
    static List<string> StickyPins = [];

    static readonly DeskLayoutPresetCatalog DeskLayouts = new();
    static IReadOnlyDictionary<string, string[]> LayoutPresets => DeskLayouts.Map;

    static readonly DeskPinAliasCatalog DeskPins = new();
    static IReadOnlyDictionary<string, string> PinAliases => DeskPins.Map;

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };

    readonly record struct SeatPane(
        string Seat,
        string? Organ,
        bool Empty,
        bool Full,
        bool Ok,
        string Line,
        object? Pane)
    {
        public object ToSlot() => new
        {
            seat = Seat,
            glyph = IdeDeskView.SeatGlyph(Seat),
            organ = Organ,
            label = IdeDeskView.ShortOrgan(Organ),
            empty = Empty,
            ok = Ok,
            line = Line,
            full = Full
        };

        public object ToCard(bool includePane) => includePane
            ? new
            {
                seat = Seat,
                organ = Organ,
                empty = Empty,
                ok = Ok,
                line = Line,
                full = Full,
                pane = Pane
            }
            : new
            {
                seat = Seat,
                organ = Organ,
                empty = Empty,
                ok = Ok,
                line = Line,
                full = Full
            };
    }

    sealed class Locus(
        string Id,
        string Kind,
        string Pulse,
        string Drill,
        string? Go = null,
        object? Detail = null)
    {
        public string Id { get; } = Id;
        public string Kind { get; } = Kind;
        public string Pulse { get; } = Pulse;
        public string Drill { get; } = Drill;
        public string? Go { get; } = Go;
        public object? Detail { get; } = Detail;

        public object Card() => new
        {
            id = Id,
            kind = Kind,
            pulse = Pulse,
            drill = Drill,
            go = Go
        };
    }

    static readonly WorldSnapPaneUnit WorldSnapPanes = new();
    static readonly EditorSnapPaneUnit EditorSnapPanes = new();

    static object WorldSnapPane(
        string organ,
        JsonElement? git,
        ShellSnap shell,
        InternetBrowserHabitat.BrowserPulse browser,
        McpOutletHabitat.McpPulse mcp)
    {
        var pin = CanonicalOrganPin(organ);
        return WorldSnapPanes.Build(pin, new WorldSnapPaneUnit.Habitat(
            GitAvailable: git is not null,
            GitPulse: GitPulseLine(git),
            ShellTabCount: shell.TabCount,
            ShellRunning: shell.Running,
            BrowserOk: browser.Ok,
            BrowserLine: browser.Line,
            McpOk: mcp.Ok,
            McpLine: mcp.Line));
    }

    static object EditorSnapPane(BufferSnap buffer) =>
        EditorSnapPanes.Build(new EditorSnapPaneUnit.BufferCounts(
            buffer.Count, buffer.DirtyCount, buffer.DiskChangedCount));

    static object QuietNoProjectPane(string organ) => new
    {
        ok = true,
        go = organ,
        detail = "pulse",
        pulse = "no project — cdp_open",
        quiet = true,
        hint = "cdp_open first; pane_full= to force organ dump anyway."
    };

    /// <summary>
    /// Desk comfort: <c>locus=buffer:doc-N</c> + <c>go=reload|keep_disk|disk_peek</c>
    /// scopes to that file when <c>path=</c> / <c>go_args.path</c> omitted.
    /// </summary>
    static void EnsureDefaultLayoutFromSettings()
    {
        lock (PinGate)
        {
            if (StickyPins.Count > 0) return;
            var layout = IdeSettingsHabitat.EffectiveDeskLayout();
            if (layout is { Length: > 0 } && LayoutPresets.TryGetValue(layout, out var preset))
                StickyPins = preset.Take(MaxTiles).ToList();
        }
    }

    /// <summary>Seats (default) or legacy tile pin mutations.</summary>
    static void ApplyDeskMutation(IReadOnlyDictionary<string, JsonElement> args)
    {
        if (IdeDeskSeats.IsSeatsMode())
        {
            if (BoolOr(args, "pin_clear", false) || BoolOr(args, "clear_pins", false)
                || BoolOr(args, "seat_clear", false) || BoolOr(args, "clear_seats", false))
            {
                IdeDeskSeats.Clear();
                return;
            }

            if (IdeDeskSeats.TryParseSeatAssignment(args, out var seat, out var organ)
                && seat is not null && organ is not null)
            {
                var pin = ResolvePinName(organ) ?? organ;
                IdeDeskSeats.TryPlaceExplicit(seat, pin);
                return;
            }

            var layout = OptString(args, "layout");
            if (layout is { Length: > 0 } && IdeDeskSeats.TryApplyPreset(layout))
                return;

            // pins= in seats mode: interpret as scan-order fill P,F,M (replace, not append).
            var pins = ParsePinList(args, "pins") ?? ParsePinList(args, "tiles");
            if (pins is { Count: > 0 })
            {
                IdeDeskSeats.Clear();
                for (var i = 0; i < Math.Min(pins.Count, IdeDeskSeats.Order.Length); i++)
                    IdeDeskSeats.TryPlaceExplicit(IdeDeskSeats.Order[i], pins[i]);
            }

            return;
        }

        ApplyPinMutation(args);
    }

    static string? ResolvePinName(string verb)
    {
        if (PinAliases.TryGetValue(verb, out var canon))
            return canon;
        return GoMap.ContainsKey(verb) ? verb : null;
    }

    /// <summary>Sticky report with no evidence → sit on plan (cheerful cold desk).</summary>
    static void CheerIdleReportSeat(SessionContext session)
    {
        var map = IdeDeskSeats.Snapshot();
        if (!map.TryGetValue("p", out var organ) || organ is not { Length: > 0 })
            return;
        if (CanonicalOrganPin(organ) is not "report")
            return;
        if (IdeReportBoard.HasEvidence(session))
            return;
        IdeDeskSeats.PlaceOrgan("plan");
    }

    static bool IsPlaceableOrgan(string pin)
    {
        if (PinAliases.ContainsKey(pin))
            return true;
        // Scene-like go verbs that own a seat pulse (not clipboard / find one-shots).
        return pin is "editor_scene" or "buffer_scene" or "browser" or "shell_scene" or "git_scene"
            or "debug_scene" or "test_scene" or "mcp_scene" or "settings" or "project_scene"
            or "plan" or "work" or "report" or "evidence" or "pfd" or "alert" or "eicas" or "sa"
            or "pressure_desk" or "pressure" or "compact_prep" or "pre_compact"
            or "problems" or "plugins"
            or "correspondence" or "quality" or "gates" or "sys" or "chk" or "ecl" or "analysis_scene"
            or "script_scene" or "semantic_map";
    }

    static void ApplyPinMutation(IReadOnlyDictionary<string, JsonElement> args)
    {
        if (BoolOr(args, "pin_clear", false) || BoolOr(args, "clear_pins", false))
        {
            lock (PinGate) StickyPins = [];
            return;
        }

        var layout = OptString(args, "layout");
        if (layout is { Length: > 0 } && LayoutPresets.TryGetValue(layout.Trim(), out var preset))
        {
            lock (PinGate) StickyPins = preset.Take(MaxTiles).ToList();
            return;
        }

        var pins = ParsePinList(args, "pins") ?? ParsePinList(args, "tiles");
        if (pins is { Count: > 0 })
        {
            lock (PinGate) StickyPins = pins.Take(MaxTiles).ToList();
            return;
        }

        var add = ParsePinList(args, "pin");
        if (add is { Count: > 0 })
        {
            lock (PinGate)
            {
                foreach (var p in add)
                {
                    if (!StickyPins.Contains(p, StringComparer.OrdinalIgnoreCase) && StickyPins.Count < MaxTiles)
                        StickyPins.Add(p);
                }
            }
        }
    }

    static List<string> SnapshotPins()
    {
        lock (PinGate) return StickyPins.ToList();
    }

    static List<string> ResolveRequestedPins(IReadOnlyDictionary<string, JsonElement> args)
    {
        var layout = OptString(args, "layout");
        if (layout is { Length: > 0 } && LayoutPresets.TryGetValue(layout.Trim(), out var preset))
            return preset.Take(MaxTiles).ToList();
        return ParsePinList(args, "pins") ?? ParsePinList(args, "tiles") ?? [];
    }

    static List<string>? ParsePinList(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;

        var raw = new List<string>();
        if (el.ValueKind == JsonValueKind.String)
        {
            raw.AddRange((el.GetString() ?? "")
                .Split([',', ';', '|', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        else if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s)
                    raw.Add(s.Trim());
            }
        }
        else
            return null;

        var resolved = new List<string>();
        foreach (var r in raw)
        {
            if (PinAliases.TryGetValue(r, out var canon))
            {
                if (!resolved.Contains(canon, StringComparer.OrdinalIgnoreCase))
                    resolved.Add(canon);
            }
            else if (GoMap.ContainsKey(r) && !resolved.Contains(r, StringComparer.OrdinalIgnoreCase))
                resolved.Add(r);
        }

        return resolved.Count == 0 ? null : resolved;
    }

    static async Task<object> BuildTilesAsync(
        IReadOnlyList<string> pins,
        string? layout,
        string? fullPane,
        IReadOnlyDictionary<string, JsonElement> cockpitArgs,
        BufferSnap buffer,
        string? focusId,
        Func<string, IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<string>> dispatch,
        CancellationToken cancellationToken)
    {
        var panes = new List<object>();
        foreach (var pin in pins.Take(MaxTiles))
        {
            var wantFull = fullPane is { Length: > 0 }
                && (string.Equals(fullPane, pin, StringComparison.OrdinalIgnoreCase)
                    || (PinAliases.TryGetValue(fullPane, out var fa)
                        && string.Equals(fa, pin, StringComparison.OrdinalIgnoreCase)));

            var tileArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var kv in cockpitArgs)
                tileArgs[kv.Key] = kv.Value;
            tileArgs["go_detail"] = JsonSerializer.SerializeToElement(wantFull ? "full" : "pulse");
            // Don't re-apply go= from parent into every pane.
            tileArgs.Remove("go");
            tileArgs.Remove("do");

            var pane = await DispatchGoAsync(pin, tileArgs, buffer, focusId, dispatch, cancellationToken)
                .ConfigureAwait(false);
            panes.Add(new
            {
                pin,
                full = wantFull,
                pane
            });
        }

        return new
        {
            ok = true,
            role = "tiles",
            layout,
            pins,
            count = panes.Count,
            panes,
            hint = "Human twin: code + browser side-by-side. Drill one pane: go=<pin> go_detail=full; or pane_full=<pin>."
        };
    }

    sealed record OrganPulse(bool Ok, string Line, string? Schema, object? Next, string? Hint);

    static readonly OrganJsonPulseUnit OrganJsonPulse = new();

    static OrganPulse PulseFromOrgan(string raw)
    {
        var p = OrganJsonPulse.FromJson(raw, GoPulseCapChars);
        return new OrganPulse(p.Ok, p.Line, p.Schema, p.Next, p.Hint);
    }

    static object? TryParseJson(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(text);
        }
        catch
        {
            return text;
        }
    }

    static (string Text, bool Truncated) CapGoResult(string raw, int cap)
    {
        if (raw.Length <= cap)
            return (raw, false);
        return (raw[..cap] + "\n…[cockpit go.result truncated]", true);
    }

    /// <summary>
    static readonly GoResultSlimUnit GoResultSlim = new();

    /// <summary>
    /// Soft-organ Handle() often ignores go_detail — slim fat dumps to pulse when A (default).
    /// </summary>
    static object? SlimGoResult(object? goResult, string? goDetailRaw) =>
        GoResultSlim.Slim(goResult, goDetailRaw, raw =>
        {
            var p = PulseFromOrgan(raw);
            return new GoResultSlimUnit.OrganPulseSnap(p.Ok, p.Line, p.Schema, p.Next, p.Hint);
        });

    static bool IsPressureGoResult(object? goResult)
    {
        if (goResult is null)
            return false;
        try
        {
            var raw = JsonSerializer.Serialize(goResult);
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.TryGetProperty("schema", out var sch)
                && sch.ValueKind == JsonValueKind.String
                && string.Equals(sch.GetString(), IdePressureChannel.SchemaVersion, StringComparison.Ordinal))
                return true;
            if (root.TryGetProperty("go", out var go)
                && go.ValueKind == JsonValueKind.String
                && go.GetString() is { Length: > 0 } g)
            {
                return g.Equals("pressure_desk", StringComparison.OrdinalIgnoreCase)
                       || g.Equals("pressure", StringComparison.OrdinalIgnoreCase)
                       || g.Equals("compact_prep", StringComparison.OrdinalIgnoreCase)
                       || g.Equals("pre_compact", StringComparison.OrdinalIgnoreCase)
                       || g.Equals("cdp_pressure", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }

    static object SessionPulse(SessionContext session) => new
    {
        phase = CdpEnumParse.ToWire(session.Phase),
        @object = CdpEnumParse.ToWire(session.Object),
        language = session.Language,
        project_root = session.ProjectRoot,
        scm_root = session.ScmRoot,
        solution_or_project_path = session.SolutionOrProjectPath
    };

    static IdeAlertChannel.Inputs BuildAlertInputs(
        SessionContext session,
        QualityGates.QualitySnap quality,
        BufferSnap buffer,
        DebugSnap debug,
        ShellSnap shell,
        JsonElement? git,
        IdeProblemsChannel.Snap problems,
        WorkSnap work,
        IntentWorkspaceStore? workspaceStore,
        IntentWorkspaceState workspaceState,
        IdeChkChannel.Snap? chk = null)
    {
        var seats = IdeDeskSeats.IsSeatsMode()
            ? IdeDeskSeats.Snapshot()
            : new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var (layoutHint, seatNote) = IdeAlertChannel.SuggestLayout(session.Phase, session.Object, seats);
        var intent = session.Intent is { } i
            ? CdpEnumParse.ToWire(i)
            : work.Pulse;
        var locus = ResolveLocusLine(buffer, session.ProjectRoot);
        var sit = new IdeAlertChannel.Sit(
            $"{CdpEnumParse.ToWire(session.Phase)}/{CdpEnumParse.ToWire(session.Object)}",
            intent,
            locus,
            layoutHint,
            seatNote);

        string? stageMismatch = null;
        if (workspaceStore is not null
            && workspaceState.ActiveStageId is { } sid
            && workspaceStore.TryGetStagePhaseAffinity(sid) is { Length: > 0 } aff)
        {
            var sessionPhase = CdpEnumParse.ToWire(session.Phase);
            if (!aff.Equals(sessionPhase, StringComparison.OrdinalIgnoreCase))
                stageMismatch = $"phase mismatch task@{aff} · session={sessionPhase}";
        }

        return new IdeAlertChannel.Inputs(
            quality,
            buffer.DiskChangedCount,
            debug.ActiveDap,
            debug.Stopped,
            problems.Errors,
            problems.Warnings,
            shell.Running,
            shell.Failed,
            GitIsDirty(git),
            sit,
            stageMismatch,
            chk?.OpenRequired ?? 0,
            chk?.Pulse);
    }

    static string? ResolveLocusLine(BufferSnap buffer, string? projectRoot)
    {
        if (buffer.Docs.Count == 0)
            return null;
        var hot = buffer.Docs.FirstOrDefault(d => d.DiskChanged)
            ?? buffer.Docs.FirstOrDefault(d => d.Dirty)
            ?? buffer.Docs[0];
        var path = hot.Path;
        if (projectRoot is { Length: > 0 }
            && path.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
        {
            var rel = path[projectRoot.Length..].TrimStart('\\', '/');
            if (rel.Length > 0) path = rel;
        }

        if (path.Length > 64)
            path = "…" + path[^60..];
        var mark = hot.DiskChanged ? " disk" : hot.Dirty ? " dirty" : "";
        return $"{path}{mark}";
    }

    static object BuildSysOrgan(
        SessionContext session,
        JsonElement? gitRoot,
        ShellSnap shell,
        BufferSnap buffer,
        DebugSnap debug,
        TestSnap test,
        WorkSnap work)
    {
        var git = GitPulseLine(gitRoot);
        var ops = IdeOpsPulse.Line();
        var pulse = $"{ops} · {git} · buf={buffer.Count} dirty={buffer.DirtyCount}";
        return new
        {
            ok = true,
            go = "sys",
            schema = "sys_organ/v0",
            pulse,
            ops,
            title = "SYS",
            project = session.ProjectRoot is null ? "no_project — cdp_open" : session.ProjectRoot,
            git,
            shell = $"tabs={shell.TabCount} running={shell.Running} failed={shell.Failed}",
            buffer = $"open={buffer.Count} dirty={buffer.DirtyCount} disk_changed={buffer.DiskChangedCount}",
            debug = debug.ActiveDap
                ? $"dap stopped={debug.Stopped} bp={debug.BreakpointCount}"
                : $"idle bp={debug.BreakpointCount}",
            test = test.Available
                ? test.LastRun is null
                    ? "no last_run — go=test"
                    : $"last {(test.Success ? "ok" : "FAIL")} {test.Passed}/{test.Total}"
                : test.Reason,
            work = work.Pulse ?? "no plan",
            hint = "Soft organ (legacy mfd=sys). Slim status already in view.banner/board."
        };
    }

    static IReadOnlyDictionary<string, JsonElement> WithStringArg(
        IReadOnlyDictionary<string, JsonElement> args,
        string key,
        string value)
    {
        var d = new Dictionary<string, JsonElement>(args, StringComparer.Ordinal);
        d[key] = JsonSerializer.SerializeToElement(value);
        return d;
    }

    static async Task<JsonElement?> TryGitAsync(
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        bool includeSubmodules,
        CancellationToken cancellationToken)
    {
        if (!byDomain.TryGetValue(CdpDomains.Git, out var git) || !git.IsEnabled)
            return null;

        var root = session.ScmRoot ?? session.ProjectRoot;
        if (string.IsNullOrWhiteSpace(root))
            return null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var callArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["workspace_path"] = JsonSerializer.SerializeToElement(root),
                ["include_submodules"] = JsonSerializer.SerializeToElement(includeSubmodules),
                ["max_roots"] = JsonSerializer.SerializeToElement(4)
            };
            var raw = await git.CallAsync("git_scene", callArgs).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    static object CompactGit(JsonElement root)
    {
        var roots = new List<object>();
        if (root.TryGetProperty("roots", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in arr.EnumerateArray().Take(8))
            {
                roots.Add(new
                {
                    path = PropStr(r, "path"),
                    ok = PropBool(r, "ok"),
                    branch = PropStr(r, "branch"),
                    dirty = PropBool(r, "dirty"),
                    ahead = PropInt(r, "ahead"),
                    behind = PropInt(r, "behind"),
                    counts = r.TryGetProperty("counts", out var c)
                        ? JsonSerializer.Deserialize<object>(c.GetRawText())
                        : null
                });
            }
        }

        return new { schema = "git_scene/v0", roots };
    }

    static bool GitIsDirty(JsonElement? root)
    {
        if (root is not { } g)
            return false;
        if (!g.TryGetProperty("roots", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return false;
        foreach (var r in arr.EnumerateArray())
        {
            if (PropBool(r, "dirty") == true)
                return true;
        }

        return false;
    }

    static string? FirstGitBranch(JsonElement root)
    {
        if (!root.TryGetProperty("roots", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var r in arr.EnumerateArray())
        {
            var b = PropStr(r, "branch");
            if (b is { Length: > 0 })
                return b;
        }

        return null;
    }

    static string GitPulseLine(JsonElement? root)
    {
        if (root is null)
            return "n/a";
        var branch = FirstGitBranch(root.Value) ?? "?";
        return GitIsDirty(root) ? $"dirty ({branch})" : $"clean ({branch})";
    }
}
