using System.Diagnostics;
using System.Text.Json;
using Cdp.Deploy;

namespace CdpMcpBridge;

/// <summary>
/// Bridge-side deploy fallback (ADR-0203): when CdpService HTTP is down, spawn C# worker
/// (<c>--deploy-cli</c>) out-of-process. SSOT remains <see cref="CdpDeployOrchestrator"/>.
/// </summary>
internal static class CdpBridgeDeployRunner
{
    internal static async Task<string> RunViaWorkerAsync(
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        var worker = ResolveWorkerExe();
        if (worker is null)
        {
            return JsonSerializer.Serialize(new
            {
                schema = "deploy/v0",
                ok = false,
                error = "worker_not_found",
                bridge_local = true,
                engine = "cdp.deploy/csharp",
                hint = "CdpService/CdpMcp worker missing under install seats — build cdp-mcp or deploy service first."
            }, JsonOptions);
        }

        var jobId = $"deploy-bridge-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..32];
        var payloadPath = Path.Combine(Path.GetTempPath(), $"{jobId}.json");
        await File.WriteAllTextAsync(payloadPath, JsonSerializer.Serialize(args), cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = worker,
                Arguments = $"--deploy-cli \"{payloadPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi)
                             ?? throw new InvalidOperationException("Failed to start deploy worker.");
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = proc.StandardError.ReadToEndAsync(cancellationToken);
            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stdout = (await stdoutTask.ConfigureAwait(false)).Trim();
            var stderr = await stderrTask.ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(stdout))
                return AnnotateLocal(stdout, jobId, worker, proc.ExitCode);

            return JsonSerializer.Serialize(new
            {
                schema = "deploy/v0",
                ok = false,
                error = "bridge_local_deploy",
                bridge_local = true,
                engine = "cdp.deploy/csharp",
                job_id = jobId,
                worker,
                exit_code = proc.ExitCode,
                stderr_tail = Tail(stderr, 1200),
                hint = "Deploy worker produced no JSON on stdout."
            }, JsonOptions);
        }
        finally
        {
            try { File.Delete(payloadPath); }
            catch { /* best effort */ }
        }
    }

    static string AnnotateLocal(string deployJson, string jobId, string worker, int exitCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(deployJson);
            var writer = new MemoryStream();
            using (var w = new Utf8JsonWriter(writer, new JsonWriterOptions { Indented = true }))
            {
                w.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                    prop.WriteTo(w);

                w.WriteBoolean("bridge_local", true);
                w.WriteString("job_id", jobId);
                w.WriteString("worker", worker);
                w.WriteNumber("exit_code", exitCode);
                w.WriteString(
                    "hint",
                    "Bridge ran C# deploy worker locally because CdpService HTTP was unavailable.");
                w.WriteEndObject();
            }

            return System.Text.Encoding.UTF8.GetString(writer.ToArray());
        }
        catch
        {
            return deployJson;
        }
    }

    internal static string? ResolveWorkerExe()
    {
        foreach (var root in new[]
                 {
                     CdpDeployLayout.Default.ServiceInstall,
                     CdpDeployLayout.Default.BridgeReleaseInstall,
                     CdpDeployLayout.Default.BridgeDebugInstall
                 })
        {
            foreach (var name in new[] { "CdpService.exe", "CdpMcp.exe" })
            {
                var candidate = Path.Combine(root, name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        var built = Path.Combine(AppContext.BaseDirectory, "CdpMcp.exe");
        return File.Exists(built) ? built : null;
    }

    static string Tail(string text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max)
            return text;
        return "…" + text[^max..];
    }

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}
