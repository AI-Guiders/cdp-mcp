using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>TS/npm + process runners for session Build/Run/Test.</summary>
internal static partial class IdeSessionLifecycle
{
    private static bool LooksTypescript(string target, string lang) =>
        lang.Equals(CdpLanguages.Typescript, StringComparison.OrdinalIgnoreCase)
        || target.EndsWith("tsconfig.json", StringComparison.OrdinalIgnoreCase)
        || target.EndsWith("package.json", StringComparison.OrdinalIgnoreCase);

    private static bool LooksCsharp(string target, string lang) =>
        lang.Equals(CdpLanguages.Csharp, StringComparison.OrdinalIgnoreCase)
        || target.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
        || target.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
        || target.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
        || target.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase);

    private static async Task<string> TypescriptBuildAsync(
        string tsconfigOrRoot,
        string? projectRoot,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken ct)
    {
        var (root, tsconfig) = ResolveTsPaths(tsconfigOrRoot, projectRoot);
        if (TryFindPackageScript(root, "build", out var pkgDir))
        {
            return await RunNpmAsync(pkgDir, ["run", "build"], "cdp.build", args, ct,
                extra: new { projection = "npm_run_build", package_dir = pkgDir, tsconfig }).ConfigureAwait(false);
        }

        if (tsconfig is null || !File.Exists(tsconfig))
            return Fail("cdp.build", "No package.json build script and no tsconfig.json found.", root);

        // npx tsc -p tsconfig
        var npx = ResolveNpmFamily("npx");
        return await RunProcessAsync(npx, ["tsc", "-p", tsconfig, "--pretty", "false"], root, "cdp.build", args, ct,
            extra: new { projection = "tsc", tsconfig }).ConfigureAwait(false);
    }

    private static async Task<string> TypescriptRunAsync(
        string tsconfigOrRoot,
        string? projectRoot,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken ct)
    {
        var (root, _) = ResolveTsPaths(tsconfigOrRoot, projectRoot);
        if (TryFindPackageScript(root, "start", out var pkgStart))
            return await RunNpmAsync(pkgStart, ["start"], "cdp.run", args, ct,
                extra: new { projection = "npm_start", package_dir = pkgStart }).ConfigureAwait(false);
        if (TryFindPackageScript(root, "dev", out var pkgDev))
            return await RunNpmAsync(pkgDev, ["run", "dev"], "cdp.run", args, ct,
                extra: new { projection = "npm_run_dev", package_dir = pkgDev }).ConfigureAwait(false);

        return Fail("cdp.run",
            "No package.json scripts.start|dev. Add a start script or pass a runnable package root.",
            root);
    }

    private static async Task<string> TypescriptNpmScriptAsync(
        string script,
        string kind,
        string tsconfigOrRoot,
        string? projectRoot,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken ct)
    {
        var (root, _) = ResolveTsPaths(tsconfigOrRoot, projectRoot);
        if (!TryFindPackageScript(root, script, out var pkgDir))
            return Fail(kind, $"No package.json scripts.{script} found under project.", root);

        var npmArgs = script == "test"
            ? new List<string> { "test" }
            : new List<string> { "run", script };
        return await RunNpmAsync(pkgDir, npmArgs, kind, args, ct,
            extra: new { projection = $"npm_{script}", package_dir = pkgDir }).ConfigureAwait(false);
    }

    private static (string Root, string? Tsconfig) ResolveTsPaths(string tsconfigOrRoot, string? projectRoot)
    {
        var full = Path.GetFullPath(tsconfigOrRoot);
        if (File.Exists(full) && full.EndsWith("tsconfig.json", StringComparison.OrdinalIgnoreCase))
            return (Path.GetDirectoryName(full)!, full);
        if (Directory.Exists(full))
        {
            var ts = Path.Combine(full, "tsconfig.json");
            return (full, File.Exists(ts) ? ts : null);
        }

        if (projectRoot is { Length: > 0 })
        {
            var root = Path.GetFullPath(projectRoot);
            var ts = Path.Combine(root, "tsconfig.json");
            return (root, File.Exists(ts) ? ts : null);
        }

        return (full, null);
    }

    private static bool TryFindPackageScript(string startDir, string scriptName, out string packageDir)
    {
        packageDir = "";
        var dir = startDir;
        for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            var pkg = Path.Combine(dir, "package.json");
            if (File.Exists(pkg) && PackageHasScript(pkg, scriptName))
            {
                packageDir = dir;
                return true;
            }

            var parent = Path.GetDirectoryName(dir);
            if (parent is null || parent == dir)
                break;
            dir = parent;
        }

        return false;
    }

    private static bool PackageHasScript(string packageJsonPath, string scriptName)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
            if (!doc.RootElement.TryGetProperty("scripts", out var scripts))
                return false;
            return scripts.TryGetProperty(scriptName, out _);
        }
        catch
        {
            return false;
        }
    }

    private static Task<string> RunNpmAsync(
        string cwd,
        IReadOnlyList<string> npmArgs,
        string kind,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken ct,
        object? extra = null)
    {
        var npm = ResolveNpmFamily("npm");
        return RunProcessAsync(npm, npmArgs, cwd, kind, args, ct, extra);
    }

    private static string ResolveNpmFamily(string name)
    {
        // Prefer absolute nodejs shims — bare "npx.cmd" can resolve to a broken
        // cwd/node_modules/npm (seen on Windows fixtures).
        if (OperatingSystem.IsWindows())
        {
            var pf = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "nodejs", name + ".cmd");
            if (File.Exists(pf))
                return pf;
            var pf86 = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "nodejs", name + ".cmd");
            if (File.Exists(pf86))
                return pf86;
            return name + ".cmd";
        }

        return name;
    }

    public static async Task<string> DotnetRunAsync(
        string projectOrSolutionPath,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken ct)
    {
        if (!projectOrSolutionPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            && !projectOrSolutionPath.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase))
        {
            return Fail("cdp.run",
                "csharp run expects a .csproj (session after cdp_open on csproj). For .sln pass path= to a project.",
                projectOrSolutionPath);
        }

        var configuration = args.TryGetValue("configuration", out var cEl) && cEl.GetString() is { Length: > 0 } cfg
            ? cfg
            : null;
        var noBuild = args.TryGetValue("no_build", out var nb) && nb.ValueKind == JsonValueKind.True;

        var argList = new List<string> { "run", "--project", projectOrSolutionPath };
        if (configuration is not null)
        {
            argList.Add("-c");
            argList.Add(configuration);
        }

        if (noBuild)
            argList.Add("--no-build");

        if (args.TryGetValue("additional_arguments", out var aa) && aa.ValueKind == JsonValueKind.Array)
        {
            argList.Add("--");
            foreach (var el in aa.EnumerateArray())
            {
                if (el.GetString() is { Length: > 0 } tok)
                    argList.Add(tok);
            }
        }

        var cwd = Path.GetDirectoryName(projectOrSolutionPath) ?? Environment.CurrentDirectory;
        return await RunProcessAsync("dotnet", argList, cwd, "cdp.run", args, ct,
            extra: new { projection = "dotnet_run", path = projectOrSolutionPath }).ConfigureAwait(false);
    }
}
