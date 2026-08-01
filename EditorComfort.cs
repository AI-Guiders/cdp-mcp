using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>
/// Basic editor comfort humans forget to ask for: undo/redo, clipboard, locus back/forward,
/// find/replace, recent files, scratch. Anchors on the surface — not path archaeology.
/// </summary>
internal static partial class EditorComfort
{
    public const string Schema = "editor_comfort/v0";
    public const int MaxUndo = 64;
    public const int MaxFindHits = 80;
    public const int MaxRecent = 24;
    public const int MaxHistoryCards = 20;

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    static readonly object Gate = new();
    static readonly Dictionary<string, EditStack> Stacks = new(StringComparer.OrdinalIgnoreCase);
    static readonly List<string> RecentPaths = [];
    static readonly List<string> NavBack = [];
    static readonly List<string> NavForward = [];
    static string? NavCurrent;
    static int ScratchSeq;

    sealed class EditStack
    {
        public readonly Stack<(string Text, string Label)> Undo = new();
        public readonly Stack<(string Text, string Label)> Redo = new();
    }

    public static bool IsComfortOp(string op) => op is
        "undo" or "redo" or "history"
        or "copy" or "paste" or "cut" or "put" or "take"
        or "clipboard" or "clip" or "clipboard_clear" or "clip_clear"
        or "find" or "find_all" or "replace_all"
        or "back" or "forward" or "nav" or "nav_status"
        or "recent_files" or "scratch";

    public static string Dispatch(
        DocumentBufferStore store,
        SessionContext session,
        string op,
        IReadOnlyDictionary<string, JsonElement> args) =>
        op switch
        {
            "undo" => Undo(store, session, args),
            "redo" => Redo(store, session, args),
            "history" => History(store, session, args),
            "copy" => Copy(store, session, args),
            "cut" => Cut(store, session, args),
            "paste" => Paste(store, session, args),
            "put" => Put(store, session, args),
            "take" => throw new InvalidOperationException(
                "take is async — use DocumentEditPlane / bare take (verify then ship)."),
            "clipboard" or "clip" => ClipboardCard(args),
            "clipboard_clear" or "clip_clear" => ClipboardClear(),
            "find" or "find_all" => Find(store, session, args, all: op == "find_all" || BoolOr(args, "all", false)),
            "replace_all" => ReplaceAll(store, session, args),
            "back" => NavStep(store, session, forward: false),
            "forward" => NavStep(store, session, forward: true),
            "nav" or "nav_status" => NavStatus(),
            "recent_files" => RecentFilesCard(session),
            "scratch" => Scratch(store, session, args),
            _ => JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                error = "unknown_comfort_op",
                op
            }, Pretty)
        };

    /// <summary>Call after a successful buffer mutate (pre-text → post-text).</summary>
    public static void RecordEdit(string absolutePath, string beforeText, string afterText, string label)
    {
        if (string.Equals(beforeText, afterText, StringComparison.Ordinal))
            return;
        lock (Gate)
        {
            var stack = StackFor(absolutePath);
            stack.Undo.Push((beforeText, label));
            if (stack.Undo.Count > MaxUndo)
            {
                var keep = stack.Undo.Take(MaxUndo).Reverse().ToArray();
                stack.Undo.Clear();
                foreach (var x in keep)
                    stack.Undo.Push(x);
            }

            stack.Redo.Clear();
        }

        RememberFile(absolutePath);
    }

    public static void ClearStack(string absolutePath)
    {
        lock (Gate)
            Stacks.Remove(absolutePath);
    }

    public static void RememberFile(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
            return;
        var full = Path.GetFullPath(absolutePath);
        lock (Gate)
        {
            RecentPaths.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
            RecentPaths.Insert(0, full);
            while (RecentPaths.Count > MaxRecent)
                RecentPaths.RemoveAt(RecentPaths.Count - 1);
        }
    }

    /// <summary>MRU file paths (absolute) for desk bookmark when buffers are empty.</summary>
    public static IReadOnlyList<string> RecentFilePaths()
    {
        lock (Gate)
            return RecentPaths.ToList();
    }

    public static void PushLocus(SessionContext session, string? wireOrPath)
    {
        if (string.IsNullOrWhiteSpace(wireOrPath))
            return;
        var wire = NormalizeWireOrFile(session, wireOrPath!);
        lock (Gate)
        {
            if (string.Equals(NavCurrent, wire, StringComparison.Ordinal))
                return;
            if (NavCurrent is { Length: > 0 })
                NavBack.Add(NavCurrent);
            while (NavBack.Count > 64)
                NavBack.RemoveAt(0);
            NavCurrent = wire;
            NavForward.Clear();
        }
    }

    public static object Snap()
    {
        lock (Gate)
        {
            return new
            {
                undo_buffers = Stacks.Count(kv => kv.Value.Undo.Count > 0),
                clipboard = SessionClipboard.Summary(),
                nav_back = NavBack.Count,
                nav_forward = NavForward.Count,
                nav_current = NavCurrent,
                recent_files = RecentPaths.Count
            };
        }
    }

    public static bool AnyUndo()
    {
        lock (Gate)
            return Stacks.Values.Any(s => s.Undo.Count > 0);
    }

    public static bool AnyNavBack()
    {
        lock (Gate)
            return NavBack.Count > 0;
    }

    public static bool AnyClipboard() => SessionClipboard.Any();

    /// <summary>Desk locus pulse when clipboard holds frames.</summary>
    public static (int Count, string? CurrentId, int Chars, string? From, string Preview)? ClipboardLocusDetail() =>
        SessionClipboard.LocusPulse();

}
