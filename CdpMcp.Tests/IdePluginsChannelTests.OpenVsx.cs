#nullable enable
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;
public sealed partial class IdePluginsChannelTests
{
    [Fact]
    public void OpenVsx_TryParseId()
    {
        Assert.True(OpenVsxClient.TryParseId("jebbs.plantuml", out var ns, out var name));
        Assert.Equal("jebbs", ns);
        Assert.Equal("plantuml", name);
        Assert.True(OpenVsxClient.TryParseId("openvsx:jebbs/plantuml", out ns, out name));
        Assert.Equal("jebbs", ns);
        Assert.Equal("plantuml", name);
        Assert.False(OpenVsxClient.TryParseId("noperiod", out _, out _));
    }

    [Fact]
    public void Search_then_install_by_id_via_stub_handler()
    {
        var pluginsRoot = Path.Combine(Path.GetTempPath(), "cdp-plugins-ovsx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pluginsRoot);
        var prevPlugins = Environment.GetEnvironmentVariable("CDP_PLUGINS_ROOT");
        var prevBase = Environment.GetEnvironmentVariable("CDP_OPENVSX_BASE");
        var vsix = MakeTinyVsixBytes();
        try
        {
            Environment.SetEnvironmentVariable("CDP_PLUGINS_ROOT", pluginsRoot);
            Environment.SetEnvironmentVariable("CDP_OPENVSX_BASE", "http://ovsx.test");
            OpenVsxClient.TestHandler = new StubOpenVsxHandler(vsix);
            OpenVsxClient.ResetHttpForTests();
            var store = new DocumentBufferStore();
            var session = new SessionContext
            {
                ProjectRoot = Path.GetTempPath()
            };
            var args = new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("search"),
                ["q"] = JsonSerializer.SerializeToElement("plantuml")
            };
            var searchBoard = IdePluginsChannel.Handle(store, session, args);
            var searchJson = JsonSerializer.Serialize(searchBoard);
            using (var doc = JsonDocument.Parse(searchJson))
            {
                Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), searchJson);
                Assert.Equal("search", doc.RootElement.GetProperty("detail").GetString());
                Assert.True(doc.RootElement.GetProperty("counts").GetProperty("hits").GetInt32() >= 1);
            }

            args = new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("install"),
                ["id"] = JsonSerializer.SerializeToElement("jebbs.plantuml")
            };
            var installBoard = IdePluginsChannel.Handle(store, session, args);
            var installJson = JsonSerializer.Serialize(installBoard);
            using (var doc = JsonDocument.Parse(installJson))
            {
                Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), installJson);
                Assert.Equal(1, doc.RootElement.GetProperty("counts").GetProperty("attention").GetInt32());
                var action = doc.RootElement.GetProperty("action");
                Assert.True(action.GetProperty("ok").GetBoolean());
                Assert.Equal("A", action.GetProperty("plugin").GetProperty("mode").GetString());
            }
        }
        finally
        {
            OpenVsxClient.TestHandler = null;
            OpenVsxClient.ResetHttpForTests();
            Environment.SetEnvironmentVariable("CDP_PLUGINS_ROOT", prevPlugins);
            Environment.SetEnvironmentVariable("CDP_OPENVSX_BASE", prevBase);
            try
            {
                Directory.Delete(pluginsRoot, true);
            }
            catch
            { /* ignore */
            }
        }
    }

    [Fact]
    public void Group_disable_hides_from_attention()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-plugins-grp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var prev = Environment.GetEnvironmentVariable("CDP_PLUGINS_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("CDP_PLUGINS_ROOT", root);
            var ext = MakeTinyPlantExtension();
            Assert.True(CdpPluginQuarantine.InstallFromUnpacked(ext).Ok);
            var off = CdpPluginQuarantine.SetGroupEnabled("diagrams", enabled: false);
            Assert.True(off.Ok, off.Error);
            Assert.Empty(CdpPluginQuarantine.List(attentionOnly: true));
            Assert.Single(CdpPluginQuarantine.List(attentionOnly: false));
            Assert.Null(CdpPluginQuarantine.ResolvePlantUmlJar());
            var on = CdpPluginQuarantine.SetGroupEnabled("diagrams", enabled: true);
            Assert.True(on.Ok);
            Assert.Single(CdpPluginQuarantine.List(attentionOnly: true));
            Assert.NotNull(CdpPluginQuarantine.ResolvePlantUmlJar());
            var manual = CdpPluginQuarantine.AddToGroup("jebbs.plantuml", "work");
            Assert.True(manual.Ok, manual.Error);
            Assert.Contains("work", manual.Plugin!.Groups);
            var ccl = IdeRepl.Apply("plugins disable group diagrams", new Dictionary<string, JsonElement>());
            Assert.Equal("disable", ccl!.Value.Args["go_args"].GetProperty("op").GetString());
            Assert.Equal("diagrams", ccl.Value.Args["go_args"].GetProperty("group").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("CDP_PLUGINS_ROOT", prev);
            try
            {
                Directory.Delete(root, true);
            }
            catch
            { /* ignore */
            }
        }
    }

    [Fact]
    public void Install_missing_target_error_card()
    {
        var store = new DocumentBufferStore();
        var session = new SessionContext
        {
            ProjectRoot = Path.GetTempPath()
        };
        var args = new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("install")
        };
        var board = IdePluginsChannel.Handle(store, session, args);
        var json = JsonSerializer.Serialize(board);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("install_target_required", doc.RootElement.GetProperty("action").GetProperty("error").GetString());
    }

    static string MakeTinyPlantExtension()
    {
        var ext = Path.Combine(Path.GetTempPath(), "cdp-ext-" + Guid.NewGuid().ToString("N"), "extension");
        Directory.CreateDirectory(ext);
        File.WriteAllText(Path.Combine(ext, "package.json"), """
            {"name":"plantuml","displayName":"PlantUML","version":"9.9.9","publisher":"jebbs"}
            """.Trim(), Encoding.UTF8);
        File.WriteAllBytes(Path.Combine(ext, "plantuml.jar"), Encoding.ASCII.GetBytes("PK fake-jar"));
        return ext;
    }

    static byte[] MakeTinyVsixBytes()
    {
        var work = Path.Combine(Path.GetTempPath(), "cdp-vsix-build-" + Guid.NewGuid().ToString("N"));
        var ext = Path.Combine(work, "extension");
        Directory.CreateDirectory(ext);
        File.WriteAllText(Path.Combine(ext, "package.json"), """{"name":"plantuml","displayName":"PlantUML","version":"2.18.1","publisher":"jebbs"}""", Encoding.UTF8);
        // >= 64 bytes for download_empty guard
        File.WriteAllBytes(Path.Combine(ext, "plantuml.jar"), Encoding.ASCII.GetBytes("PK fake-jar-bytes-padded-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"));
        var vsix = Path.Combine(Path.GetTempPath(), "cdp-vsix-" + Guid.NewGuid().ToString("N") + ".vsix");
        if (File.Exists(vsix))
            File.Delete(vsix);
        ZipFile.CreateFromDirectory(work, vsix);
        var bytes = File.ReadAllBytes(vsix);
        try
        {
            Directory.Delete(work, true);
        }
        catch
        { /* ignore */
        }

        try
        {
            File.Delete(vsix);
        }
        catch
        { /* ignore */
        }

        return bytes;
    }
}