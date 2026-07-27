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

    static readonly DeskLayoutPresetCatalog DeskLayouts = new();
    static IReadOnlyDictionary<string, string[]> LayoutPresets => DeskLayouts.Map;

    static readonly DeskPinAliasCatalog DeskPins = new();
    static IReadOnlyDictionary<string, string> PinAliases => DeskPins.Map;
    static readonly DeskPlaceableOrganUnit DeskPlaceable = new();

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
}
