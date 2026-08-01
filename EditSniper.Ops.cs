using System.Text;
using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CdpMcp;

internal static partial class EditSniper
{
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
            phase = h.Phase,
            count = h.LineEnd - h.LineStart + 1,
            text = h.PeekText,
            next = ShootNext(),
            hint = h.Phase == PhaseArmed
                ? "Armed. Fire hard-gated: put/paste sniper. Prefer semantic [F:;M:;K:] / X: next aim."
                : "Corridor held but not armed — go=scope to lock+peek."
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
        var fileText = ReadText(store, fromPath);
        zone = ExpandToFullLines(fileText, zone);
        var peek = SliceCorridor(fileText, zone.LineStart, zone.LineEnd);
        Hold = new Corridor(
            fromPath,
            label,
            NormalizeWire(fromWire),
            tillWire,
            zone.LineStart,
            zone.ColumnStart,
            zone.LineEnd,
            zone.ColumnEnd,
            detail,
            PhaseArmed,
            peek);

        var lines = zone.LineEnd - zone.LineStart + 1;
        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "scope",
            process = "sight→lock→arm",
            phase = PhaseArmed,
            hold = HoldCard(),
            resolve = detail,
            count = lines,
            start_line = zone.LineStart,
            end_line = zone.LineEnd,
            text = peek,
            next = ShootNext(),
            hint =
                "Locked+armed (full lines + auto-peek). Fire: put/paste sniper — hard-blocked until armed. " +
                "Prefer semantic [F:;M:;K:] / X:; L: is line_literal."
        }, Pretty);
    }

    

}
