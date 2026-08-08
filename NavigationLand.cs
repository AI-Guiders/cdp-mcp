using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>
/// Land via <c>Family:navigation</c> Anchor wire (ADR 0186). Not Deep-Link / URI.
/// Nested <c>Anchor:[…]</c> reuses the same BracketLocate resolve path.
/// </summary>
internal static class NavigationLand
{
    public const string Schema = "navigation_land/v1";

    static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static async Task<string> RunAsync(
        IReadOnlyDictionary<string, JsonElement> args,
        SessionContext session,
        DocumentBufferStore buffers,
        Func<string, ProjectOpenResult> detectOpen,
        Action? syncShellCwd,
        Action? notifyListChanged,
        Func<string, IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<string>>? dispatchTool,
        CancellationToken cancellationToken)
    {
        var wire = Opt(args, "anchor") ?? Opt(args, "at") ?? Opt(args, "wire");
        if (string.IsNullOrWhiteSpace(wire))
            return Fail("anchor_required", "Pass anchor=[Family:navigation;Command:…;…]");

        BracketLocate.Span span;
        try
        {
            span = BracketLocate.Parse(wire);
        }
        catch (ArgumentException ex)
        {
            return Fail("bad_anchor", ex.Message);
        }

        var family = BracketLocate.ClassifyFamily(span, out var famErr);
        if (famErr is not null)
            return Fail(famErr, wire);
        if (family != BracketLocate.AxisFamily.Navigation)
            return Fail("not_navigation_family", "Expected Family:navigation (or Command/Go/Anchor). Code/xml → edit_op=anchor.");

        var command = (span.Command ?? "").Trim().ToLowerInvariant();
        if (command.Length == 0)
            return Fail("command_required", "Command:open|goto|restore|show|go");

        return command switch
        {
            "restore" => LandRestore(session, buffers, detectOpen, syncShellCwd, notifyListChanged, span),
            "open" or "goto" => LandOpenOrGoto(session, buffers, span, command, args),
            "show" => LandShow(session, span, args),
            "go" => await LandGoAsync(span, dispatchTool, cancellationToken).ConfigureAwait(false),
            _ => Fail("unknown_command", $"Command:{command}")
        };
    }

    static string LandRestore(
        SessionContext session,
        DocumentBufferStore buffers,
        Func<string, ProjectOpenResult> detectOpen,
        Action? syncShellCwd,
        Action? notifyListChanged,
        BracketLocate.Span span)
    {
        var raw = DeskBookmark.Restore(session, buffers, detectOpen, syncShellCwd, notifyListChanged);
        return Ok("restore", span, JsonSerializer.Deserialize<JsonElement>(raw));
    }

    static string LandOpenOrGoto(
        SessionContext session,
        DocumentBufferStore buffers,
        BracketLocate.Span span,
        string command,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var inner = span.NestedAnchor
                    ?? (string.IsNullOrWhiteSpace(span.File)
                        ? null
                        : new BracketLocate.Span(span.File, span.MemberKey, span.LineStart, span.LineEnd));
        if (inner is null || string.IsNullOrWhiteSpace(inner.File))
            return Fail("anchor_file_required", "Nested Anchor:[File:…] required for Command:open|goto");

        var full = ResolvePath(session, inner.File!);
        if (!File.Exists(full))
            return Fail("not_found", full);

        var buf = buffers.Open(full, refresh: false);
        EditorComfort.RememberFile(buf.Path);
        EditorComfort.PushLocus(session, buf.Path);
        DeskBookmark.Save(session, buffers);

        object? peek = null;
        var line = inner.LineStart;
        if (line is > 0 || !string.IsNullOrWhiteSpace(inner.MemberKey))
        {
            if (!string.IsNullOrWhiteSpace(inner.MemberKey)
                && BracketSyntaxResolve.TryResolve(buf.Path, buf.Text, inner, out var range, out _))
                line = range.LineStart;

            if (line is > 0)
            {
                var lines = buf.Text.Replace("\r\n", "\n").Split('\n');
                var start = Math.Clamp(line.Value, 1, Math.Max(1, lines.Length));
                var end = Math.Min(lines.Length, start + 8);
                peek = new
                {
                    start_line = start,
                    end_line = end,
                    text = string.Join("\n", lines.Skip(start - 1).Take(end - start + 1)),
                    member = inner.MemberKey
                };
            }
        }

        // Default quiet dual-HCI: Human Face only when show_face=true invite.
        var showFace = OptBool(args, "show_face");
        NavigationLandLatch.Publish(
            command, buf.Path, line, inner.MemberKey, BracketLocate.Format(span), showFace);

        return Ok(command, span, new
        {
            path = buf.Path,
            doc_id = buf.DocId,
            line,
            member = inner.MemberKey,
            show_face = showFace,
            peek,
            nested_wire = BracketLocate.Format(inner),
            latch = NavigationLandLatch.LatchPath,
            hint = showFace
                ? "Landed + Face invite. Edit → edit_op=anchor with code/xml family (not navigation)."
                : "Landed quiet (Agent-Side). show_face=true to invite Human Glass Face."
        });
    }


