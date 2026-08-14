#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Explicit Explore escape when locus has no useful ADR — still stamps latch with why=.</summary>
internal static class ExploreCorrNoAdr
{
    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static string Run(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var why = Opt(args, "why") ?? Opt(args, "reason");
        if (string.IsNullOrWhiteSpace(why))
        {
            return JsonSerializer.Serialize(new
            {
                schema = ExploreCorrLatch.Schema,
                ok = false,
                feature = "no_adr",
                error = "why_required",
                hint = "feature=no_adr why=short-reason path=locus — empty why = theatre"
            }, Pretty);
        }

        var pathArg = Opt(args, "path") ?? Opt(args, "file");
        string? abs = null;
        if (pathArg is { Length: > 0 })
        {
            abs = Path.IsPathRooted(pathArg)
                ? Path.GetFullPath(pathArg)
                : Path.GetFullPath(Path.Combine(
                    session.ScmRoot ?? session.ProjectRoot ?? Directory.GetCurrentDirectory(),
                    pathArg));
        }
        else if (store.All.FirstOrDefault() is { Path.Length: > 0 } doc)
            abs = doc.Path;

        if (abs is null)
        {
            return JsonSerializer.Serialize(new
            {
                schema = ExploreCorrLatch.Schema,
                ok = false,
                feature = "no_adr",
                error = "path_required",
                hint = "path= file under project, or open a buffer"
            }, Pretty);
        }

        var rootHint = session.ScmRoot ?? session.ProjectRoot;
        var ws = ExploreCorrLatch.FindWorkspaceRoot(abs, rootHint)
                 ?? rootHint
                 ?? Path.GetDirectoryName(abs)
                 ?? Directory.GetCurrentDirectory();
        var rel = Rel(ws, abs);

        try
        {
            ExploreCorrLatch.StampNoAdr(ws, rel, why);
        }
        catch (ArgumentException ex)
        {
            return JsonSerializer.Serialize(new
            {
                schema = ExploreCorrLatch.Schema,
                ok = false,
                feature = "no_adr",
                error = "stamp_failed",
                message = ex.Message
            }, Pretty);
        }

        return JsonSerializer.Serialize(new
        {
            schema = ExploreCorrLatch.Schema,
            ok = true,
            feature = "no_adr",
            file = rel,
            workspace_root = ws,
            why = why.Trim(),
            explore_corr = ExploreCorrLatch.Pulse(ws),
            hint = "Latch stamped — Act/mutate green for this locus (TTL). Prefer real corr when ADRs exist."
        }, Pretty);
    }

    static string Rel(string workspaceRoot, string abs)
    {
        var root = Path.GetFullPath(workspaceRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var full = Path.GetFullPath(abs);
        if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && full.Length > root.Length)
            return full[(root.Length + 1)..].Replace('\\', '/');
        return Path.GetFileName(full);
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;
}
