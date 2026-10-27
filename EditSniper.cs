using System.Text;
using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CdpMcp;

/// <summary>
/// Edit sniper — process <c>sight → lock → arm → fire → verify</c> (kj-1848).
/// <c>scope</c> = lock: full-line expand + auto-peek → <c>phase=armed</c>.
/// Fire (put/paste sniper) is hard-blocked until armed — no peek ritual for the agent.
/// Prefer semantic wires [Kind:CodeEdit; File:…; Member:…] / xml Element:; Text: needle content_literal (survives L-drift); Line: alone is line_literal corridor (not Roslyn node snap).
/// </summary>
internal static partial class EditSniper
{
    public const string Schema = "edit_sniper/v0";
    public const string ToolName = "cdp_edit_sniper";
    public const string PhaseArmed = "armed";
    public const int MaxTargets = 48;

    static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        // Corridor и рекорды снайпера сериализуются в snake — как весь CDP-провод
        // и как схему их генерирует ToolSchemas (единый источник имён).
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    internal sealed record Corridor(
        string Path,
        string FileLabel,
        string FromWire,
        string? TillWire,
        int LineStart,
        int ColumnStart,
        int LineEnd,
        int ColumnEnd,
        string ResolveDetail,
        string Phase,
        string? PeekText);

    /// <summary>Wire node для op=target — SchemaGen-источник (единственный ранее безымянный проекции).</summary>
    internal sealed record SniperTarget(string kind, string name, string? member, int line_start, int line_end, string wire);

    /// <summary>Suggested next step (cdp_edit_sniper next) — SchemaGen-источник.</summary>
    internal sealed record SniperNextStep(string go, string label, string why);

    static Corridor? Hold;

    public static bool HasHold => Hold is not null;

    public static bool IsArmed => Hold is { Phase: PhaseArmed };

    public static string? PulseLine =>
        Hold is { } h
            ? $"{h.Phase} L{h.LineStart}-{h.LineEnd} @ {Path.GetFileName(h.Path)}"
            : null;

    public static object? HoldCard() =>
        Hold is { } h ? h : null;

    /// <summary>Hard gate for put/paste sniper — hold alone is not enough.</summary>
    public static bool TryEnsureFire(out string error, out string hint)
    {
        if (Hold is null)
        {
            error = "no_sniper_hold";
            hint = "go=scope from=/till= (lock auto-arms with peek) then put/paste sniper=true";
            return false;
        }

        if (Hold.Phase != PhaseArmed)
        {
            error = "sniper_not_armed";
            hint = "Fire hard-blocked until phase=armed. go=scope — lock expands full lines + auto-peek.";
            return false;
        }

        error = "";
        hint = "";
        return true;
    }

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


}
