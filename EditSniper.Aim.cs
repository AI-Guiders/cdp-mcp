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
    /// <summary>
    /// Warm buffer + set corridor from wire + peek (Problems / goto aim).
    /// Falls back to line corridor when full csharp resolve fails but L: is present.
    /// </summary>
    public static object AimAtWire(DocumentBufferStore store, SessionContext session, string wire, int pad = 2)
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
            if (scopeDoc.RootElement.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.False)
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
            hint = "Sniper aimed (armed). Fire: put/paste sniper=true | go=target | scope_clear."
        };
    }

    static bool TryAimFromLineWire(DocumentBufferStore store, SessionContext session, string wire, out string? error)
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
        var text = ReadText(store, path);
        var zone = ExpandToFullLines(text, new BracketSyntaxResolve.TextRange(line, 1, end, 1));
        var peek = SliceCorridor(text, zone.LineStart, zone.LineEnd);
        Hold = new Corridor(path, label, from, null, zone.LineStart, zone.ColumnStart, zone.LineEnd, zone.ColumnEnd, "line_literal", PhaseArmed, peek);
        EditorComfort.RememberFile(path);
        return true;
    }

    static string? PropString(JsonElement el, string name) => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}