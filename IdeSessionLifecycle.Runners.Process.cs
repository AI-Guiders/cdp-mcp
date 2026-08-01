using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Process runner + lifecycle JSON helpers (≤ADX soft-warn peel).</summary>
internal static partial class IdeSessionLifecycle
{
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
