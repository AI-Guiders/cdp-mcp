#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent git — sync CallAsync on git soft organ (e2e observe+ship); place git_scene.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake backend JSON; live uses <see cref="ByDomainResolver"/>.</summary>
    internal static Func<string, IReadOnlyDictionary<string, JsonElement>, Task<string>>? GitCallOverride { get; set; }

    static Applied RunGit(CitizenIntentRouter.Route route)
    {
        var shortTool = string.IsNullOrWhiteSpace(route.Op) ? "scene" : route.Op!;
        var tool = CdpDomains.ExpandUnderlying(CdpDomains.Git, shortTool);
        var args = BuildGitArgs(route.Raw);

        try
        {
            var json = CallGitBackend(tool, args);
            if (json is null)
            {
                return new Applied(
                    route.Raw,
                    route.Verb.ToString(),
                    Ok: false,
                    Action: "git",
                    Go: "git",
                    Reason: "git_disabled");
            }

            if (json.StartsWith("git_", StringComparison.Ordinal))
            {
                return new Applied(
                    route.Raw,
                    route.Verb.ToString(),
                    Ok: false,
                    Action: "git",
                    Go: "git",
                    Reason: json);
            }

            var ok = TryReadGitOk(json);
            if (ok && string.Equals(shortTool, "commit", StringComparison.OrdinalIgnoreCase))
                IdeDomainStampPending.Mark("citizen_git_commit");
            var pulse = TryReadGitPulse(json, shortTool);
            var seat = IdeDeskSeats.PlaceOrgan("git");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "git",
                Seat: seat,
                Go: "git",
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "git_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "git",
                Go: "git",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>Returns JSON body, null when disabled, or reason token when workspace missing.</summary>
    static string? CallGitBackend(string tool, Dictionary<string, JsonElement> args)
    {
        if (GitCallOverride is { } ov)
            return ov(tool, args).ConfigureAwait(false).GetAwaiter().GetResult();

        var session = SessionResolver?.Invoke();
        if (session is not null)
            args = new Dictionary<string, JsonElement>(
                GitSessionDefaults.WithWorkspace(args, session),
                StringComparer.Ordinal);

        var byDomain = ByDomainResolver?.Invoke()
            ?? new Dictionary<string, ICdpBackendModule>(StringComparer.Ordinal);
        if (!byDomain.TryGetValue(CdpDomains.Git, out var backend) || !backend.IsEnabled)
            return null;

        if (!args.ContainsKey("workspace_path") && !GitSessionDefaults.HasSlices(args))
            return "git_workspace_required";

        return backend.CallAsync(tool, args).ConfigureAwait(false).GetAwaiter().GetResult();
    }

    static Dictionary<string, JsonElement> BuildGitArgs(string raw)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var key in GitArgKeys)
        {
            var val = ExtractMcpKeyed(raw, key);
            if (val is { Length: > 0 })
                args[key] = JsonSerializer.SerializeToElement(val);
        }

        if (ExtractMcpKeyed(raw, "staged") is { Length: > 0 } stagedRaw
            && bool.TryParse(stagedRaw, out var staged))
            args["staged"] = JsonSerializer.SerializeToElement(staged);

        if (ExtractMcpKeyed(raw, "include_submodules") is { Length: > 0 } subRaw
            && bool.TryParse(subRaw, out var includeSubs))
            args["include_submodules"] = JsonSerializer.SerializeToElement(includeSubs);

        if (ExtractMcpKeyed(raw, "n") is { Length: > 0 } nRaw
            && int.TryParse(nRaw, out var n))
            args["n"] = JsonSerializer.SerializeToElement(n);

        if (ExtractMcpKeyed(raw, "max_roots") is { Length: > 0 } mrRaw
            && int.TryParse(mrRaw, out var maxRoots))
            args["max_roots"] = JsonSerializer.SerializeToElement(maxRoots);

        if (TryBuildGitPaths(raw, out var pathsEl))
            args["paths"] = pathsEl;

        return args;
    }

    static bool TryBuildGitPaths(string raw, out JsonElement pathsEl)
    {
        pathsEl = default;
        var list = new List<string>();
        if (ExtractMcpKeyed(raw, "paths") is { Length: > 0 } pathsRaw)
        {
            var trimmed = pathsRaw.Trim();
            if (trimmed.StartsWith('['))
            {
                try
                {
                    using var doc = JsonDocument.Parse(trimmed);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var e in doc.RootElement.EnumerateArray())
                        {
                            if (e.ValueKind == JsonValueKind.String
                                && e.GetString() is { Length: > 0 } s)
                                list.Add(s);
                        }
                    }
                }
                catch
                {
                    /* fall through to CSV */
                }
            }

            if (list.Count == 0)
            {
                foreach (var part in trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (part.Length > 0)
                        list.Add(part.Trim('"'));
                }
            }
        }

        if (list.Count == 0 && ExtractMcpKeyed(raw, "path") is { Length: > 0 } one)
            list.Add(one);

        if (list.Count == 0)
            return false;

        pathsEl = JsonSerializer.SerializeToElement(list);
        return true;
    }

    static readonly string[] GitArgKeys =
    [
        "workspace_path", "path", "rev", "remote", "branch", "message"
    ];

    static string? TryReadGitPulse(string json, string tool)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("roots", out var roots) && roots.ValueKind == JsonValueKind.Array)
            {
                var line = IdeCockpitGitPulse.FromScene(root);
                return TruncPulse("git " + tool + " " + line);
            }

            var bits = new List<string> { "git", tool };
            if (root.TryGetProperty("ok", out var okEl))
                bits.Add(okEl.ValueKind == JsonValueKind.True ? "ok" : "fail");
            if (root.TryGetProperty("stdout", out var so) && so.ValueKind == JsonValueKind.String
                && so.GetString() is { Length: > 0 } stdout)
                bits.Add(TruncPulse(stdout.Replace('\r', ' ').Replace('\n', ' ')) ?? "out");
            else if (root.TryGetProperty("branch", out var b) && b.ValueKind == JsonValueKind.String
                && b.GetString() is { Length: > 0 } branch)
                bits.Add(branch);
            return TruncPulse(string.Join(' ', bits));
        }
        catch
        {
            return TruncPulse("git " + tool + " " + FirstPulseLine(json));
        }
    }

    static bool TryReadGitOk(string json)
    {
        var trimmed = json.TrimStart();
        // git CLI stdout often starts with "[branch hash] msg" — not JSON arrays.
        if (trimmed.StartsWith('{'))
            return TryReadLifecycleOk(json);
        return trimmed.Length > 0;
    }

    static string FirstPulseLine(string text)
    {
        var line = text.Replace('\r', '\n');
        var sp = line.IndexOf('\n');
        if (sp >= 0)
            line = line[..sp];
        return line.Trim();
    }
}

