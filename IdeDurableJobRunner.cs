#nullable enable
using System.Text.Json;
using Cdp.Core;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>Out-of-process worker: <c>CdpMcp --durable-job &lt;id&gt;</c> (ADR-0032).</summary>
internal static class IdeDurableJobRunner
{
    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<int> RunAsync(string jobId)
    {
        if (!DurableJobStore.TryReadRecordPublic(jobId, out var record) || record.Lifecycle is null)
        {
            Console.Error.WriteLine($"[durable-job] not found or not lifecycle: {jobId}");
            return 2;
        }

        ApplyIgniteSeat(record.Lifecycle);

        var session = RestoreSession(record.Lifecycle);
        var args = DeserializeArgs(record.Lifecycle.ArgsJson);
        string result;
        var ok = false;

        try
        {
            result = record.Kind switch
            {
                "build" => await IdeSessionLifecycle.BuildAsync(session, args, BuildModule(), CancellationToken.None)
                    .ConfigureAwait(false),
                "test" => await IdeSessionLifecycle.TestAsync(session, args, BuildModule(), CancellationToken.None)
                    .ConfigureAwait(false),
                "deploy" => IdeDeploy.Run(session, args),
                _ => JsonSerializer.Serialize(new { ok = false, error = "unknown_kind", kind = record.Kind })
            };
            ok = LooksOk(result);
        }
        catch (Exception ex)
        {
            result = JsonSerializer.Serialize(new { ok = false, error = ex.Message });
            DurableJobStore.Finish(jobId, ok: false, result, ex.Message);
            await NotifyAsync(record, ok: false, ex.Message).ConfigureAwait(false);
            return 1;
        }

        DurableJobStore.Finish(jobId, ok, result, ok ? null : "failed");
        await NotifyAsync(record, ok, ok ? record.Kind : "fail").ConfigureAwait(false);
        return ok ? 0 : 1;
    }

    internal static void ApplyIgniteSeat(DurableLifecyclePayload life)
    {
        var seat = life.IgniteSeat;
        if (string.IsNullOrWhiteSpace(seat) && !string.IsNullOrWhiteSpace(life.WorkerExePath))
            seat = IdeDeploy.ClassifySeat(Path.GetDirectoryName(Path.GetFullPath(life.WorkerExePath)));
        if (!string.IsNullOrWhiteSpace(seat))
            Environment.SetEnvironmentVariable("CDP_IGNITE_SEAT", seat);
    }

    static async Task NotifyAsync(DurableJobRecord record, bool ok, string? detail)
    {
        if (record.Lifecycle is not null)
            ApplyIgniteSeat(record.Lifecycle);

        IdeIgniteArmHost.EnsureStarted();
        IdeIgniteArmHost.Notify(record.IgniteEvent, ok, pulse: record.Kind, detail: detail);

        if (!string.IsNullOrWhiteSpace(record.ArmId))
            await IdeIgniteArmHost.WaitForArmDeliveryAsync(record.ArmId, TimeSpan.FromSeconds(120))
                .ConfigureAwait(false);
        else if (IdeIgniteArmHost.IsEventTriggeredArm(record.IgniteEvent))
            await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
    }

    static ICdpBackendModule? BuildModule()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var candidates = new[]
            {
                Environment.GetEnvironmentVariable("CDP_MCP_CONFIG"),
                Path.Combine(baseDir, "cdp-mcp.toml"),
                Path.Combine(baseDir, "config", "cdp-mcp.toml")
            };
            var configPath = candidates.FirstOrDefault(File.Exists) ?? Path.Combine(baseDir, "cdp-mcp.toml");
            var settings = CdpSettings.Load(configPath);
            return settings.Dev.Build.Enabled ? new CdpMcp.Backends.BuildTestBackend(settings) : null;
        }
        catch
        {
            return null;
        }
    }
    static SessionContext RestoreSession(DurableLifecyclePayload life) => new()
    {
        ProjectRoot = life.ProjectRoot,
        ScmRoot = life.ScmRoot,
        SolutionOrProjectPath = life.SolutionOrProjectPath,
        ProjectKind = life.ProjectKind,
        TsConfigPath = life.TsConfigPath,
        Phase = CdpPhase.Act,
        Object = CdpObjectKind.Code
    };

    static Dictionary<string, JsonElement> DeserializeArgs(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOpts)
                   ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }
    }

    static bool LooksOk(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var ok))
                return ok.ValueKind == JsonValueKind.True;
            if (root.TryGetProperty("success", out var success))
                return success.ValueKind == JsonValueKind.True;
            return true;
        }
        catch
        {
            return false;
        }
    }
}



