using System.Text.Json;
using Cdp.Core;
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
        LayoutPresets.Keys
            .Concat(IdeDeskSeats.PresetIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static bool IsKnownGoVerb(string verb) => GoMap.ContainsKey(verb);
    public static bool IsKnownPinAlias(string alias) => PinAliases.ContainsKey(alias);

    /// <summary>Canonical seat organ pin (aliases → plan/editor_scene/…).</summary>
    public static string CanonicalOrganPin(string organPin)
    {
        var pin = organPin.Trim().ToLowerInvariant();
        return PinAliases.TryGetValue(pin, out var canon) ? canon : pin;
    }

    static readonly object PinGate = new();
    static List<string> StickyPins = [];

    static readonly Dictionary<string, string[]> LayoutPresets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["code+net"] = ["editor_scene", "browser"],
        ["code+shell"] = ["editor_scene", "shell"],
        ["code+git"] = ["editor_scene", "git_scene"],
        ["net+shell"] = ["browser", "shell"],
        ["desk"] = ["editor_scene", "browser", "shell"],
        ["cockpit"] = ["editor_scene", "browser", "shell"],
        ["code+net+shell"] = ["editor_scene", "browser", "shell"],
        ["agent"] = ["plan", "editor_scene", "script_scene"],
    };

    static readonly Dictionary<string, string> PinAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["editor"] = "editor_scene",
        ["editor_scene"] = "editor_scene",
        ["code"] = "editor_scene",
        ["buffer"] = "buffer_scene",
        ["buffer_scene"] = "buffer_scene",
        ["browser"] = "browser",
        ["net"] = "browser",
        ["internet"] = "browser",
        ["internet_browser"] = "browser",
        ["scene_internet_browser"] = "browser",
        ["shell"] = "shell_scene",
        ["shell_scene"] = "shell_scene",
        ["git"] = "git_scene",
        ["git_scene"] = "git_scene",
        ["debug"] = "debug_scene",
        ["debug_scene"] = "debug_scene",
        ["test"] = "test_scene",
        ["test_scene"] = "test_scene",
        ["mcp"] = "mcp_scene",
        ["mcp_scene"] = "mcp_scene",
        ["settings"] = "settings",
        ["settings_scene"] = "settings",
        ["ide_settings"] = "settings",
        ["prefs"] = "settings",
        ["options"] = "settings",
        ["correspondence"] = "correspondence",
        ["corr"] = "correspondence",
        ["work"] = "plan",
        ["tasks"] = "plan",
        ["plan"] = "plan",
        ["task"] = "plan",
        ["feature"] = "plan",
        ["tm"] = "plan",
        ["report"] = "report",
        ["evidence"] = "report",
        ["pfd"] = "report",
        ["find_desk"] = "find_desk",
        ["search_desk"] = "find_desk",
        ["code_search"] = "find_desk",
        ["sa_desk"] = "sa_desk",
        ["code_sa"] = "sa_desk",
        ["pre_sa"] = "sa_desk",
        ["sa_code"] = "sa_desk",
        ["refactor_plan"] = "refactor_plan",
        ["refactor"] = "refactor_plan",
        ["cdp_refactor"] = "refactor_plan",
        ["debt_scene"] = "refactor_plan",
        ["debug_desk"] = "debug_desk",
        ["dap_sa"] = "debug_desk",
        ["debug_sa"] = "debug_desk",
        ["test_desk"] = "test_desk",
        ["test_sa"] = "test_desk",
        ["build_desk"] = "build_desk",
        ["ship_desk"] = "build_desk",
        ["build_sa"] = "build_desk",
        ["ship_sa"] = "build_desk",
        ["crm"] = "crm",
        ["callout"] = "crm",
        ["crm_panel"] = "crm",
        ["files_desk"] = "files_desk",
        ["files"] = "files_desk",
        ["explorer"] = "files_desk",
        ["fm"] = "files_desk",
        ["file_manager"] = "files_desk",
        ["ignite_desk"] = "ignite_desk",
        ["ignite"] = "ignite_desk",
        ["autoignite"] = "ignite_desk",
        ["cdt_ignite"] = "ignite_desk",
        ["webcam_desk"] = "webcam_desk",
        ["webcam"] = "webcam_desk",
        ["camera"] = "webcam_desk",
        ["sense"] = "webcam_desk",
        ["pressure_desk"] = "pressure_desk",
        ["pressure"] = "pressure_desk",
        ["compact_prep"] = "pressure_desk",
        ["pre_compact"] = "pressure_desk",
        ["alert"] = "alert",
        ["eicas"] = "alert",
        ["sa"] = "alert",
        ["ecl"] = "ecl",
        ["chk"] = "ecl",
        ["qrh"] = "qrh",
        ["eqrh"] = "qrh",
        ["handbook"] = "qrh",
        ["review"] = "review",
        ["problems"] = "problems",
        ["problem"] = "problems",
        ["errlist"] = "problems",
        ["errorlist"] = "problems",
        ["err"] = "problems",
        ["diags"] = "problems",
        ["plugins"] = "plugins",
        ["plugin"] = "plugins",
        ["vsix"] = "plugins",
        ["project"] = "project_scene",
        ["project_scene"] = "project_scene",
    };

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };
    static readonly HashSet<string> MfdPages = new(StringComparer.OrdinalIgnoreCase)
    {
        "nav", "sys", "chk", "ecl", "qrh", "gates"
    };

    /// <summary>VS Ctrl+Q — fuzzy desk verbs / organs (not code).</summary>
    public static FeatureHit[] SearchFeatures(string query, int max)
    {
        var q = (query ?? "").Trim();
        if (q.Length == 0)
            return [];

        static int Score(string name, string query)
        {
            if (name.Equals(query, StringComparison.OrdinalIgnoreCase))
                return 1000;
            if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                return 800;
            if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
                return 500;
            return 0;
        }

        return GoMap.Keys
            .Select(go => (go, score: Score(go, q)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.go, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(x => new FeatureHit(x.go, x.score, GoMap[x.go].Tool))
            .ToArray();
    }

    public readonly record struct FeatureHit(string Go, int Score, string Tool);

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

    static string ResolveDeskDetail(IReadOnlyDictionary<string, JsonElement> args, string? focusId)
    {
        var raw = (OptString(args, "desk_detail") ?? OptString(args, "nav_detail") ?? "slim")
            .Trim().ToLowerInvariant();
        if (raw is "compact")
            raw = "slim";
        // Focused locus needs the nav catalog.
        if (focusId is { Length: > 0 } && raw is "slim" or "omit")
            return "nav";
        if (raw is "slim" or "omit" or "nav" or "full")
            return raw is "omit" ? "slim" : raw;
        return "slim";
    }


    static object WorldSnapPane(
        string organ,
        JsonElement? git,
        ShellSnap shell,
        InternetBrowserHabitat.BrowserPulse browser,
        McpOutletHabitat.McpPulse mcp)
    {
        var pin = CanonicalOrganPin(organ);
        return pin switch
        {
            "git_scene" => IdeWorldChannel.Pane("git_scene", git is not null, GitPulseLine(git)),
            "shell_scene" => IdeWorldChannel.Pane(
                "shell_scene",
                true,
                shell.Running > 0
                    ? $"shell · {shell.TabCount} tab(s) · {shell.Running} running"
                    : $"shell · {shell.TabCount} tab(s)"),
            "browser" => IdeWorldChannel.Pane("browser", browser.Ok, browser.Line),
            "mcp_scene" => IdeWorldChannel.Pane("mcp_scene", mcp.Ok, mcp.Line),
            _ => IdeWorldChannel.Pane(pin, true, pin)
        };
    }

    static object EditorSnapPane(BufferSnap buffer)
    {
        var pulse = buffer.Count == 0
            ? "—"
            : buffer.DiskChangedCount > 0
                ? $"{buffer.Count} buf · disk×{buffer.DiskChangedCount}"
                : buffer.DirtyCount > 0
                    ? $"{buffer.Count} buf · dirty×{buffer.DirtyCount}"
                    : $"{buffer.Count} buf";
        return new
        {
            ok = true,
            go = "editor_scene",
            detail = "pulse",
            pulse,
            snap = true,
            hint = "pane_full=editor for dump"
        };
    }

    static object QuietNoProjectPane(string organ) => new
    {
        ok = true,
        go = organ,
        detail = "pulse",
        pulse = "no project — cdp_open",
        quiet = true,
        hint = "cdp_open first; pane_full= to force organ dump anyway."
    };

    static async Task<object> DispatchGoAsync(
        string verb,
        IReadOnlyDictionary<string, JsonElement> cockpitArgs,
        BufferSnap buffer,
        string? focusId,
        Func<string, IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<string>> dispatch,
        CancellationToken cancellationToken)
    {
        var detail = (OptString(cockpitArgs, "go_detail") ?? "pulse").Trim().ToLowerInvariant();
        if (detail is not ("pulse" or "full"))
            detail = "pulse";

        if (verb.Equals("cockpit", StringComparison.OrdinalIgnoreCase)
            || verb.Equals("cdp_cockpit", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                ok = false,
                go = verb,
                error = "refuse_self",
                hint = "go= routes to organs; use mfd=/locus= for cockpit itself."
            };
        }

        if (!GoMap.TryGetValue(verb, out var map))
        {
            return new
            {
                ok = false,
                go = verb,
                error = "unknown_go",
                hint = "Pick from go_verbs[] or next[].go / locus.go."
            };
        }

        var callArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (map.Defaults is not null)
        {
            foreach (var kv in map.Defaults)
                callArgs[kv.Key] = kv.Value;
        }

        if (cockpitArgs.TryGetValue("go_args", out var goArgs) && goArgs.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in goArgs.EnumerateObject())
                callArgs[p.Name] = p.Value.Clone();
        }

        InjectBufferPathFromLocus(verb, callArgs, buffer, focusId);

        try
        {
            var raw = await dispatch(map.Tool, callArgs, cancellationToken).ConfigureAwait(false);
            if (detail == "full")
            {
                var capped = CapGoResult(raw, GoResultCapChars);
                object? parsed = TryParseJson(capped.Text);
                return new
                {
                    ok = true,
                    go = verb,
                    tool = map.Tool,
                    detail = "full",
                    truncated = capped.Truncated,
                    result = parsed
                };
            }

            var pulse = PulseFromOrgan(raw);
            return new
            {
                ok = pulse.Ok,
                go = verb,
                tool = map.Tool,
                detail = "pulse",
                pulse = pulse.Line,
                schema = pulse.Schema,
                next = pulse.Next,
                hint = pulse.Hint ?? "go_detail=full for organ dump; or call organ tool directly."
            };
        }
        catch (Exception ex)
        {
            return new
            {
                ok = false,
                go = verb,
                tool = map.Tool,
                detail,
                error = ex.Message
            };
        }
    }

    /// <summary>
    /// Desk comfort: <c>locus=buffer:doc-N</c> + <c>go=reload|keep_disk|disk_peek</c>
    /// scopes to that file when <c>path=</c> / <c>go_args.path</c> omitted.
    /// </summary>
    static void InjectBufferPathFromLocus(
        string verb,
        Dictionary<string, JsonElement> callArgs,
        BufferSnap buffer,
        string? focusId)
    {
        if (verb is not ("reload" or "keep_disk" or "disk_peek"))
            return;
        if (callArgs.TryGetValue("path", out var pathEl)
            && pathEl.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(pathEl.GetString()))
            return;
        if (focusId is not { Length: > 0 }
            || !focusId.StartsWith("buffer:", StringComparison.OrdinalIgnoreCase)
            || focusId.Equals("buffer:none", StringComparison.OrdinalIgnoreCase))
            return;

        var docId = focusId["buffer:".Length..];
        var doc = buffer.Docs.FirstOrDefault(d =>
            string.Equals(d.DocId, docId, StringComparison.OrdinalIgnoreCase));
        if (doc is null || string.IsNullOrWhiteSpace(doc.Path) || doc.Path == "?")
            return;

        callArgs["path"] = JsonSerializer.SerializeToElement(doc.Path);
    }

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

    static OrganPulse PulseFromOrgan(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var ok = !root.TryGetProperty("ok", out var okEl) || okEl.ValueKind != JsonValueKind.False;
            if (root.TryGetProperty("pulse", out var pulseEl) && pulseEl.ValueKind == JsonValueKind.String)
            {
                var pulseLine = pulseEl.GetString() ?? "";
                if (pulseLine.Length > 0)
                {
                    var hintEarly = root.TryGetProperty("hint", out var h0) && h0.ValueKind == JsonValueKind.String
                        ? Truncate(h0.GetString(), 240)
                        : null;
                    var schemaEarly = root.TryGetProperty("schema", out var sch0) && sch0.ValueKind == JsonValueKind.String
                        ? sch0.GetString()
                        : null;
                    return new OrganPulse(ok, Truncate(pulseLine, GoPulseCapChars) ?? pulseLine, schemaEarly, null, hintEarly);
                }
            }

            var schema = root.TryGetProperty("schema", out var sch) && sch.ValueKind == JsonValueKind.String
                ? sch.GetString()
                : null;
            var hint = root.TryGetProperty("hint", out var h) && h.ValueKind == JsonValueKind.String
                ? Truncate(h.GetString(), 240)
                : null;
            object? next = null;
            if (root.TryGetProperty("next", out var n))
                next = JsonSerializer.Deserialize<JsonElement>(n.GetRawText());

            var bits = new List<string>();
            if (schema is { Length: > 0 })
                bits.Add(schema);
            bits.Add(ok ? "ok" : "FAIL");

            void AddNum(string key, string label)
            {
                if (root.TryGetProperty(key, out var el) && el.TryGetInt32(out var n))
                    bits.Add($"{label}={n}");
            }

            AddNum("count", "n");
            AddNum("dirty_count", "dirty");
            AddNum("disk_changed_count", "disk");
            AddNum("candidate_count", "cand");
            AddNum("slice_count", "slices");
            AddNum("path_count", "paths");
            AddNum("tab_count", "tabs");
            AddNum("groups", "groups");
            AddNum("files_scanned", "files");
            AddNum("undo_left", "undo");
            AddNum("redo_left", "redo");
            AddNum("replaced", "replaced");

            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
                bits.Add(Truncate(err.GetString(), 80) ?? "error");

            // git_scene often nests roots
            if (root.TryGetProperty("roots", out var roots) && roots.ValueKind == JsonValueKind.Array)
                bits.Add($"roots={roots.GetArrayLength()}");

            var line = string.Join(' ', bits);
            if (line.Length > GoPulseCapChars)
                line = line[..GoPulseCapChars] + "…";
            return new OrganPulse(ok, line, schema, next, hint);
        }
        catch
        {
            var line = Truncate(raw, GoPulseCapChars) ?? "";
            return new OrganPulse(true, line, null, null, "go_detail=full for parseable dump");
        }
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
    /// Soft-organ Handle() often ignores go_detail — slim fat dumps to pulse when A (default).
    /// </summary>
    static object? SlimGoResult(object? goResult, string? goDetailRaw)
    {
        if (goResult is null)
            return null;
        var detail = (goDetailRaw ?? "pulse").Trim().ToLowerInvariant();
        if (detail is "full")
            return goResult;

        string raw;
        try
        {
            raw = JsonSerializer.Serialize(goResult);
        }
        catch
        {
            return goResult;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.TryGetProperty("detail", out var d)
                && d.ValueKind == JsonValueKind.String
                && d.GetString() is "pulse"
                && root.TryGetProperty("pulse", out var p)
                && p.ValueKind == JsonValueKind.String
                && !GoResultHasFatDump(root))
            {
                return goResult;
            }

            var pulse = PulseFromOrgan(raw);
            var go = PropStr(root, "go") ?? "go";
            var tool = PropStr(root, "tool");
            return new
            {
                ok = pulse.Ok,
                go,
                tool,
                detail = "pulse",
                pulse = pulse.Line,
                schema = pulse.Schema,
                next = pulse.Next,
                slimmed = true,
                hint = pulse.Hint ?? "go_detail=full for organ dump; or call organ tool directly."
            };
        }
        catch
        {
            var pulse = PulseFromOrgan(raw);
            return new
            {
                ok = pulse.Ok,
                go = "go",
                detail = "pulse",
                pulse = pulse.Line,
                slimmed = true,
                hint = "go_detail=full for organ dump"
            };
        }
    }

    static bool GoResultHasFatDump(JsonElement root)
    {
        if (root.TryGetProperty("view", out var view) && view.ValueKind == JsonValueKind.Object)
            return true;
        if (root.TryGetProperty("result", out var result)
            && result.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            return true;
        if (root.TryGetProperty("board", out _))
            return true;
        if (root.TryGetProperty("lines", out var lines)
            && lines.ValueKind == JsonValueKind.Array
            && lines.GetArrayLength() > 2)
            return true;
        if (root.TryGetProperty("panes", out var panes)
            && panes.ValueKind == JsonValueKind.Array
            && panes.GetArrayLength() > 0)
            return true;
        return false;
    }

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

    static object[] BuildNext(
        SessionContext session,
        JsonElement? gitRoot,
        ShellSnap shell,
        BufferSnap buffer,
        DebugSnap debug,
        TestSnap test,
        WorkSnap work,
        string? focusId,
        QualityGates.QualitySnap quality,
        IdeAlertChannel.Snap alert,
        IdeChkChannel.Snap? chk = null,
        IdeChkChannel.ProbeCtx? chkCtx = null)
    {
        var list = new List<object>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string id, string go, string label, string why)
        {
            if (list.Count >= 8 || !seen.Add(go))
                return;
            list.Add(new { id, go, label, why });
        }

        if (session.ProjectRoot is null)
        {
            Add("n-open", "project_scene", "Project map", "No project — cdp_open / project_scene first");
            if (File.Exists(DeskBookmark.FilePath))
                Add("n-restore", "restore", "Restore Previous", "desk bookmark — project + buffers (not LLM chat)");
            if (work.IntentId is not null)
                Add("n-plan", "plan", "Task Manager", work.Pulse ?? work.IntentId);
            else
                Add("n-plan", "plan", "Task Manager", "no plan — feature <name>");
            Add("n-settings", "options", "Tools → Options", "IDE prefs — internet/desk/shell/mcp (not Cursor)");
            return list.ToArray();
        }

        // EICAS/SA: surface alert before comfort next when something beeps.
        if (alert.Level != IdeAlertChannel.Level.Clear)
            Add("n-alert", "alert", "SA board", alert.Pulse);
        if (IdePressureChannel.IsArmed())
            Add("n-pressure", "pressure", "Pressure prep", IdePressureChannel.PulseLine());
        if (chk is { OpenRequired: > 0 })
            Add("n-ecl", "ecl", "ECL", chk.Pulse);
        if (session.Phase is CdpPhase.Review or CdpPhase.Verify)
            Add("n-review", "review", "Review", session.Phase is CdpPhase.Review ? "Judgment board" : "After verify — judgment before ship");
        if (chkCtx is { } qCtx)
        {
            var qSuggest = IdeQrhChannel.SuggestFor(qCtx, chk);
            if (qSuggest.HotId is { Length: > 0 })
                Add("n-qrh", "qrh", "eQRH", qSuggest.Pulse);
        }
        if (alert.Sit?.LayoutHint is { Length: > 0 } layoutHint)
            Add("n-layout", "layout", $"Layout {layoutHint}",
                $"cmd=\"layout {layoutHint}\" — {alert.Sit.SeatNote ?? layoutHint}");
        if (alert.ProblemErrors > 0)
            Add("n-problems", "problems", "Error List", $"E×{alert.ProblemErrors} — aim row, don't dump");

        Add("n-goto", "goto", "Go To (Ctrl+T)", "query= type/member/file — land on anchor");
        Add("n-editor", "editor_scene", "Editor map", "Buffer/desk loop");

        // Dual-instance: sticky warm + go=deploy from survivor.
        if (File.Exists(DeskBookmark.FilePath))
            Add("n-restore", "restore", "Restore Previous", "desk bookmark — usually auto on cold tools");
        Add("n-deploy", "deploy", "Deploy", "hard → sibling install; dry_run= to preview");

        if (EditorComfort.AnyUndo())
            Add("n-undo", "undo", "Undo last edit", "buffer edit stack");
        if (EditorComfort.AnyClipboard())
            Add("n-clipboard", "clipboard", "Clipboard", "frames — pick frame= + paste");
        if (EditorComfort.AnyNavBack())
            Add("n-back", "back", "Nav back", "locus stack");

        // Quality stabilizer: after thick files / gate findings — guide, don't sermon.
        if (quality is { Enabled: true, Fail: > 0 })
            Add("n-quality", "quality", "Quality gates", $"FAIL×{quality.Fail} — harness next step");
        else if (quality is { Enabled: true, Warn: > 0 })
            Add("n-quality", "quality", "Quality gates", $"WARN×{quality.Warn} — review or tune overlay");

        if (quality.SuggestSniper && !EditSniper.HasHold)
            Add("n-scope", "scope", "Sniper aim", "Large open file — aim corridor before thick edit");

        // VS-style: File Modified Outside the Environment — Reload?
        if (buffer.DiskChangedCount > 0)
        {
            Add("n-disk-peek", "disk_peek", "Peek disk vs memory",
                "Glance before Reload? (mtime / content)");
            Add("n-reload", "reload", "Reload from disk",
                $"{buffer.DiskChangedCount} file(s) changed outside — like VS Reload?");
            Add("n-keep-disk", "keep_disk", "Keep memory",
                focusId is { Length: > 0 } && focusId.StartsWith("buffer:", StringComparison.OrdinalIgnoreCase)
                    ? $"Don't Reload — locus {focusId} → path="
                    : "Don't Reload — silence all drifted (or path= / locus=buffer:…)");
        }

        // Sniper beats (kj-1848): scope → target → shoot — prefer over file-wide outline.
        if (EditSniper.HasHold)
        {
            Add("n-target", "target", "Outline corridor", $"Aim {EditSniper.PulseLine}");
            Add("n-peek", "peek", "Peek aim", "wire= optional; corridor window");
            if (EditorComfort.AnyClipboard())
                Add("n-paste-sniper", "paste_sniper", "Paste frame into aim", "MRU/frame= replace hold");
            Add("n-put-sniper", "put_sniper", "Put draft into aim", "text=/frame= thick rewrite");
            Add("n-edit-draft", "edit_draft", "Shoot (draft)", "mutate/fix inside aim");
            Add("n-scope-clear", "scope_clear", "Clear aim", "drop From/Till");
        }
        else if (buffer.Count > 0 || session.ProjectRoot is not null)
        {
            Add("n-scope", "scope", "Sniper aim", "from=/till= corridor before outline");
            if (session.ProjectRoot is not null)
                Add("n-put", "put", "Put draft file", "path= + text=/frame= — one-shot dump");
            if (buffer.Count > 0)
            {
                Add("n-share", "share", "Share with operator", "inbox file + thin chat= (not into agent)");
                Add("n-take", "take", "Take into agent", "rare — body + chat_markdown into context");
            }
        }

        if (buffer.Count > 0 && !EditSniper.HasHold)
            Add("n-edit-draft", "edit_draft", "Edit plan draft", $"Open buffers={buffer.Count} dirty={buffer.DirtyCount}");
        else if (session.ProjectRoot is not null && buffer.Count == 0 && !EditSniper.HasHold)
            Add("n-buffer", "buffer_scene", "Buffer scene", "No open buffers yet");

        if (session.ProjectRoot is not null)
            Add("n-script", "script_scene", "Script habitat", "put→diags→check→run");

        if (gitRoot is { } g && GitIsDirty(g))
            Add("n-git-draft", "git_draft", "Git plan draft", "Dirty SCM — logical slices");
        else
            Add("n-git", "git_scene", "Git scene", "SCM map");

        if (test.Failed > 0)
            Add("n-test-plan", "test_plan", "Retest failed", "last_run has failures");
        else
            Add("n-test", "test_scene", "Test scene", "Discover / last_run");

        if (debug.Stopped)
            Add("n-debug", "debug_scene", "Debug scene", "DAP stopped — stop_context via organ");
        else
            Add("n-shell", "shell_scene", "Shell habitat", shell.Running > 0 ? "jobs running" : "tabs map");

        Add("n-settings", "options", "Tools → Options", "IDE prefs — internet/desk/shell/mcp (not Cursor)");

        if (focusId is { Length: > 0 }
            && focusId.StartsWith("buffer:", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(focusId, "buffer:none", StringComparison.OrdinalIgnoreCase))
            Add("n-focus-editor", "editor_scene", "Focus editor context", $"locus {focusId}");

        if (work.IntentId is not null)
            Add("n-plan", "plan", "Task Manager", work.Pulse ?? work.IntentId);
        else
            Add("n-plan", "plan", "Task Manager", "no plan — feature <name>");

        if (chk is null || chk.OpenRequired == 0)
            Add("n-ecl", "ecl", "ECL", "go=ecl");
        return list.ToArray();
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

    static List<Locus> BuildLoci(
        SessionContext session,
        JsonElement? gitRoot,
        ShellSnap shell,
        InternetBrowserHabitat.BrowserPulse browser,
        IdeSettingsHabitat.SettingsPulse settings,
        BufferSnap buffer,
        DebugSnap debug,
        TestSnap test,
        WorkSnap work,
        QualityGates.QualitySnap quality)
    {
        var list = new List<Locus>();

        list.Add(new Locus(
            "session:project",
            "session",
            session.ProjectRoot is null
                ? "no project — cdp_open"
                : $"{session.Language ?? "?"} @ {ShortPath(session.ProjectRoot)}",
            "cdp_open / cdp_session",
            "project_scene",
            SessionPulse(session)));

        list.Add(new Locus(
            "settings:ide",
            "settings",
            settings.Line,
            "go=options → page=internet|desk|shell|mcp",
            "settings",
            new
            {
                ok = settings.Ok,
                user_count = settings.UserCount,
                user_path = settings.UserPath,
                process_path = settings.ProcessPath
            }));

        if (gitRoot is { } g)
        {
            var dirty = GitIsDirty(g);
            var branch = FirstGitBranch(g) ?? "?";
            list.Add(new Locus(
                "git:scm",
                "git",
                dirty ? $"dirty on {branch}" : $"clean {branch}",
                "go=git_scene → go=git_draft",
                dirty ? "git_draft" : "git_scene",
                CompactGit(g)));
        }
        else
        {
            list.Add(new Locus(
                "git:scm",
                "git",
                "unavailable — cdp_open scm_root",
                "go=git_scene",
                "git_scene",
                new { available = false }));
        }

        foreach (var tab in shell.Tabs.Take(12))
        {
            var id = $"shell:{tab.Id}";
            var pulse = $"{tab.State}" +
                        (tab.LastExit is { } ex ? $" exit={ex}" : "") +
                        (tab.Cwd is { } cwd ? $" @ {ShortPath(cwd)}" : "");
            list.Add(new Locus(
                id,
                "shell",
                pulse,
                "go=shell_scene / go=shell_last",
                "shell_scene",
                tab));
        }

        list.Add(new Locus(
            "browser:net",
            "browser",
            browser.Line,
            "go=browser / go=search q=… / layout=code+net",
            "browser",
            new
            {
                ok = browser.Ok,
                active_tab = browser.ActiveTab,
                tab_count = browser.TabCount,
                url = browser.Url,
                preview = browser.Preview,
                lynx = browser.LynxPath
            }));

        foreach (var doc in buffer.Docs.Take(16))
        {
            var both = doc.DiskChanged && doc.Dirty;
            var pulse =
                (both ? "DIRTY+DISK " : doc.DiskChanged ? "DISK CHANGED " : doc.Dirty ? "DIRTY " : "") +
                ShortPath(doc.Path);
            list.Add(new Locus(
                $"buffer:{doc.DocId}",
                "buffer",
                pulse,
                doc.DiskChanged
                    ? (both
                        ? "go=disk_peek → reload loses edits; or keep_disk"
                        : "go=disk_peek → reload | keep_disk — modified outside")
                    : "go=editor_scene → go=edit_draft",
                doc.DiskChanged ? "disk_peek" : "editor_scene",
                doc));
        }

        if (buffer.Count == 0)
        {
            list.Add(new Locus(
                "buffer:none",
                "buffer",
                "no open buffers",
                "cdp_buffer op=open → go=editor_scene",
                "buffer_scene",
                new { count = 0 }));
        }

        if (EditorComfort.ClipboardLocusDetail() is { } clip)
        {
            list.Add(new Locus(
                "clip:session",
                "clipboard",
                $"clip ×{clip.Count} ({clip.CurrentId})",
                "go=clipboard → paste frame= | clip_clear",
                "clipboard",
                new
                {
                    count = clip.Count,
                    current = clip.CurrentId,
                    chars = clip.Chars,
                    from = clip.From,
                    preview = clip.Preview
                }));
        }

        list.Add(new Locus(
            "debug:session",
            "debug",
            debug.ActiveDap
                ? (debug.Stopped ? "STOPPED" : "dap running") + $" bp={debug.BreakpointCount}"
                : $"idle bp={debug.BreakpointCount}",
            "go=debug_scene",
            "debug_scene",
            debug));

        list.Add(new Locus(
            "test:last",
            "test",
            !test.Available
                ? test.Reason ?? "unavailable"
                : test.LastRun is null
                    ? "no last_run"
                    : $"{(test.Success ? "ok" : "FAIL")} {test.Passed}/{test.Total}",
            test.Failed > 0 ? "go=test_plan" : "go=test_scene",
            test.Failed > 0 ? "test_plan" : "test_scene",
            test));

        list.Add(new Locus(
            "analysis:scene",
            "analysis",
            session.ProjectRoot is { Length: > 0 } ? "analysis ready" : "no project",
            "go=analysis_scene → correspondence|semantic_map|clones",
            "analysis_scene",
            new { features = new[] { "correspondence", "semantic_map", "clones" } }));

        list.Add(new Locus(
            "plan:focus",
            "plan",
            work.Pulse ?? "no plan — feature <name>",
            "go=plan / cmd=\"feature X\" | task Y | done",
            "plan",
            work));

        list.Add(new Locus(
            "mfd:ecl",
            "mfd",
            "ECL (electronic checklist)",
            "go=ecl",
            "ecl",
            new { switch_to = "ecl" }));

        if (quality.Enabled)
        {
            list.Add(new Locus(
                "mfd:gates",
                "mfd",
                quality.Fail > 0 || quality.Warn > 0
                    ? $"quality {quality.Pulse}"
                    : "quality gates ok",
                "go=quality — project-tunable",
                "quality",
                quality));
        }

        return list;
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

    sealed record ShellTab(string Id, string State, string? Cwd, int? LastExit, string? LastCommand);

    sealed record ShellSnap(int TabCount, int Running, int Failed, IReadOnlyList<ShellTab> Tabs);

    static ShellSnap CollectShell(string sceneJson)
    {
        using var doc = JsonDocument.Parse(sceneJson);
        var root = doc.RootElement;
        var tabs = new List<ShellTab>();
        var running = 0;
        var failed = 0;
        if (root.TryGetProperty("tabs", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in arr.EnumerateArray())
            {
                var state = PropStr(t, "state") ?? "unknown";
                if (string.Equals(state, "running", StringComparison.OrdinalIgnoreCase))
                    running++;
                if (string.Equals(state, "failed", StringComparison.OrdinalIgnoreCase))
                    failed++;
                tabs.Add(new ShellTab(
                    PropStr(t, "id") ?? "?",
                    state,
                    PropStr(t, "cwd"),
                    PropInt(t, "last_exit"),
                    Truncate(PropStr(t, "last_command"), 80)));
            }
        }

        return new ShellSnap(PropInt(root, "tab_count") ?? tabs.Count, running, failed, tabs);
    }

    sealed record BufferDoc(
        string DocId,
        string Path,
        string? Language,
        bool Dirty,
        bool DiskChanged,
        int? Version);

    sealed record BufferSnap(int Count, int DirtyCount, int DiskChangedCount, IReadOnlyList<BufferDoc> Docs);

    static BufferSnap CollectBuffer(object sceneObj)
    {
        var json = JsonSerializer.Serialize(sceneObj, Compact);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var docs = new List<BufferDoc>();
        if (root.TryGetProperty("docs", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var d in arr.EnumerateArray())
            {
                docs.Add(new BufferDoc(
                    PropStr(d, "doc_id") ?? "?",
                    PropStr(d, "path") ?? "?",
                    PropStr(d, "language"),
                    PropBool(d, "dirty") == true,
                    PropBool(d, "disk_changed") == true,
                    PropInt(d, "version")));
            }
        }

        return new BufferSnap(
            PropInt(root, "count") ?? docs.Count,
            PropInt(root, "dirty_count") ?? docs.Count(d => d.Dirty),
            PropInt(root, "disk_changed_count") ?? docs.Count(d => d.DiskChanged),
            docs);
    }

    sealed record DebugSnap(bool ActiveDap, bool Stopped, int LastStoppedThreadId, int BreakpointCount);

    static DebugSnap CollectDebug(SessionContext session)
    {
        var ws = session.ProjectRoot ?? session.ScmRoot;
        var target = session.SolutionOrProjectPath;
        var bpCount = 0;
        if (!string.IsNullOrWhiteSpace(ws) && !string.IsNullOrWhiteSpace(target))
        {
            try
            {
                bpCount = BreakpointsStorage.GetBreakpoints(ws, target).Count;
            }
            catch
            {
                /* ignore */
            }
        }

        return new DebugSnap(
            DebugSession.CurrentClient is not null,
            DebugSession.LastStoppedThreadId > 0,
            DebugSession.LastStoppedThreadId,
            bpCount);
    }

    sealed record TestSnap(
        bool Available,
        string? Reason,
        string? Target,
        bool? LastRun,
        bool Success,
        int Total,
        int Passed,
        int Failed,
        object? Detail);

    static TestSnap CollectTest(SessionContext session)
    {
        if (!IdeSessionLifecycle.TryResolveTarget(session, new Dictionary<string, JsonElement>(), out var target, out var err))
            return new TestSnap(false, err, null, null, false, 0, 0, 0, null);

        var last = TestRunCache.TryGet(target);
        if (last is null)
            return new TestSnap(true, null, target, null, false, 0, 0, 0, new { target, last_run = (object?)null });

        return new TestSnap(
            true,
            null,
            target,
            true,
            last.Success,
            last.Total,
            last.Passed,
            last.Failed,
            new
            {
                target,
                at_utc = last.AtUtc,
                success = last.Success,
                total = last.Total,
                passed = last.Passed,
                failed = last.Failed,
                skipped = last.Skipped,
                filter = last.Filter,
                failed_names = last.FailedTests.Select(f => f.Name).Take(12).ToArray()
            });
    }

    sealed record WorkSnap(string? IntentId, string? StageId, string? Pulse);

    static WorkSnap CollectWork(
        IntentWorkspaceStore? store,
        IntentWorkspaceState state,
        SessionContext session)
    {
        if (store is null)
            return new WorkSnap(null, null, "no task store");
        var pulse = IdeTaskManager.PulseLine(store, state, CdpEnumParse.ToWire(session.Phase));
        return new WorkSnap(
            state.ActiveIntentId?.ToString("D"),
            state.ActiveStageId?.ToString("D"),
            pulse);
    }

    static string ShortPath(string path)
    {
        try
        {
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var parent = Path.GetFileName(Path.GetDirectoryName(path));
            if (string.IsNullOrEmpty(name))
                return path;
            return string.IsNullOrEmpty(parent) ? name : $"{parent}/{name}";
        }
        catch
        {
            return path;
        }
    }

    static bool BoolOr(IReadOnlyDictionary<string, JsonElement> args, string key, bool defaultValue)
    {
        if (!args.TryGetValue(key, out var el))
            return defaultValue;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultValue
        };
    }

    static string? OptString(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    static string? PropStr(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    static bool? PropBool(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p)
            ? p.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            }
            : null;

    static int? PropInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
            return null;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n))
            return n;
        return null;
    }

    static string? Truncate(string? s, int max)
    {
        if (s is null)
            return null;
        return s.Length <= max ? s : s[..max] + "…";
    }
}