    static string LandShow(
        SessionContext session,
        BracketLocate.Span span,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var path = span.NestedAnchor?.File ?? span.File;
        if (string.IsNullOrWhiteSpace(path))
            return Fail("file_required", "Command:show needs Anchor:[File:…]");

        var full = ResolvePath(session, path!);
        var exists = File.Exists(full);
        // Command:show = invite Human Face when file exists (dual-HCI share slice).
        var showFace = OptBool(args, "show_face", defaultValue: true);
        if (exists && showFace)
            NavigationLandLatch.Publish("show", full, span.LineStart, span.MemberKey, BracketLocate.Format(span), showFace: true);

        return Ok("show", span, new
        {
            path = full,
            exists,
            show_face = exists && showFace,
            latch = exists && showFace ? NavigationLandLatch.LatchPath : null,
            hint = exists
                ? (showFace
                    ? "Face invite published — Glass may open AvalonEdit + PreferSurface."
                    : "Artifact path — show_face=false kept quiet; Read for PNG.")
                : "Path missing on disk."
        });
    }

    static async Task<string> LandGoAsync(
        BracketLocate.Span span,
        Func<string, IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<string>>? dispatchTool,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(span.Go))
            return Fail("go_required", "Command:go needs Go:editor_scene|git_scene|…");

        if (dispatchTool is null)
            return Fail("no_dispatch", "Tool dispatch unavailable.");

        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["go"] = JsonSerializer.SerializeToElement(span.Go!.Trim())
        };
        var raw = await dispatchTool("cdp_cockpit", args, cancellationToken).ConfigureAwait(false);

        object? nestedLand = null;
        // Optional: after organ switch, also open nested file locus — caller can chain cdp_land open.
        if (span.NestedAnchor?.File is { Length: > 0 })
            nestedLand = new { pending_nested = BracketLocate.Format(span.NestedAnchor), hint = "Organ switched; land nested with Command:open if needed." };

        return Ok("go", span, new
        {
            go = span.Go,
            cockpit = JsonSerializer.Deserialize<JsonElement>(raw),
            nested = nestedLand
        });
    }

    static string ResolvePath(SessionContext session, string path)
    {
        path = path.Trim().Trim('"');
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);
        var root = session.ProjectRoot ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
    }

    static string Ok(string command, BracketLocate.Span span, object? result) =>
        JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            family = "navigation",
            command,
            wire = BracketLocate.Format(span),
            result
        }, Pretty);

    static string Fail(string error, string? detail = null) =>
        JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = false,
            family = "navigation",
            error,
            detail
        }, Pretty);

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    static bool OptBool(
        IReadOnlyDictionary<string, JsonElement> args,
        string key,
        bool defaultValue = false)
    {
        if (!args.TryGetValue(key, out var el))
            return defaultValue;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(el.GetString(), out var b) ? b : defaultValue,
            JsonValueKind.Number => el.TryGetInt32(out var n) && n != 0,
            _ => defaultValue
        };
    }
}
