using System.Text;
using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CdpMcp;

/// <summary>
/// Edit sniper — scope (From/Till corridor) → target (outline inside) → shoot (edit_plan).
/// Hold lives in-process for the MCP session (kj-20260724-1848).
/// </summary>
internal static class EditSniper
{
    public const string Schema = "edit_sniper/v0";
    public const string ToolName = "cdp_edit_sniper";
    public const int MaxTargets = 48;

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    sealed record Corridor(
        string Path,
        string FileLabel,
        string FromWire,
        string? TillWire,
        int LineStart,
        int ColumnStart,
        int LineEnd,
        int ColumnEnd,
        string ResolveDetail);

    static Corridor? Hold;

    public static bool HasHold => Hold is not null;

    public static string? PulseLine =>
        Hold is { } h
            ? $"L{h.LineStart}-{h.LineEnd} @ {Path.GetFileName(h.Path)}"
            : null;

    public static object? HoldCard() =>
        Hold is { } h
            ? new
            {
                path = h.Path,
                from = h.FromWire,
                till = h.TillWire,
                line_start = h.LineStart,
                line_end = h.LineEnd,
                lines = h.LineEnd - h.LineStart + 1
            }
            : null;

    /// <summary>For clipboard paste into sniper corridor (before/after/replace).</summary>
    public static bool TryGetHold(
        out string path,
        out string fileLabel,
        out int lineStart,
        out int columnStart,
        out int lineEnd,
        out int columnEnd)
    {
        if (Hold is not { } h)
        {
            path = "";
            fileLabel = "";
            lineStart = columnStart = lineEnd = columnEnd = 0;
            return false;
        }

        path = h.Path;
        fileLabel = h.FileLabel;
        lineStart = h.LineStart;
        columnStart = h.ColumnStart;
        lineEnd = h.LineEnd;
        columnEnd = h.ColumnEnd;
        return true;
    }

    public static bool IsSniperTool(string name) =>
        string.Equals(name, ToolName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Warm buffer + set corridor from wire + peek (Problems / goto aim).
    /// Falls back to line corridor when full csharp resolve fails but L: is present.
    /// </summary>
    public static object AimAtWire(
        DocumentBufferStore store,
        SessionContext session,
        string wire,
        int pad = 2)
    {
        if (string.IsNullOrWhiteSpace(wire))
        {
            return new
            {
                schema = Schema,
                ok = false,
                op = "aim",
                error = "wire_required",
                hint = "Pass [F:…;L:…] or member wire."
            };
        }

        // Warm buffer into store before corridor (desk presence).
        try
        {
            var span = BracketLocate.Parse(wire);
            if (span.File is { Length: > 0 })
            {
                var path = ResolveUserPath(session, span.File);
                if (File.Exists(path) || store.TryGet(path, out _))
                    store.Open(path);
            }
        }
        catch
        {
            // Scope will report parse/resolve errors.
        }

        var scopeArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement("scope"),
            ["from"] = JsonSerializer.SerializeToElement(wire.Trim())
        };
        var scopeRaw = Scope(store, session, scopeArgs);
        using (var scopeDoc = JsonDocument.Parse(scopeRaw))
        {
            if (scopeDoc.RootElement.TryGetProperty("ok", out var okEl)
                && okEl.ValueKind == JsonValueKind.False)
            {
                if (!TryAimFromLineWire(store, session, wire, out var lineErr))
                {
                    return new
                    {
                        schema = Schema,
                        ok = false,
                        op = "aim",
                        error = PropString(scopeDoc.RootElement, "error") ?? "scope_failed",
                        fallback = lineErr,
                        wire,
                        hint = "Wire must resolve (F:+L:/M:). Open file exists?"
                    };
                }
            }
        }

        var peekArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement("peek"),
            ["pad"] = JsonSerializer.SerializeToElement(Math.Clamp(pad, 0, 40)),
            ["max_lines"] = JsonSerializer.SerializeToElement(40)
        };
        var peekRaw = Peek(store, session, peekArgs);
        object? peek = null;
        try
        {
            peek = JsonSerializer.Deserialize<JsonElement>(peekRaw);
        }
        catch
        {
            peek = peekRaw;
        }

