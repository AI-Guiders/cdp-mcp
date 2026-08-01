using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>Nav stack + MRU + scratch for EditorComfort FindNav (≤ADX soft-warn peel).</summary>
internal static partial class EditorComfort
{
    static string NavStep(DocumentBufferStore store, SessionContext session, bool forward)
    {
        string? target;
        lock (Gate)
        {
            if (forward)
            {
                if (NavForward.Count == 0)
                {
                    return JsonSerializer.Serialize(new
                    {
                        schema = Schema,
                        ok = false,
                        op = "forward",
                        error = "nav_empty"
                    }, Pretty);
                }

                if (NavCurrent is { Length: > 0 })
                    NavBack.Add(NavCurrent);
                target = NavForward[^1];
                NavForward.RemoveAt(NavForward.Count - 1);
                NavCurrent = target;
            }
            else
            {
                if (NavBack.Count == 0)
                {
                    return JsonSerializer.Serialize(new
                    {
                        schema = Schema,
                        ok = false,
                        op = "back",
                        error = "nav_empty"
                    }, Pretty);
                }

                if (NavCurrent is { Length: > 0 })
                    NavForward.Add(NavCurrent);
                target = NavBack[^1];
                NavBack.RemoveAt(NavBack.Count - 1);
                NavCurrent = target;
            }
        }

        // Best-effort: open file from F: if present.
        try
        {
            var span = BracketLocate.Parse(target);
            if (span.File is { Length: > 0 })
            {
                var path = ResolveUserPath(session, span.File);
                if (File.Exists(path))
                {
                    store.Open(path);
                    RememberFile(path);
                }
            }
        }
        catch
        {
            // wire may be bare path
            try
            {
                var path = ResolveUserPath(session, target.Trim('[', ']'));
                if (File.Exists(path))
                    store.Open(path);
            }
            catch
            {
                // ignore
            }
        }

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = forward ? "forward" : "back",
            locus = target,
            nav = NavPulse(),
            next = new object[]
            {
                new { go = "peek", label = "Peek locus", why = $"go_args.wire={target}" },
                new { go = forward ? "back" : "forward", label = forward ? "Back" : "Forward", why = "nav stack" }
            },
            hint = "Locus stack (VS Navigate Backward/Forward analogue)."
        }, Pretty);
    }

    static string NavStatus() =>
        JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "nav",
            nav = NavPulse(),
            hint = "go=back / go=forward"
        }, Pretty);

    static string RecentFilesCard(SessionContext session)
    {
        List<string> paths;
        lock (Gate)
            paths = RecentPaths.ToList();

        var files = paths.Select(p => new
        {
            anchor = WireFile(session, p),
            name = Path.GetFileName(p)
        }).ToArray();

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "recent_files",
            count = files.Length,
            files,
            next = files.Length > 0
                ? new object[] { new { go = "buffer_scene", label = "Open from MRU", why = "cdp_buffer op=open via F:" } }
                : Array.Empty<object>(),
            hint = "MRU of edited/opened files this MCP session."
        }, Pretty);
    }

    static string Scratch(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var root = session.ProjectRoot is { Length: > 0 } pr
            ? Path.Combine(pr, ".cdp", "scratch")
            : Path.Combine(Path.GetTempPath(), "cdp-scratch");
        Directory.CreateDirectory(root);
        int n;
        lock (Gate)
            n = ++ScratchSeq;
        var ext = OptString(args, "ext") ?? "cs";
        if (!ext.StartsWith('.'))
            ext = "." + ext;
        var path = Path.Combine(root, $"untitled-{n}{ext}");
        var text = OptString(args, "text") ?? "// scratch\n";
        var buf = store.Create(path, text, overwrite: true);
        RememberFile(path);
        var wire = WireFile(session, path);
        PushLocus(session, wire);
        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "scratch",
            anchor = wire,
            meta = buf.ToMeta(),
            next = new object[]
            {
                new { go = "edit_draft", label = "Edit scratch", why = "untitled buffer ready" }
            },
            hint = "Untitled under .cdp/scratch (or temp). Not OS temp forever when project open."
        }, Pretty);
    }
}
