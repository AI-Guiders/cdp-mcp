#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft-refuse Mutate / buffer edit when locus has mapped ADRs but Explore latch missing.
/// force=true escape. Dig free.
/// </summary>
internal static class ExploreCorrGate
{
    public const string RefuseId = ExploreCorrLatch.RefuseId;

    public static void RefuseMutateIfNeeded(
        string? absPath,
        string? rootHint,
        IReadOnlyDictionary<string, JsonElement>? args,
        string verb = "mutate")
    {
        if (!ExploreCorrLatch.IsEnabled())
            return;
        if (ForceArg(args))
            return;
        if (string.IsNullOrWhiteSpace(absPath))
            return;

        string full;
        try
        {
            full = Path.GetFullPath(absPath);
        }
        catch
        {
            return;
        }

        if (!ExploreCorrLatch.HasMappedAdrs(full, rootHint))
            return;

        var ws = ExploreCorrLatch.FindWorkspaceRoot(full, rootHint);
        if (string.IsNullOrWhiteSpace(ws))
            return;

        var rel = Rel(ws, full);
        if (ExploreCorrLatch.HasSatisfied(ws, rel))
            return;

        throw new ArgumentException(
            $"{verb} refused — {RefuseId}: ADR-mapped locus needs Explore full-a — " +
            "cdp_analysis_scene feature=correspondence path= (or feature=no_adr why=…). " +
            "Half-a Explore = disaster. force=true escape.");
    }

    public static CitizenRouteHost.Applied? TryRefuseRoute(CitizenIntentRouter.Route route)
    {
        if (!ExploreCorrLatch.IsEnabled())
            return null;
        if (HasForceRoute(route))
            return null;
        if (!IsMutateVerb(route))
            return null;
        if (string.IsNullOrWhiteSpace(route.Path))
            return null;

        var session = CitizenRouteHost.SessionResolver?.Invoke();
        var rootHint = session?.ScmRoot ?? session?.ProjectRoot;
        string full;
        try
        {
            full = Path.IsPathRooted(route.Path)
                ? Path.GetFullPath(route.Path)
                : Path.GetFullPath(Path.Combine(rootHint ?? Directory.GetCurrentDirectory(), route.Path));
        }
        catch
        {
            return null;
        }

        if (!ExploreCorrLatch.HasMappedAdrs(full, rootHint))
            return null;

        var ws = ExploreCorrLatch.FindWorkspaceRoot(full, rootHint);
        if (string.IsNullOrWhiteSpace(ws))
            return null;

        var rel = Rel(ws, full);
        if (ExploreCorrLatch.HasSatisfied(ws, rel))
            return null;

        return new CitizenRouteHost.Applied(
            route.Raw,
            route.Verb.ToString(),
            Ok: false,
            Action: "refuse",
            Path: route.Path,
            Go: route.Go,
            Cmd: route.Cmd,
            Pulse: RefuseId + " · corr dig or no_adr before Act",
            Reason: RefuseId +
                    ": ADR-mapped locus — cdp_analysis_scene feature=correspondence path= " +
                    "(or feature=no_adr why=…). force=true escape.");
    }

    static bool IsMutateVerb(CitizenIntentRouter.Route route)
    {
        if (route.Verb == CitizenIntentRouter.Verb.Buffer
            && route.Op is { Length: > 0 } op
            && (op.Equals("read", StringComparison.OrdinalIgnoreCase)
                || op.Equals("scene", StringComparison.OrdinalIgnoreCase)
                || op.Equals("diagnostics", StringComparison.OrdinalIgnoreCase)
                || op.Equals("disk_peek", StringComparison.OrdinalIgnoreCase)
                || op.Equals("open", StringComparison.OrdinalIgnoreCase)))
            return false;

        return route.Verb is CitizenIntentRouter.Verb.Replace
            or CitizenIntentRouter.Verb.ReplaceAll
            or CitizenIntentRouter.Verb.Create
            or CitizenIntentRouter.Verb.Append
            or CitizenIntentRouter.Verb.Delete
            or CitizenIntentRouter.Verb.Edit
            or CitizenIntentRouter.Verb.Put
            or CitizenIntentRouter.Verb.Sniper
            or CitizenIntentRouter.Verb.Buffer;
    }

    static string Rel(string workspaceRoot, string abs)
    {
        var root = Path.GetFullPath(workspaceRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var full = Path.GetFullPath(abs);
        if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            && full.Length > root.Length)
        {
            return full[(root.Length + 1)..].Replace('\\', '/');
        }

        return Path.GetFileName(full);
    }

    static bool ForceArg(IReadOnlyDictionary<string, JsonElement>? args)
    {
        if (args is null)
            return false;
        if (Boolish(args, "force"))
            return true;
        if (args.TryGetValue("go_args", out var ga) && ga.ValueKind == JsonValueKind.Object
            && ga.TryGetProperty("force", out var f))
        {
            if (f.ValueKind == JsonValueKind.True)
                return true;
            if (f.ValueKind == JsonValueKind.String && bool.TryParse(f.GetString(), out var b) && b)
                return true;
        }

        return false;
    }

    static bool Boolish(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return false;
        if (el.ValueKind == JsonValueKind.True)
            return true;
        return el.ValueKind == JsonValueKind.String
               && bool.TryParse(el.GetString(), out var b)
               && b;
    }

    static bool HasForceRoute(CitizenIntentRouter.Route route)
    {
        var f = CitizenIntentRouter.ExtractKeyedValue(route.Raw ?? "", "force");
        if (string.IsNullOrWhiteSpace(f))
            return false;
        if (bool.TryParse(f, out var b))
            return b;
        return f.Equals("1", StringComparison.OrdinalIgnoreCase)
            || f.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}