        return new
        {
            schema = Schema,
            ok = true,
            op = "aim",
            wire = NormalizeWire(wire),
            hold = HoldCard(),
            peek,
            hint = "Sniper aimed. go=target | edit_draft | scope_clear."
        };
    }

    static bool TryAimFromLineWire(
        DocumentBufferStore store,
        SessionContext session,
        string wire,
        out string? error)
    {
        error = null;
        BracketLocate.Span span;
        try
        {
            span = BracketLocate.Parse(wire);
        }
        catch (Exception ex)
        {
            error = $"anchor_parse:{ex.Message}";
            return false;
        }

        if (span.File is not { Length: > 0 } || span.LineStart is not int line)
        {
            error = "F_and_L_required_for_fallback";
            return false;
        }

        var path = ResolveUserPath(session, span.File);
        if (!File.Exists(path) && !store.TryGet(path, out _))
        {
            error = "file_missing";
            return false;
        }

        try
        {
            store.Open(path);
        }
        catch (Exception ex)
        {
            error = $"open:{ex.Message}";
            return false;
        }

        var end = span.LineEnd ?? line;
        var label = FileLabel(session, path);
        var from = NormalizeWire(wire);
        Hold = new Corridor(
            path,
            label,
            from,
            null,
            line,
            1,
            end,
            1,
            "line_fallback");
        EditorComfort.RememberFile(path);
        return true;
    }

    static string? PropString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    public static string Dispatch(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var op = (OptString(args, "op") ?? "status").Trim().ToLowerInvariant();
        return op switch
        {
            "scope" or "set" => Scope(store, session, args),
            "target" or "outline" => Target(store, session, args),
            "peek" or "view" => Peek(store, session, args),
            "aim" => JsonSerializer.Serialize(
                AimAtWire(
                    store,
                    session,
                    OptString(args, "wire") ?? OptString(args, "anchor") ?? OptString(args, "from") ?? "",
                    IntOr(args, "pad", 2)),
                Pretty),
            "clear" or "scope_clear" => Clear(),
            "status" or "show" => Status(),
            _ => JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                error = "unknown_op",
                hint = "op=scope|target|peek|aim|clear|status"
            }, Pretty)
        };
    }

    static string Clear()
    {
        Hold = null;
        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "clear",
            hold = (object?)null,
            next = new object[]
            {
                new { go = "scope", label = "Set corridor", why = "from=/till= anchors" }
            },
            hint = "Sniper cleared. go=scope from=… till=… to aim again."
        }, Pretty);
    }

    static string Status()
    {
        if (Hold is null)
        {
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = true,
                op = "status",
                hold = (object?)null,
                next = new object[]
                {
                    new { go = "scope", label = "Set corridor", why = "from=/till= [F:;M:;S:/L:]" }
                },
                hint = "No aim. go=scope + go_args.from (+ optional till)."
            }, Pretty);
        }

        var h = Hold;
        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "status",
            hold = HoldCard(),
            count = h.LineEnd - h.LineStart + 1,
            next = ShootNext(),
            hint = "Corridor held. go=target for in-corridor outline; then edit_draft / edit_plan."
        }, Pretty);
    }

    static string Scope(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var fromWire = OptString(args, "from") ?? OptString(args, "select_from") ?? OptString(args, "anchor");
        if (string.IsNullOrWhiteSpace(fromWire))
        {
            if (Hold is not null)
                return Status();
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                op = "scope",
                error = "from_required",
                hint = "go_args: { from: \"[F:…;M:…;S:while]\" , till?: \"…\" | body }"
            }, Pretty);
        }

        if (!TryResolveWire(store, session, fromWire, out var fromPath, out var fromRange, out var fromDetail, out var fromErr))
        {
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                op = "scope",
                error = fromErr,
                from = fromWire
            }, Pretty);
        }

        var tillRaw = OptString(args, "till") ?? OptString(args, "select_till") ?? OptString(args, "to");
        BracketSyntaxResolve.TextRange zone = fromRange;
        string? tillWire = null;
        var detail = fromDetail;

        if (!string.IsNullOrWhiteSpace(tillRaw)
            && !string.Equals(tillRaw, fromWire, StringComparison.Ordinal))
        {
            if (IsBodyTill(tillRaw))
            {
                if (!TryExpandToMemberBody(store, fromPath, fromRange, out var bodyZone, out var bodyDetail, out var bodyErr))
                {
                    return JsonSerializer.Serialize(new
                    {
                        schema = Schema,
                        ok = false,
                        op = "scope",
                        error = bodyErr,
                        from = fromWire,
                        till = tillRaw
                    }, Pretty);
                }

                // From stays; Till = end of enclosing member body (corridor, not whole-body replace).
                zone = new BracketSyntaxResolve.TextRange(
                    fromRange.LineStart,
                    fromRange.ColumnStart,
                    bodyZone.LineEnd,
                    bodyZone.ColumnEnd);
                tillWire = "body";
                detail = $"{fromDetail}+{bodyDetail}";
            }
            else if (IsEnclosingTill(tillRaw))
            {
                if (!TryExpandToEnclosingMember(store, fromPath, fromRange, out zone, out var encDetail, out var encErr))
                {
                    return JsonSerializer.Serialize(new
                    {
                        schema = Schema,
                        ok = false,
                        op = "scope",
                        error = encErr,
                        from = fromWire,
                        till = tillRaw
                    }, Pretty);
                }

                tillWire = "enclosing";
                detail = $"{fromDetail}+{encDetail}";
            }
            else
            {
                if (!TryResolveWire(store, session, tillRaw, out var tillPath, out var tillRange, out var tillDetail, out var tillErr))
                {
                    return JsonSerializer.Serialize(new
                    {
                        schema = Schema,
                        ok = false,
                        op = "scope",
                        error = tillErr,
                        till = tillRaw
                    }, Pretty);
                }

                if (!string.Equals(fromPath, tillPath, StringComparison.OrdinalIgnoreCase))
                {
                    return JsonSerializer.Serialize(new
                    {
                        schema = Schema,
                        ok = false,
                        op = "scope",
                        error = "from_till_different_files",
                        from_path = fromPath,
                        till_path = tillPath
                    }, Pretty);
                }

                zone = MergeZones(fromRange, tillRange);
                tillWire = NormalizeWire(tillRaw);
                detail = $"{fromDetail}+{tillDetail}";
            }
        }

        var label = FileLabel(session, fromPath);
        Hold = new Corridor(
            fromPath,
            label,
            NormalizeWire(fromWire),
            tillWire,
            zone.LineStart,
            zone.ColumnStart,
            zone.LineEnd,
            zone.ColumnEnd,
            detail);

        var lines = zone.LineEnd - zone.LineStart + 1;
        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "scope",
            hold = HoldCard(),
            resolve = detail,
            count = lines,
            next = ShootNext(),
            hint = "Corridor set. go=target → outline inside From–Till (not file-wide)."
        }, Pretty);
    }

    static string Target(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (Hold is null)
        {
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                op = "target",
                error = "no_scope",
                hint = "go=scope from=… first"
            }, Pretty);
        }

        var h = Hold;
        var text = ReadText(store, h.Path);
        var tree = CSharpSyntaxTree.ParseText(text);
        var root = tree.GetCompilationUnitRoot();
        var max = Math.Clamp(IntOr(args, "max", MaxTargets), 1, 120);

        var nodes = new List<object>();
        foreach (var node in root.DescendantNodes())
        {
            if (!IsInteresting(node))
                continue;
            var span = node.GetLocation().GetLineSpan();
            var ls = span.StartLinePosition.Line + 1;
            var le = span.EndLinePosition.Line + 1;
            if (!Overlaps(ls, le, h.LineStart, h.LineEnd))
                continue;
            // Prefer nodes that begin inside the corridor (avoid enclosing method leak).
            if (ls < h.LineStart)
                continue;

            var member = node.AncestorsAndSelf().OfType<MemberDeclarationSyntax>().FirstOrDefault();
            var memberKey = member is null ? null : MemberName(member);
            var kind = KindOf(node);
            var name = NameOf(node);
            var wire = BracketLocate.Format(new BracketLocate.Span(
                h.FileLabel,
                memberKey,
                ls,
                le == ls ? null : le));

            nodes.Add(new
            {
                kind,
                name,
                member = memberKey,
                line_start = ls,
                line_end = le,
                wire
            });

            if (nodes.Count >= max)
                break;
        }

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "target",
            hold = HoldCard(),
            count = nodes.Count,
            truncated = nodes.Count >= max,
            targets = nodes,
            next = new object[]
            {
                new { go = "peek", label = "Peek wire/corridor", why = "go_args.wire= optional" },
                new { go = "edit_draft", label = "Draft mutate/fix", why = "pick wire → YAML / fix" },
                new { go = "scope_clear", label = "Clear aim", why = "drop corridor" }
            },
            hint =
                "Targets are in-corridor only. go=peek wire=… → tiny window; then edit_plan. " +
                "Wide file outline is not default."
        }, Pretty);
    }

    static string Peek(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (Hold is null)
        {
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                op = "peek",
                error = "no_scope",
                hint = "go=scope from=… first (or peek after target)"
            }, Pretty);
        }

        var h = Hold;
        var pad = Math.Clamp(IntOr(args, "pad", 0), 0, 40);
        var maxLines = Math.Clamp(IntOr(args, "max_lines", 60), 1, 200);
        var wire = OptString(args, "wire") ?? OptString(args, "anchor") ?? OptString(args, "at");

        int lineStart;
        int lineEnd;
        string? resolve = null;
        string? usedWire = null;

        if (!string.IsNullOrWhiteSpace(wire))
        {
            if (!TryResolveWire(store, session, wire, out var path, out var range, out var detail, out var err))
            {
                return JsonSerializer.Serialize(new
                {
                    schema = Schema,
                    ok = false,
                    op = "peek",
                    error = err,
                    wire
                }, Pretty);
            }

            if (!string.Equals(path, h.Path, StringComparison.OrdinalIgnoreCase))
            {
                return JsonSerializer.Serialize(new
                {
                    schema = Schema,
                    ok = false,
                    op = "peek",
                    error = "wire_outside_hold_file",
                    hold_path = h.Path,
                    wire_path = path
                }, Pretty);
            }

            lineStart = range.LineStart;
            lineEnd = range.LineEnd;
            resolve = detail;
            usedWire = NormalizeWire(wire);
        }
        else
        {
            lineStart = h.LineStart;
            lineEnd = h.LineEnd;
            usedWire = null;
        }

        lineStart = Math.Max(1, lineStart - pad);
        lineEnd += pad;

        var text = ReadText(store, h.Path);
        var allLines = SplitLines(text);
        lineEnd = Math.Min(allLines.Count, lineEnd);
        if (lineEnd - lineStart + 1 > maxLines)
            lineEnd = lineStart + maxLines - 1;

        var slice = new StringBuilder();
        for (var i = lineStart; i <= lineEnd; i++)
        {
            if (i > lineStart) slice.Append('\n');
            slice.Append(allLines[i - 1]);
        }

        var locusWire = usedWire
            ?? BracketLocate.Format(new BracketLocate.Span(
                h.FileLabel, null, lineStart, lineEnd == lineStart ? null : lineEnd));
        EditorComfort.PushLocus(session, locusWire);
        EditorComfort.RememberFile(h.Path);

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "peek",
            hold = HoldCard(),
            wire = usedWire,
            resolve,
            start_line = lineStart,
            end_line = lineEnd,
            count = lineEnd - lineStart + 1,
            text = slice.ToString(),
            next = new object[]
            {
                new { go = "edit_draft", label = "Shoot (draft)", why = "mutate/fix with seen wire" },
                new { go = "back", label = "Nav back", why = "locus stack" },
                new { go = "target", label = "Re-outline", why = "pick another wire" },
                new { go = "scope_clear", label = "Clear aim", why = "drop corridor" }
            },
            hint = "Peek is corridor/wire only — not a file dump. pad= for ± lines. Pushes locus nav."
        }, Pretty);
    }

    static List<string> SplitLines(string text)
    {
        var list = new List<string>();
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
            list.Add(line);
        return list;
    }

    static object[] ShootNext() =>
    [
        new { go = "target", label = "Outline corridor", why = "sniper step 2" },
        new { go = "peek", label = "Peek corridor", why = "tiny window before shoot" },
        new { go = "paste_sniper", label = "Paste frame into aim", why = "clipboard → replace hold" },
        new { go = "put_sniper", label = "Put draft into aim", why = "text=/frame= thick rewrite" },
        new { go = "edit_draft", label = "Shoot (draft)", why = "mutate/fix plan" },
        new { go = "scope_clear", label = "Clear aim", why = "drop From/Till" }
    ];

    static bool TryResolveWire(
        DocumentBufferStore store,
        SessionContext session,
        string wire,
        out string path,
        out BracketSyntaxResolve.TextRange range,
        out string detail,
        out string error)
    {
        path = "";
        range = default!;
        detail = "";
        error = "";
        BracketLocate.Span span;
        try
        {
            span = BracketLocate.Parse(wire);
        }
        catch (Exception ex)
        {
            error = $"anchor_parse:{ex.Message}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(span.File))
        {
            error = "F_required";
            return false;
        }

        path = ResolveUserPath(session, span.File);
        var family = BracketLocate.ClassifyFamily(span, out var familyError);
        if (familyError is not null)
        {
            error = familyError;
            return false;
        }

        if (family != BracketLocate.AxisFamily.Csharp)
        {
            error = "csharp_axes_required";
            return false;
        }

        var text = ReadText(store, path);
        if (!BracketSyntaxResolve.TryResolve(path, text, span, out range, out detail))
        {
            error = $"anchor_resolve:{detail}";
            return false;
        }

        return true;
    }

    static bool TryExpandToMemberBody(
        DocumentBufferStore store,
        string path,
        BracketSyntaxResolve.TextRange from,
        out BracketSyntaxResolve.TextRange zone,
        out string detail,
        out string error)
    {
        zone = from;
        detail = "";
        error = "";
        var text = ReadText(store, path);
        var tree = CSharpSyntaxTree.ParseText(text);
        var root = tree.GetCompilationUnitRoot();
        var pos = tree.GetText().Lines[Math.Clamp(from.LineStart - 1, 0, tree.GetText().Lines.Count - 1)].Start
                  + Math.Max(0, from.ColumnStart - 1);
        var node = root.FindToken(pos).Parent;
        BlockSyntax? body =
            node?.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Body
            ?? node?.AncestorsAndSelf().OfType<LocalFunctionStatementSyntax>().FirstOrDefault()?.Body
            ?? node?.AncestorsAndSelf().OfType<AccessorDeclarationSyntax>().FirstOrDefault()?.Body
            ?? node?.AncestorsAndSelf().OfType<ConstructorDeclarationSyntax>().FirstOrDefault()?.Body;
        if (body is null)
        {
            error = "member_body_not_found";
            return false;
        }

        var span = body.GetLocation().GetLineSpan();
        zone = new BracketSyntaxResolve.TextRange(
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1,
            span.EndLinePosition.Line + 1,
            Math.Max(1, span.EndLinePosition.Character + 1));
        detail = "member_body";
        return true;
    }

    static bool TryExpandToEnclosingMember(
        DocumentBufferStore store,
        string path,
        BracketSyntaxResolve.TextRange from,
        out BracketSyntaxResolve.TextRange zone,
        out string detail,
        out string error)
    {
        zone = from;
        detail = "";
        error = "";
        var text = ReadText(store, path);
        var tree = CSharpSyntaxTree.ParseText(text);
        var root = tree.GetCompilationUnitRoot();
        var pos = tree.GetText().Lines[Math.Clamp(from.LineStart - 1, 0, tree.GetText().Lines.Count - 1)].Start
                  + Math.Max(0, from.ColumnStart - 1);
        var node = root.FindToken(pos).Parent;
        var member = node?.AncestorsAndSelf().OfType<MemberDeclarationSyntax>().FirstOrDefault();
        if (member is null)
        {
            error = "enclosing_member_not_found";
            return false;
        }

        var span = member.GetLocation().GetLineSpan();
        zone = new BracketSyntaxResolve.TextRange(
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1,
            span.EndLinePosition.Line + 1,
            Math.Max(1, span.EndLinePosition.Character + 1));
        detail = "enclosing_member";
        return true;
    }

    static BracketSyntaxResolve.TextRange MergeZones(
        BracketSyntaxResolve.TextRange a,
        BracketSyntaxResolve.TextRange b)
    {
        var lineStart = Math.Min(a.LineStart, b.LineStart);
        var lineEnd = Math.Max(a.LineEnd, b.LineEnd);
        var colStart = a.LineStart <= b.LineStart ? a.ColumnStart : b.ColumnStart;
        var colEnd = a.LineEnd >= b.LineEnd ? a.ColumnEnd : b.ColumnEnd;
        return new BracketSyntaxResolve.TextRange(lineStart, colStart, lineEnd, colEnd);
    }

    static bool IsInteresting(SyntaxNode node) => node is
        MethodDeclarationSyntax
        or PropertyDeclarationSyntax
        or ConstructorDeclarationSyntax
        or LocalFunctionStatementSyntax
        or LocalDeclarationStatementSyntax
        or IfStatementSyntax
        or WhileStatementSyntax
        or ForStatementSyntax
        or ForEachStatementSyntax
        or SwitchStatementSyntax
        or ReturnStatementSyntax
        or ThrowStatementSyntax
        or TryStatementSyntax
        or LockStatementSyntax
        or UsingStatementSyntax;

    static string KindOf(SyntaxNode node) => node switch
    {
        MethodDeclarationSyntax => "method",
        PropertyDeclarationSyntax => "property",
        ConstructorDeclarationSyntax => "ctor",
        LocalFunctionStatementSyntax => "local_function",
        LocalDeclarationStatementSyntax => "local",
        IfStatementSyntax => "if",
        WhileStatementSyntax => "while",
        ForStatementSyntax => "for",
        ForEachStatementSyntax => "foreach",
        SwitchStatementSyntax => "switch",
        ReturnStatementSyntax => "return",
        ThrowStatementSyntax => "throw",
        TryStatementSyntax => "try",
        LockStatementSyntax => "lock",
        UsingStatementSyntax => "using",
        _ => node.Kind().ToString()
    };

    static string? NameOf(SyntaxNode node) => node switch
    {
        MethodDeclarationSyntax m => m.Identifier.ValueText,
        PropertyDeclarationSyntax p => p.Identifier.ValueText,
        ConstructorDeclarationSyntax c => c.Identifier.ValueText,
        LocalFunctionStatementSyntax lf => lf.Identifier.ValueText,
        LocalDeclarationStatementSyntax ld => string.Join(',',
            ld.Declaration.Variables.Select(v => v.Identifier.ValueText)),
        ForEachStatementSyntax fe => fe.Identifier.ValueText,
        _ => null
    };

    static string MemberName(MemberDeclarationSyntax m) => m switch
    {
        MethodDeclarationSyntax x => x.Identifier.ValueText,
        PropertyDeclarationSyntax x => x.Identifier.ValueText,
        ConstructorDeclarationSyntax x => x.Identifier.ValueText,
        FieldDeclarationSyntax x => x.Declaration.Variables.FirstOrDefault()?.Identifier.ValueText ?? "field",
        EventDeclarationSyntax x => x.Identifier.ValueText,
        IndexerDeclarationSyntax => "this",
        OperatorDeclarationSyntax => "operator",
        ConversionOperatorDeclarationSyntax => "conversion",
        TypeDeclarationSyntax x => x.Identifier.ValueText,
        _ => m.GetType().Name
    };

    static bool Overlaps(int aStart, int aEnd, int bStart, int bEnd) =>
        aStart <= bEnd && bStart <= aEnd;

    static bool IsBodyTill(string raw) =>
        raw.Equals("body", StringComparison.OrdinalIgnoreCase)
        || raw.Equals("member_body", StringComparison.OrdinalIgnoreCase);

    static bool IsEnclosingTill(string raw) =>
        raw.Equals("enclosing", StringComparison.OrdinalIgnoreCase)
        || raw.Equals("member", StringComparison.OrdinalIgnoreCase)
        || raw.Equals("enclosing_member", StringComparison.OrdinalIgnoreCase);

    static string NormalizeWire(string wire)
    {
        var t = wire.Trim();
        if (!t.StartsWith('['))
            t = "[" + t;
        if (!t.EndsWith(']'))
            t += "]";
        return t;
    }

    static string FileLabel(SessionContext session, string absolutePath)
    {
        var root = session.ProjectRoot;
        if (root is { Length: > 0 })
        {
            var fullRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var full = Path.GetFullPath(absolutePath);
            if (full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return full[(fullRoot.Length + 1)..].Replace('\\', '/');
            }
        }

        return Path.GetFileName(absolutePath);
    }

    static string ReadText(DocumentBufferStore store, string path)
    {
        var open = store.All.FirstOrDefault(b =>
            string.Equals(b.Path, path, StringComparison.OrdinalIgnoreCase));
        if (open is not null)
            return open.Text;
        if (!File.Exists(path))
            throw new FileNotFoundException("sniper path missing", path);
        return File.ReadAllText(path);
    }

    static string ResolveUserPath(SessionContext session, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var p = path.Trim();
        if (Path.IsPathRooted(p))
            return Path.GetFullPath(p);
        var root = session.ProjectRoot is { Length: > 0 } pr
            ? pr
            : Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(root, p));
    }

    static string? OptString(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    static int IntOr(IReadOnlyDictionary<string, JsonElement> args, string key, int defaultValue)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.Number)
            return defaultValue;
        return el.TryGetInt32(out var n) ? n : defaultValue;
    }
}
