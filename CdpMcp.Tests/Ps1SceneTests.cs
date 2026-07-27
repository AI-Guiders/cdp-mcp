#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public sealed class Ps1SceneTests
{
    [Fact]
    public void IsPs1Tool_matches_canonical_name()
    {
        Assert.True(Ps1Scene.IsPs1Tool("cdp_ps1_scene"));
        Assert.True(Ps1Scene.IsPs1Tool("CDP_PS1_SCENE"));
        Assert.False(Ps1Scene.IsPs1Tool("cdp_script_scene"));
    }

    [Fact]
    public async Task Scene_without_project_pulses_open_first()
    {
        var session = new SessionContext();
        var json = await Ps1Scene.DispatchAsync(
            new DocumentBufferStore(),
            session,
            new Dictionary<string, ICdpBackendModule>(),
            new Dictionary<string, JsonElement>(),
            default);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ps1_scene/v0", doc.RootElement.GetProperty("schema").GetString());
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains("cdp_open", doc.RootElement.GetProperty("pulse").GetString());
    }

    [Fact]
    public async Task Put_check_run_roundtrip_under_temp_project()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-ps1-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        try
        {
            var session = new SessionContext { ProjectRoot = root };
            var store = new DocumentBufferStore();
            var empty = new Dictionary<string, ICdpBackendModule>();

            var put = await Ps1Scene.DispatchAsync(store, session, empty, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("put"),
                ["name"] = JsonSerializer.SerializeToElement("smoke"),
                ["text"] = JsonSerializer.SerializeToElement("Write-Output 'ps1-ok'\n")
            }, default);
            using (var putDoc = JsonDocument.Parse(put))
            {
                Assert.True(putDoc.RootElement.GetProperty("ok").GetBoolean());
                Assert.EndsWith(".ps1", putDoc.RootElement.GetProperty("path").GetString());
            }

            var check = await Ps1Scene.DispatchAsync(store, session, empty, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("check"),
                ["name"] = JsonSerializer.SerializeToElement("smoke")
            }, default);
            using (var checkDoc = JsonDocument.Parse(check))
                Assert.True(checkDoc.RootElement.GetProperty("ok").GetBoolean());

            var run = await Ps1Scene.DispatchAsync(store, session, empty, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("run"),
                ["name"] = JsonSerializer.SerializeToElement("smoke")
            }, default);
            using var runDoc = JsonDocument.Parse(run);
            Assert.True(runDoc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(0, runDoc.RootElement.GetProperty("exit_code").GetInt32());
            Assert.Contains("ps1-ok", runDoc.RootElement.GetProperty("stdout").GetString());
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }
}
