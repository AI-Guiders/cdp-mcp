#nullable enable
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Cdp.Config;
using static CdpMcp.IdeIgniteArmHost;

namespace CdpMcp;

/// <summary>
/// L6 Autoi primary gun — Cursor SDK local <c>Agent.create|resume</c> + <c>agent.send</c>.
/// CDT Composer (:9222) is escape only (<see cref="DeliveryPathCdtEscape"/>).
/// Bridge = node script under <c>scripts/cursor-sdk-local-fire.mjs</c>; tests inject <see cref="BridgeOverride"/>.
/// </summary>
internal static class IdeIgniteSdkLocalFire
{
    public const string DeliveryPathSdkLocal = "sdk_local";
    public const string DeliveryPathCdtEscape = "cdt_escape";
    public const string SubmitKindSdkLocal = "sdk_local";
    public const string ChannelSdkLocal = "sdk_local";

    /// <summary>Test hook: replace process bridge.</summary>
    internal static Func<SdkFireRequest, CancellationToken, Task<SdkFireResult>>? BridgeOverride { get; set; }

    /// <summary>Test hook: pretend SDK unavailable → caller falls through to CDT escape.</summary>
    internal static bool ForceUnavailableForTests { get; set; }

    /// <summary>Test hook: pretend API key present.</summary>
    internal static bool? ApiKeyPresentOverride { get; set; }

    public static void ResetTestHooks()
    {
        BridgeOverride = null;
        ForceUnavailableForTests = false;
        ApiKeyPresentOverride = null;
    }

    public static bool IsForceCdt(IgniteArm arm) =>
        arm.ForceCdt
        || string.Equals(arm.CursorFirePolicy, "composer", StringComparison.OrdinalIgnoreCase)
        || string.Equals(arm.CursorFirePolicy, "cdt", StringComparison.OrdinalIgnoreCase);

    public static bool IsSdkConfigured()
    {
        if (BridgeOverride is not null)
            return true;
        if (!CdpOpsConfig.Current.CursorSdkLocal)
            return false;
        if (ForceUnavailableForTests)
            return false;
        if (ApiKeyPresentOverride is bool forced)
            return forced;
        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CURSOR_API_KEY"));
    }

