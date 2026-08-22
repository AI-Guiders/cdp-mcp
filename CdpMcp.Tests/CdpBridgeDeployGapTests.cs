#nullable enable
using System.Text.Json;
using CdpMcpBridge;
using TerminalMcp.Core;
using Xunit;

namespace CdpMcp.Tests;

[CollectionDefinition("BridgeDurable", DisableParallelization = true)]
public sealed class BridgeDurableCollection;

public sealed class CdpBridgeDeployPolicyTests
{
    [Theory]
    [InlineData("apply", false, null, null, true)]
    [InlineData("hard", false, null, null, true)]
    [InlineData("rollout", false, null, null, true)]
    [InlineData("soft", false, null, null, false)]
    [InlineData("apply", true, null, null, false)]
    [InlineData("apply", false, "false", null, false)]
    [InlineData("apply", false, null, "true", true)]
    [InlineData("soft", false, null, "true", true)]
    public void ShouldBridgeWait_modes(
        string mode,
        bool dryRun,
        string? bridgeWait,
        string? wait,
        bool expected)
    {
        var args = new Dictionary<string, JsonElement>
        {
            ["mode"] = Json(mode)
        };
        if (dryRun)
            args["dry_run"] = Json(true);
        if (bridgeWait is not null)
            args["bridge_wait"] = Json(bridgeWait);
        if (wait is not null)
            args["wait"] = Json(wait);

        Assert.Equal(expected, CdpBridgeDeployPolicy.ShouldBridgeWait(args));
    }

    [Fact]
    public void PrepareForwardDeployArgs_strips_wait_and_sets_background_durable()
    {
        var args = new Dictionary<string, JsonElement>
        {
            ["mode"] = Json("apply"),
            ["wait"] = Json(true),
            ["bridge_wait"] = Json(true),
            ["background"] = Json(false)
        };

        var forward = CdpBridgeDeployPolicy.PrepareForwardDeployArgs(args);

        Assert.False(forward.ContainsKey("wait"));
        Assert.False(forward.ContainsKey("bridge_wait"));
        Assert.True(ReadBool(forward["background"]));
        Assert.True(ReadBool(forward["durable"]));
        Assert.Equal("apply", forward["mode"].GetString());
    }

    static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    static bool ReadBool(JsonElement el) =>
        el.ValueKind == JsonValueKind.True
        || (el.ValueKind == JsonValueKind.String && bool.TryParse(el.GetString(), out var b) && b);
}

[Collection("BridgeDurable")]
public sealed class CdpBridgeDurableAccessTests : IDisposable
{
    readonly string _root;

    public CdpBridgeDurableAccessTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-bridge-durable-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        DurableJobStore.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        DurableJobStore.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void ReadLifecycleLast_annotates_bridge_local()
    {
        var jobId = "deploy-test-001";
        var record = new DurableJobRecord
        {
            JobId = jobId,
            Kind = "deploy",
            IgniteEvent = "peer_ship",
            State = "idle",
            ResultJson = """{"schema":"lifecycle_job/v0","ok":true,"state":"idle","job_id":"deploy-test-001"}"""
        };
        WriteRecord(record);

        var json = CdpBridgeDurableAccess.ReadLifecycleLast(new Dictionary<string, JsonElement>
        {
            ["job_id"] = JsonSerializer.SerializeToElement(jobId)
        });

        Assert.Contains("bridge_local", json, StringComparison.Ordinal);
        Assert.Contains("deploy-test-001", json, StringComparison.Ordinal);
    }

    [Fact]
    public void HasInFlightDeploy_detects_running_job()
    {
        WriteRecord(new DurableJobRecord
        {
            JobId = "deploy-running",
            Kind = "deploy",
            IgniteEvent = "peer_ship",
            State = "running",
            EnqueuedUtc = DateTimeOffset.UtcNow
        });

        Assert.True(CdpBridgeDurableAccess.HasInFlightDeploy());
        Assert.Equal("deploy-running", CdpBridgeDurableAccess.InFlightDeployJobId());
    }

    [Fact]
    public void IsRunningState_recognizes_queued_and_running()
    {
        Assert.True(CdpBridgeDurableAccess.IsRunningState("""{"state":"running"}"""));
        Assert.True(CdpBridgeDurableAccess.IsRunningState("""{"state":"queued"}"""));
        Assert.False(CdpBridgeDurableAccess.IsRunningState("""{"state":"idle"}"""));
    }

    static void WriteRecord(DurableJobRecord record)
    {
        var dir = Path.Combine(DurableJobStore.JobsDirectory);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, record.JobId + ".json");
        var json = JsonSerializer.Serialize(record, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });
        File.WriteAllText(path, json);
    }
}

[Collection("BridgeDurable")]
public sealed class CdpBridgeServiceEnsurerDeployGuardTests : IDisposable
{
    readonly string _root;

    public CdpBridgeServiceEnsurerDeployGuardTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-bridge-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        DurableJobStore.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        DurableJobStore.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void ShouldSuppressAutoStart_when_deploy_in_flight()
    {
        var dir = DurableJobStore.JobsDirectory;
        Directory.CreateDirectory(dir);
        var record = new DurableJobRecord
        {
            JobId = "deploy-guard",
            Kind = "deploy",
            IgniteEvent = "peer_ship",
            State = "running"
        };
        File.WriteAllText(
            Path.Combine(dir, "deploy-guard.json"),
            JsonSerializer.Serialize(record, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            }));

        var ensurer = new CdpBridgeServiceEnsurer(new CdpBridgeSettings
        {
            BaseUrl = new Uri("http://127.0.0.1:8771/"),
            Token = "t",
            InstallDir = @"D:\cdp-service",
            AutoStart = true
        });

        Assert.True(ensurer.ShouldSuppressAutoStart());
    }
}
