using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>IDE-style Build / Run / Test — language-agnostic meta; projections by session language.</summary>
internal static partial class IdeSessionLifecycle
{
    public static string ResolveLanguage(SessionContext session)
    {
        if (!string.IsNullOrWhiteSpace(session.Language)
            && !CdpLanguages.IsAny(session.Language))
            return session.Language!;
        if (!string.IsNullOrWhiteSpace(session.TsConfigPath))
            return CdpLanguages.Typescript;
        if (!string.IsNullOrWhiteSpace(session.SolutionOrProjectPath))
            return CdpLanguages.Csharp;
        return CdpLanguages.Any;
    }

    public static bool TryResolveTarget(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        out string target,
        out string error)
    {
        target = "";
        error = "";
        if (args.TryGetValue("path", out var p) && p.GetString() is { Length: > 0 } path)
        {
            target = Path.GetFullPath(path);
            return true;
        }

        if (args.TryGetValue("solution_path", out var s) && s.GetString() is { Length: > 0 } sol)
        {
            target = Path.GetFullPath(sol);
            return true;
        }

        var lang = ResolveLanguage(session);
        if (lang.Equals(CdpLanguages.Typescript, StringComparison.OrdinalIgnoreCase))
        {
            if (session.TsConfigPath is { Length: > 0 } ts)
            {
                target = Path.GetFullPath(ts);
                return true;
            }

            if (session.ProjectRoot is { Length: > 0 } root)
            {
                var candidate = Path.Combine(root, "tsconfig.json");
                if (File.Exists(candidate))
                {
                    target = Path.GetFullPath(candidate);
                    return true;
                }
            }
        }

        if (session.SolutionOrProjectPath is { Length: > 0 } sess)
        {
            target = Path.GetFullPath(sess);
            return true;
        }

        if (session.ProjectRoot is { Length: > 0 } pr)
        {
            target = Path.GetFullPath(pr);
            return true;
        }

        error = "No project open. Call cdp_open(path) first, or pass path=/solution_path=.";
        return false;
    }

    public static Dictionary<string, JsonElement> WithSolution(
        IReadOnlyDictionary<string, JsonElement> args,
        string solutionPath)
    {
        var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var kv in args)
        {
            if (kv.Key is "path" or "solution_path")
                continue;
            dict[kv.Key] = kv.Value.Clone();
        }

        dict["solution_path"] = JsonSerializer.SerializeToElement(solutionPath);
        return dict;
    }

    public static async Task<string> BuildAsync(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        ICdpBackendModule? buildMod,
        CancellationToken ct)
    {
        if (!TryResolveTarget(session, args, out var target, out var err))
            throw new ArgumentException(err);

        var lang = ResolveLanguage(session);
        string result;
        try
        {
            if (LooksTypescript(target, lang))
                result = await TypescriptBuildAsync(target, session.ProjectRoot, args, ct).ConfigureAwait(false);
            else if (LooksCsharp(target, lang))
            {
                if (buildMod is null)
                    throw new InvalidOperationException("build backend not mounted.");
                var buildArgs = WithSolution(args, target);
                result = await buildMod.CallAsync("build_structured", buildArgs).ConfigureAwait(false);
            }
            else
                result = Fail("cdp.build", $"No build projection for language '{lang}'. Open csharp/ts project via cdp_open.", target);
        }
        catch (Exception ex)
        {
            IdeIgniteArmHost.Notify("build_finished", ok: false, pulse: "exception", detail: ex.Message);
            throw;
        }

        var ok = LooksLifecycleOk(result);
        IdeIgniteArmHost.Notify("build_finished", ok, pulse: ok ? "ok" : "fail", detail: target);
        return result;
    }

    public static async Task<string> TestAsync(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        ICdpBackendModule? buildMod,
        CancellationToken ct)
    {
        if (!TryResolveTarget(session, args, out var target, out var err))
            throw new ArgumentException(err);

        var lang = ResolveLanguage(session);
        string result;
        try
        {
            if (LooksTypescript(target, lang))
                result = await TypescriptNpmScriptAsync("test", "cdp.test", target, session.ProjectRoot, args, ct)
                    .ConfigureAwait(false);
            else if (LooksCsharp(target, lang))
            {
                if (buildMod is null)
                    throw new InvalidOperationException("build backend not mounted.");
                var testArgs = WithSolution(args, target);
                result = await buildMod.CallAsync("run_tests", testArgs).ConfigureAwait(false);
            }
            else
                result = Fail("cdp.test", $"No test projection for language '{lang}'.", target);
        }
        catch (Exception ex)
        {
            IdeIgniteArmHost.Notify("test_finished", ok: false, pulse: "exception", detail: ex.Message);
            throw;
        }

        var ok = LooksLifecycleOk(result);
        IdeIgniteArmHost.Notify("test_finished", ok, pulse: ok ? "ok" : "fail", detail: target);
        return result;
    }

    public static async Task<string> TestSceneAsync(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        ICdpBackendModule? buildMod,
        CancellationToken ct)
    {
        if (!TryResolveTarget(session, args, out var target, out var err))
            throw new ArgumentException(err);

        var lang = ResolveLanguage(session);
        if (!LooksCsharp(target, lang))
            return Fail("cdp.test_scene", "csharp only in v0 (dotnet test --list-tests).", target);

        if (buildMod is null)
            throw new InvalidOperationException("build backend not mounted.");
        return await buildMod.CallAsync("test_scene", WithSolution(args, target)).ConfigureAwait(false);
    }

    public static async Task<string> TestPlanAsync(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        ICdpBackendModule? buildMod,
        CancellationToken ct)
    {
        if (!TryResolveTarget(session, args, out var target, out var err))
            throw new ArgumentException(err);

        var lang = ResolveLanguage(session);
        if (!LooksCsharp(target, lang))
            return Fail("cdp.test_plan", "csharp only in v0.", target);

        if (buildMod is null)
            throw new InvalidOperationException("build backend not mounted.");
        return await buildMod.CallAsync("test_plan", WithSolution(args, target)).ConfigureAwait(false);
    }

    public static async Task<string> RunAsync(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken ct)
    {
        if (!TryResolveTarget(session, args, out var target, out var err))
            throw new ArgumentException(err);

        var lang = ResolveLanguage(session);
        if (LooksTypescript(target, lang))
            return await TypescriptRunAsync(target, session.ProjectRoot, args, ct).ConfigureAwait(false);

        if (LooksCsharp(target, lang))
            return await DotnetRunAsync(target, args, ct).ConfigureAwait(false);

        return Fail("cdp.run", $"No run projection for language '{lang}'.", target);
    }
}
