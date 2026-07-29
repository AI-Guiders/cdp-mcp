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
            "open" or "goto" => LandOpenOrGoto(session, buffers, span, command),
            "show" => LandShow(session, span),
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
        string command)
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

        NavigationLandLatch.Publish(command, buf.Path, line, inner.MemberKey, BracketLocate.Format(span));

        return Ok(command, span, new
        {
            path = buf.Path,
            doc_id = buf.DocId,
            line,
            member = inner.MemberKey,
            peek,
            nested_wire = BracketLocate.Format(inner),
            latch = NavigationLandLatch.LatchPath,
            hint = "Landed. Edit → edit_op=anchor with code/xml family (not navigation)."
        });
    }


    static string LandShow(SessionContext session, BracketLocate.Span span)
    {
        var path = span.NestedAnchor?.File ?? span.File;
        if (string.IsNullOrWhiteSpace(path))
            return Fail("file_required", "Command:show needs Anchor:[File:…]");

        var full = ResolvePath(session, path!);
        return Ok("show", span, new
        {
            path = full,
            exists = File.Exists(full),
            hint = File.Exists(full)
                ? "Artifact path — Read (or vision) for PNG; not CodeAnchor edit."
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
}
