#nullable enable
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CdpMcp;

internal static partial class CdpPluginQuarantine
{
    /// <summary>Probe PATH for host tools required by primary payload (e.g. java for jar).</summary>
    internal static IReadOnlyList<HostDep> ProbeHostDeps(ModeAPayload? payload)
    {
        if (payload is null)
            return [];
        if (string.Equals(payload.Kind, "jar", StringComparison.OrdinalIgnoreCase))
            return [ProbeOnPath("java")];
        return [];
    }

    internal static object ProbeHostDepsCard(ModeAPayload? payload)
    {
        var deps = ProbeHostDeps(payload);
        return new
        {
            host_deps = deps.Select(d => new { name = d.Name, ok = d.Ok, path = d.ResolvedPath }).ToArray(),
            host_ok = deps.Count == 0 || deps.All(d => d.Ok)
        };
    }

    public static object HostProbeCard(PluginInfo? plugin)
    {
        if (plugin?.PayloadPath is not { Length: > 0 })
            return ProbeHostDepsCard(null);
        var kind = plugin.PayloadKind ?? GuessKind(plugin.PayloadPath);
        return ProbeHostDepsCard(new ModeAPayload(kind, plugin.PayloadPath, plugin.PayloadPath));
    }


    static HostDep ProbeOnPath(string command)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "where.exe" : "which",
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null)
                return new HostDep(command, false, null);
            var stdout = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(4000))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return new HostDep(command, false, null);
            }

            var first = stdout
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            return new HostDep(command, first is { Length: > 0 }, first);
        }
        catch
        {
            return new HostDep(command, false, null);
        }
    }

    static string FormatHostHint(IReadOnlyList<HostDep> host)
    {
        if (host.Count == 0)
            return "";
        var missing = host.Where(h => !h.Ok).Select(h => h.Name).ToArray();
        if (missing.Length == 0)
            return " · host ok (" + string.Join(",", host.Select(h => h.Name)) + ")";
        return " · host missing: " + string.Join(",", missing);
    }

    static string[] ListContributesKeys(JsonElement pkg)
    {
        if (!pkg.TryGetProperty("contributes", out var c) || c.ValueKind != JsonValueKind.Object)
            return [];
        return c.EnumerateObject().Select(p => p.Name).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    static (bool Hit, string Why) DetectLspSignals(JsonElement pkg, string extensionDir)
    {
        try
        {
            var raw = pkg.GetRawText();
            if (raw.Contains("vscode-languageclient", StringComparison.OrdinalIgnoreCase)
                || raw.Contains("LanguageClient", StringComparison.Ordinal))
                return (true, "package.json LanguageClient");
        }
        catch { /* ignore */ }

        foreach (var file in EnumeratePayloadCandidateFiles(extensionDir))
        {
            var name = Path.GetFileName(file);
            if (name.Contains("language-server", StringComparison.OrdinalIgnoreCase)
                || name.Contains("languageserver", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".lsp", StringComparison.OrdinalIgnoreCase)
                || name.Equals("server.js", StringComparison.OrdinalIgnoreCase)
                   && (Path.GetFileName(Path.GetDirectoryName(file) ?? "").Equals("server", StringComparison.OrdinalIgnoreCase)
                       || Path.GetFileName(Path.GetDirectoryName(file) ?? "").Equals("lsp", StringComparison.OrdinalIgnoreCase)))
                return (true, "tree:" + name);
        }

        return (false, "");
    }

    static object? BuildRuntimeNode(ModeAPayload? payload, IReadOnlyList<HostDep>? host = null)
    {
        if (payload is null)
            return null;

        var path = payload.RelPath.Replace('\\', '/');
        var hostDeps = (host ?? ProbeHostDeps(payload))
            .Select(d => new { name = d.Name, ok = d.Ok, path = d.ResolvedPath })
            .ToArray();

        return payload.Kind switch
        {
            "jar" => new
            {
                kind = "jar",
                exe = "java",
                jar = path,
                path,
                formats = new[] { "png", "svg" },
                host_deps = hostDeps
            },
            "exe" => new { kind = "exe", path, exe = path, host_deps = hostDeps },
            "wasm" => new { kind = "wasm", path, host_deps = hostDeps },
            _ => new { kind = payload.Kind, path, exe = path, host_deps = hostDeps }
        };
    }

}