/// <summary>Pulse helper for citizen git_scene — mirrors IdeCockpit.GitPulseLine without partial coupling.</summary>
file static class IdeCockpitGitPulse
{
    public static string FromScene(JsonElement root)
    {
        var branch = FirstBranch(root) ?? "?";
        if (MaterialDirty(root))
            return "dirty (" + branch + ")";
        var noise = UntrackedCount(root);
        return noise > 0 ? "noise×" + noise + " (" + branch + ")" : "clean (" + branch + ")";
    }

    static string? FirstBranch(JsonElement root)
    {
        if (!root.TryGetProperty("roots", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var r in arr.EnumerateArray())
        {
            if (r.TryGetProperty("branch", out var b) && b.ValueKind == JsonValueKind.String
                && b.GetString() is { Length: > 0 } branch)
                return branch;
        }

        return null;
    }

    static bool MaterialDirty(JsonElement root)
    {
        if (!root.TryGetProperty("roots", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return false;
        foreach (var r in arr.EnumerateArray())
        {
            if (r.TryGetProperty("counts", out var counts) && counts.ValueKind == JsonValueKind.Object)
            {
                var staged = PropInt(counts, "staged");
                var unstaged = PropInt(counts, "unstaged");
                if (staged > 0 || unstaged > 0)
                    return true;
                continue;
            }

            if (r.TryGetProperty("dirty", out var d) && d.ValueKind == JsonValueKind.True)
                return true;
        }

        return false;
    }

    static int UntrackedCount(JsonElement root)
    {
        if (!root.TryGetProperty("roots", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return 0;
        var n = 0;
        foreach (var r in arr.EnumerateArray())
        {
            if (r.TryGetProperty("counts", out var counts) && counts.ValueKind == JsonValueKind.Object)
                n += PropInt(counts, "untracked");
        }

        return n;
    }

    static int PropInt(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var el) && el.TryGetInt32(out var n) ? n : 0;
}
