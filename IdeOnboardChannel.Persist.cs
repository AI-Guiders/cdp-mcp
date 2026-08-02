#nullable enable
using System.Text.Json;
using System.Text.RegularExpressions;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;
internal static partial class IdeOnboardChannel
{
    static object OkCard(SessionContext session, ScanDoc doc, string op)
    {
        var next = BuildNext(doc);
        return new
        {
            ok = true,
            schema = SchemaVersion,
            go = GoName,
            tool = ToolName,
            op,
            pulse = Pulse(doc),
            detail = "full",
            board_path = LatestPath(session),
            profile_hint = doc.ProfileHint,
            project = doc.ProjectName,
            root = doc.Root,
            docs = new
            {
                has_readme = doc.Docs.HasReadme,
                readme_path = doc.Docs.ReadmePath,
                has_docs_dir = doc.Docs.HasDocsDir,
                adr_count = doc.Docs.AdrCount
            },
            entrypoints = doc.Entrypoints.Select(e => new { label = e.Label, path = e.Path, anchor = e.Anchor, score = e.Score }),
            top_folders = doc.TopFolders.Select(f => new { path = f.Path, file_count = f.FileCount }),
            verticals = doc.Verticals.Select(v => new { name = v.Name, file_count = v.FileCount, sample_path = v.SamplePath, sample_anchor = v.SampleAnchor }),
            solutions = doc.Solutions,
            csproj_count = doc.CsprojCount,
            files_scanned = doc.FilesScanned,
            truncated = doc.Truncated,
            updated_utc = doc.UpdatedUtc,
            next,
            hint = "Cold-start map — not Code Map. Open entrypoint → find_usages → one vertical. op=as_built when profile_hint=cide|cdp_desk."
        };
    }

    static List<object> BuildNext(ScanDoc doc)
    {
        var next = new List<object>();
        if (doc.Entrypoints.Count > 0)
        {
            var e = doc.Entrypoints[0];
            next.Add(new { go = "buffer", label = $"Open {e.Label}", why = $"op=open path={e.Path}" });
            if (e.Anchor is { Length: > 0 })
            {
                next.Add(new { go = "goto", label = $"Land {e.Label}", why = $"anchor={e.Anchor}" });
            }
        }

        if (doc.Verticals.Count > 0)
        {
            var v = doc.Verticals[0];
            next.Add(new { go = "find_desk", label = $"Search in {v.Name}", why = $"query={v.Name} — pick a type, then find_usages" });
        }

        if (doc.ProfileHint is "cide" or "cdp_desk")
        {
            next.Add(new { go = "arch_desk", label = "As-built layers", why = "op=as_built — ontological board for known profile" });
        }

        if (doc.Docs.HasReadme && doc.Docs.ReadmePath is { Length: > 0 } rm)
        {
            next.Add(new { go = "buffer", label = "Open README", why = $"op=open path={rm}" });
        }

        next.Add(new { go = GoName, label = "Rescan", why = "op=scan" });
        next.Add(new { go = "layout", label = "Layout onboard", why = "cmd=\"layout onboard\" — M=onboard_desk" });
        return next;
    }

    static ScanDoc Load(SessionContext session)
    {
        lock (Gate)
            return LoadUnlocked(session);
    }

    static void Save(SessionContext session, ScanDoc doc)
    {
        lock (Gate)
        {
            doc.UpdatedUtc = DateTimeOffset.UtcNow;
            doc.Schema = SchemaVersion;
            var dir = BoardDir(session);
            Directory.CreateDirectory(dir);
            File.WriteAllText(LatestPath(session), JsonSerializer.Serialize(doc, Pretty));
        }

        PublishGlass(session);
    }

    static ScanDoc LoadUnlocked(SessionContext session)
    {
        var path = LatestPath(session);
        if (!File.Exists(path))
            return new ScanDoc();
        try
        {
            return JsonSerializer.Deserialize<ScanDoc>(File.ReadAllText(path), Pretty) ?? new ScanDoc();
        }
        catch
        {
            return new ScanDoc();
        }
    }

    static string LatestPath(SessionContext session) => Path.Combine(BoardDir(session), "LATEST.json");
    static string BoardDir(SessionContext session)
    {
        var root = session.ProjectRoot ?? session.ScmRoot;
        if (root is { Length: > 0 })
            return Path.GetFullPath(Path.Combine(root, ".cdp", "onboard"));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "cdp-mcp", "onboard");
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString()?.Trim(),
            JsonValueKind.Number => el.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    static object Err(string error, string hint) => new
    {
        ok = false,
        schema = SchemaVersion,
        go = GoName,
        tool = ToolName,
        error,
        hint
    };
}