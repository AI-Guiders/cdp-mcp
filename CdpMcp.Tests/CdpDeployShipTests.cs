using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

/// <summary>ADR-0209 stage 3 (ship): immutable snapshots, retire predicate, slot port pinning.</summary>
public sealed class CdpDeployShipTests : IDisposable
{
    readonly string _dir;

    public CdpDeployShipTests()
    {
        _dir = Path.Combine(
            Path.GetTempPath(),
            "cdp-mcp-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            /* best effort */
        }
    }

    [Theory]
    [InlineData("ship", Cdp.Deploy.CdpDeployMode.Ship)]
    [InlineData("sh", Cdp.Deploy.CdpDeployMode.Ship)]
    [InlineData("slot", Cdp.Deploy.CdpDeployMode.Ship)]
    [InlineData("SHIP", Cdp.Deploy.CdpDeployMode.Ship)]
    public void Mode_parser_ship_aliases(string input, Cdp.Deploy.CdpDeployMode expected) =>
        Assert.Equal(expected, Cdp.Deploy.CdpDeployModeParser.Parse(input));

    [Fact]
    public void Allocate_snapshot_dir_is_unique_and_under_staging_root()
    {
        var staging = Path.Combine(_dir, "cdp-service.staging");

        var a = Cdp.Deploy.CdpDeployShip.AllocateSnapshotDir(staging);
        var b = Cdp.Deploy.CdpDeployShip.AllocateSnapshotDir(staging);

        Assert.StartsWith(staging, a, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(staging, b, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(a, b);
        Assert.True(Directory.Exists(a));
        Assert.True(Directory.Exists(b));
    }

    [Fact]
    public void Move_to_snapshot_moves_tree_and_removes_stage()
    {
        var staged = Path.Combine(_dir, "cdp-service.next");
        Directory.CreateDirectory(Path.Combine(staged, "sub"));
        File.WriteAllText(Path.Combine(staged, "CdpService.exe"), "stub");
        File.WriteAllText(Path.Combine(staged, "sub", "CdpMcp.dll"), "stub");
        var snapshot = Path.Combine(_dir, "cdp-service.staging", "snap-1");

        Cdp.Deploy.CdpDeployShip.MoveToSnapshot(staged, snapshot);

        Assert.False(Directory.Exists(staged));
        Assert.True(File.Exists(Path.Combine(snapshot, "CdpService.exe")));
        Assert.True(File.Exists(Path.Combine(snapshot, "sub", "CdpMcp.dll")));
    }

    [Theory]
    // exe under live root → retire
    [InlineData(@"D:\cdp-service\CdpService.exe", 100, 200, 300, true)]
    // exe under a previous snapshot → retire (prefix must include the separator)
    [InlineData(@"D:\cdp-service.staging\20260909_CdpService\CdpService.exe", 100, 200, 300, true)]
    // the freshly started slot → keep
    [InlineData(@"D:\cdp-service.staging\new\CdpService.exe", 200, 200, 300, false)]
    // the caller itself → keep (ADR-0203: never kill the calling process tree)
    [InlineData(@"D:\cdp-service\CdpService.exe", 300, 200, 300, false)]
    // unrelated process → keep
    [InlineData(@"D:\elsewhere\CdpService.exe", 100, 200, 300, false)]
    // no path (access denied etc.) → keep
    [InlineData(null, 100, 200, 300, false)]
    public void ShouldRetire_predicate(string? exe, int pid, int keepPid, int selfPid, bool expected)
    {
        var roots = new[]
        {
            @"D:\cdp-service\",
            @"D:\cdp-service.staging\"
        };

        Assert.Equal(expected, Cdp.Deploy.CdpDeployShip.ShouldRetire(exe, pid, keepPid, selfPid, roots));
    }

    [Fact]
    public void IsPortFree_reports_occupied_port()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Assert.False(CdpSlotRegistry.IsPortFree(port));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void ResolveTarget_ship_defaults_to_service_install()
    {
        var d = IdeDeploy.ResolveTarget(
            IdeDeploy.DebugTarget,
            "cdp-debug",
            targetRaw: null,
            mode: "ship",
            force: false);

        Assert.True(d.Ok);
        Assert.Equal(IdeDeploy.ServiceTarget, d.Target);
    }

    [Fact]
    public void Run_ship_dry_run_plans_stage_publish_and_no_bridge()
    {
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", ".."));
        while (root is not null
               && !File.Exists(Path.Combine(root, "CdpMcp.csproj"))
               && Directory.GetParent(root) is { } parent)
            root = parent.FullName;

        var json = IdeDeploy.Run(
            new SessionContext { ProjectRoot = root },
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["mode"] = JsonSerializer.SerializeToElement("ship"),
                ["dry_run"] = JsonSerializer.SerializeToElement(true)
            });

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), json);
        Assert.Equal("ship", doc.RootElement.GetProperty("mode").GetString());

        // Ship publishes into the shared stage first (ServicePublishRoot = .next), then moves
        // it into an immutable snapshot; bridges are never ship targets (ADR-0209 service-only).
        var plan = doc.RootElement.GetProperty("plan");
        Assert.EndsWith("cdp-service.next", plan.GetProperty("service_publish").GetString());
        Assert.Equal(JsonValueKind.Null, plan.GetProperty("bridge_publish").ValueKind);
    }
}