    /// <summary>
    /// Prefer SDK local when configured and not force_cdt.
    /// Returns null → caller may CDT-escape. Non-null → ApplyFireOutcome shape.
    /// </summary>
    public static async Task<object?> TryDeliverAsync(
        IgniteArm arm,
        string charge,
        CancellationToken ct,
        string? cwd = null)
    {
        if (IsForceCdt(arm))
            return null;

        if (!IsSdkConfigured())
            return null;

        var workCwd = ResolveCwd(cwd);
        var req = new SdkFireRequest(
            Charge: charge,
            Cwd: workCwd,
            AgentId: arm.SdkAgentId,
            ArmId: arm.Id,
            SettingSources: ["project", "user"]);

        SdkFireResult result;
        try
        {
            result = BridgeOverride is not null
                ? await BridgeOverride(req, ct).ConfigureAwait(false)
                : await RunNodeBridgeAsync(req, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Hard bridge crash → CDT escape (sdk unavailable class).
            IdeFlightDataRecorder.RecordWake(
                "wake_sdk_bridge_fail", arm.Id, null, ex.Message);
            return null;
        }

        if (!result.Ok)
        {
            // Soft unavailable → CDT escape; hard send fail → surface error.
            if (result.Unavailable)
                return null;

            return new
            {
                ok = false,
                error = result.Error ?? "sdk_local_fail",
                detail = result.Detail,
                delivery_path = DeliveryPathSdkLocal,
                submit_kind = SubmitKindSdkLocal,
                submit_kind_after = SubmitKindSdkLocal,
                agent_id = result.AgentId ?? arm.SdkAgentId
            };
        }

        if (!string.IsNullOrWhiteSpace(result.AgentId))
            arm.SdkAgentId = result.AgentId;
        arm.DeliveryPath = DeliveryPathSdkLocal;

        _ = IdeIgniteWakeLatch.Publish(
            arm.Id,
            charge,
            ChannelSdkLocal,
            arm.Reason,
            arm.Task,
            sdkAgentId: arm.SdkAgentId);

        return new
        {
            ok = true,
            delivery_path = DeliveryPathSdkLocal,
            submit_kind = SubmitKindSdkLocal,
            submit_kind_after = SubmitKindSdkLocal,
            agent_id = arm.SdkAgentId,
            detail = result.Detail ?? "sdk_local_send_ok",
            cwd = workCwd
        };
    }

    public static string ResolveCwd(string? cwd)
    {
        if (!string.IsNullOrWhiteSpace(cwd))
            return Path.GetFullPath(cwd.Trim());
        try
        {
            var scm = GitSessionDefaults.TryResolveScmRoot(Environment.CurrentDirectory);
            if (!string.IsNullOrWhiteSpace(scm))
                return Path.GetFullPath(scm);
        }
        catch
        {
            /* ignore */
        }

        return Environment.CurrentDirectory;
    }

    static async Task<SdkFireResult> RunNodeBridgeAsync(SdkFireRequest req, CancellationToken ct)
    {
        var script = LocateBridgeScript();
        if (script is null)
            return new SdkFireResult(Ok: false, Unavailable: true, Error: "sdk_bridge_missing");

        var psi = new ProcessStartInfo
        {
            FileName = "node",
            Arguments = $"\"{script}\"",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = req.Cwd
        };

        using var proc = new Process { StartInfo = psi };
        if (!proc.Start())
            return new SdkFireResult(Ok: false, Unavailable: true, Error: "node_start_fail");

        var payload = JsonSerializer.Serialize(new
        {
            charge = req.Charge,
            cwd = req.Cwd,
            agent_id = req.AgentId,
            arm_id = req.ArmId,
            setting_sources = req.SettingSources
        });
        await proc.StandardInput.WriteAsync(payload.AsMemory(), ct).ConfigureAwait(false);
        await proc.StandardInput.FlushAsync(ct).ConfigureAwait(false);
        proc.StandardInput.Close();

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (proc.ExitCode != 0)
        {
            return new SdkFireResult(
                Ok: false,
                Unavailable: LooksUnavailable(stderr) || LooksUnavailable(stdout),
                Error: "sdk_bridge_exit",
                Detail: Trunc(stderr.Length > 0 ? stderr : stdout, 400));
        }

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(stdout) ? "{}" : stdout);
            var root = doc.RootElement;
            var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
            var agentId = root.TryGetProperty("agent_id", out var idEl) ? idEl.GetString() : null;
            var err = root.TryGetProperty("error", out var errEl) ? errEl.GetString() : null;
            var detail = root.TryGetProperty("detail", out var dEl) ? dEl.GetString() : null;
            var unavailable = root.TryGetProperty("unavailable", out var uEl)
                && uEl.ValueKind == JsonValueKind.True;
            return new SdkFireResult(ok, unavailable, err, detail, agentId);
        }
        catch (Exception ex)
        {
            return new SdkFireResult(
                Ok: false,
                Unavailable: true,
                Error: "sdk_bridge_parse",
                Detail: Trunc(ex.Message + " | " + stdout, 400));
        }
    }

    static string? LocateBridgeScript()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "scripts", "cursor-sdk-local-fire.mjs"),
            Path.Combine(Environment.CurrentDirectory, "scripts", "cursor-sdk-local-fire.mjs"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "scripts", "cursor-sdk-local-fire.mjs")
        };
        foreach (var c in candidates)
        {
            try
            {
                var full = Path.GetFullPath(c);
                if (File.Exists(full))
                    return full;
            }
            catch
            {
                /* ignore */
            }
        }

        return null;
    }

    static bool LooksUnavailable(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        return text.Contains("CURSOR_API_KEY", StringComparison.OrdinalIgnoreCase)
               || text.Contains("MODULE_NOT_FOUND", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Cannot find module", StringComparison.OrdinalIgnoreCase)
               || text.Contains("ENOENT", StringComparison.OrdinalIgnoreCase);
    }

    static string Trunc(string s, int n) =>
        s.Length <= n ? s : s[..n] + "…";

    internal readonly record struct SdkFireRequest(
        string Charge,
        string Cwd,
        string? AgentId,
        string ArmId,
        string[] SettingSources);

    internal readonly record struct SdkFireResult(
        bool Ok,
        bool Unavailable = false,
        string? Error = null,
        string? Detail = null,
        string? AgentId = null);
}
