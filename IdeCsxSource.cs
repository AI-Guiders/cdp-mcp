#nullable enable
using System.Text.Json;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>Resolve CSX source from inline <c>code=</c> or a path with dogfood-aware candidates.</summary>
internal static class IdeCsxSource
{
    public static async Task<string> ResolveAsync(
        IReadOnlyDictionary<string, JsonElement> callArgs,
        string? projectRoot,
        string? solutionOrProjectPath,
        CancellationToken cancellationToken = default)
    {
        if (callArgs.TryGetValue("code", out var c) && c.GetString() is { Length: > 0 } code)
            return code;
        if (!callArgs.TryGetValue("path", out var p) || p.GetString() is not { Length: > 0 } path)
            throw new ArgumentException("code or path is required for CSX tools.");

        var candidates = CollectCandidates(callArgs, path, projectRoot, solutionOrProjectPath);
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return await File.ReadAllTextAsync(candidate, cancellationToken).ConfigureAwait(false);
        }

        throw new ArgumentException(
            $"CSX path not found: {path}. Tried: {string.Join(" | ", candidates)}");
    }

    static List<string> CollectCandidates(
        IReadOnlyDictionary<string, JsonElement> callArgs,
        string path,
        string? projectRoot,
        string? solutionOrProjectPath)
    {
        var candidates = new List<string>();
        void Add(string? candidate) => AddCandidate(candidates, candidate);

        Add(path);
        AddDualFolderSpellings(Add, path);

        if (callArgs.TryGetValue("workspace_path", out var wp) && wp.GetString() is { Length: > 0 } root)
        {
            if (!Path.IsPathRooted(path))
                Add(Path.Combine(root, path));
            Add(Path.Combine(root, "_dogfood-w23-live", Path.GetFileName(path)));
        }

        Add(Path.Combine(Environment.CurrentDirectory, path));
        Add(Path.Combine(Environment.CurrentDirectory, "_dogfood-w23-live", Path.GetFileName(path)));
        AddProjectCandidates(Add, path, projectRoot);
        AddSolutionCandidates(Add, path, solutionOrProjectPath);
        return candidates;
    }

    static void AddDualFolderSpellings(Action<string?> add, string path)
    {
        // Dual folder spellings on this machine (space vs compacted).
        if (path.Contains("Personal Cursor Folder", StringComparison.OrdinalIgnoreCase))
            add(path.Replace("Personal Cursor Folder", "PersonalCursorFolder", StringComparison.OrdinalIgnoreCase));
        if (path.Contains("PersonalCursorFolder", StringComparison.OrdinalIgnoreCase)
            && !path.Contains("Personal Cursor Folder", StringComparison.OrdinalIgnoreCase))
            add(path.Replace("PersonalCursorFolder", "Personal Cursor Folder", StringComparison.OrdinalIgnoreCase));
    }

    static void AddProjectCandidates(Action<string?> add, string path, string? projectRoot)
    {
        if (projectRoot is not { Length: > 0 }) return;
        add(Path.Combine(projectRoot, path));
        add(Path.Combine(projectRoot, Path.GetFileName(path)));
        try
        {
            var gitRoot = GitRootResolver.ResolveGitRoot(projectRoot);
            add(Path.Combine(gitRoot, path));
            add(Path.Combine(gitRoot, "_dogfood-w23-live", Path.GetFileName(path)));
            if (!Path.IsPathRooted(path))
                add(Path.Combine(gitRoot, path));
        }
        catch
        {
            // not a git path — skip
        }
    }

    static void AddSolutionCandidates(Action<string?> add, string path, string? solutionOrProjectPath)
    {
        if (solutionOrProjectPath is not { Length: > 0 } sol) return;
        var solDir = Path.GetDirectoryName(sol);
        if (string.IsNullOrEmpty(solDir)) return;
        add(Path.Combine(solDir, Path.GetFileName(path)));
        try
        {
            var gitRoot = GitRootResolver.ResolveGitRoot(solDir);
            add(Path.Combine(gitRoot, "_dogfood-w23-live", Path.GetFileName(path)));
        }
        catch { /* ignore */ }
    }

    static void AddCandidate(List<string> candidates, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return;
        try
        {
            var full = Path.GetFullPath(candidate);
            if (!candidates.Contains(full, StringComparer.OrdinalIgnoreCase))
                candidates.Add(full);
        }
        catch
        {
            // ignore invalid path candidates
        }
    }
}
