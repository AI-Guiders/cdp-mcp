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

    private static async Task<string> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> argList,
        string cwd,
        string kind,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken ct,
        object? extra = null)
    {
        var timeoutSec = 120;
        if (args.TryGetValue("timeout_seconds", out var tEl) && tEl.TryGetInt32(out var t) && t > 0)
            timeoutSec = t;

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in argList)
            psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        try
        {
            if (!proc.Start())
                return Fail(kind, $"failed to start {fileName}", cwd);
        }
        catch (Exception ex)
        {
            return Fail(kind, $"failed to start {fileName}: {ex.Message}", cwd);
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        using var reg = ct.Register(() =>
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); }
            catch { /* ignore */ }
        });

        var finished = await Task.Run(() => proc.WaitForExit(timeoutSec * 1000), ct).ConfigureAwait(false);
        if (!finished)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
            return JsonSerializer.Serialize(new
            {
                ok = false,
                kind,
                error = $"timed out after {timeoutSec}s",
                cwd,
                command = fileName,
                args = argList,
                stdout = Trim(stdout.ToString()),
                stderr = Trim(stderr.ToString()),
                extra
            });
        }

        var code = proc.ExitCode;
        return JsonSerializer.Serialize(new
        {
            ok = code == 0,
            kind,
            exit_code = code,
            cwd,
            command = fileName,
            args = argList,
            stdout = Trim(stdout.ToString()),
            stderr = Trim(stderr.ToString()),
            extra
        });
    }

    private static bool LooksLifecycleOk(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var ok))
                return ok.ValueKind == JsonValueKind.True;
            if (root.TryGetProperty("success", out var success))
                return success.ValueKind == JsonValueKind.True;
            if (root.TryGetProperty("exit_code", out var code) && code.TryGetInt32(out var n))
                return n == 0;
            // build_structured pulse often has error_count
            if (root.TryGetProperty("error_count", out var ec) && ec.TryGetInt32(out var errors))
                return errors == 0;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string Fail(string kind, string error, string? path) =>
        JsonSerializer.Serialize(new { ok = false, kind, error, path });

    private static string Trim(string s)
    {
        if (s.Length <= 8000)
            return s;
        return s[..8000] + "\n…(truncated)";
    }
}
