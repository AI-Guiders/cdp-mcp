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
    public void Build_empty_pulse()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-plugins-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var prev = Environment.GetEnvironmentVariable("CDP_PLUGINS_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("CDP_PLUGINS_ROOT", root);
            var snap = IdePluginsChannel.Build();
            Assert.True(snap.Ok);
            Assert.Equal(0, snap.Count);
            Assert.Contains("empty", snap.Pulse, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CDP_PLUGINS_ROOT", prev);
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Install_mode_a_and_list()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-plugins-inst-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var prev = Environment.GetEnvironmentVariable("CDP_PLUGINS_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("CDP_PLUGINS_ROOT", root);
            var ext = MakeTinyPlantExtension();
            var result = CdpPluginQuarantine.InstallFromUnpacked(ext);
            Assert.True(result.Ok, result.Error + " " + result.Hint);
            Assert.NotNull(result.Plugin);
            Assert.Equal("A", result.Plugin!.Mode);
            Assert.True(File.Exists(result.Plugin.JarPath!));

            var snap = IdePluginsChannel.Build();
            Assert.Equal(1, snap.Count);
            Assert.Equal(1, snap.ModeA);
            Assert.True(snap.Plugins[0].Attention);
            Assert.Contains("diagrams", snap.Plugins[0].Groups);

            var store = new DocumentBufferStore();
            var session = new SessionContext { ProjectRoot = Path.GetTempPath() };
            var board = IdePluginsChannel.Handle(store, session);
            var json = JsonSerializer.Serialize(board);
            using var doc = JsonDocument.Parse(json);
            Assert.Equal("plugins", doc.RootElement.GetProperty("go").GetString());
            Assert.Equal(1, doc.RootElement.GetProperty("counts").GetProperty("attention").GetInt32());
        }
        finally
        {
            Environment.SetEnvironmentVariable("CDP_PLUGINS_ROOT", prev);
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Install_mode_a_exe_payload()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-plugins-exe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var prev = Environment.GetEnvironmentVariable("CDP_PLUGINS_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("CDP_PLUGINS_ROOT", root);
            var ext = Path.Combine(Path.GetTempPath(), "cdp-ext-exe-" + Guid.NewGuid().ToString("N"), "extension");
            Directory.CreateDirectory(Path.Combine(ext, "bin"));
            File.WriteAllText(Path.Combine(ext, "package.json"),
                """{"name":"tool","displayName":"Tool","version":"1.0.0","publisher":"acme"}""",
                Encoding.UTF8);
            File.WriteAllBytes(Path.Combine(ext, "bin", "tool.exe"), Encoding.ASCII.GetBytes("MZ fake-exe"));

            var result = CdpPluginQuarantine.InstallFromUnpacked(ext);
            Assert.True(result.Ok, result.Error + " " + result.Hint);
            Assert.Equal("A", result.Plugin!.Mode);
            Assert.Equal("exe", result.Plugin.PayloadKind);
            Assert.True(File.Exists(result.Plugin.PayloadPath!));
            Assert.Null(result.Plugin.JarPath);
            Assert.Contains("Mode A", result.Hint!, StringComparison.OrdinalIgnoreCase);

            var n = CdpPluginQuarantine.ReharvestInstalled();
            Assert.True(n >= 1);
            var again = CdpPluginQuarantine.List(attentionOnly: true).Single();
            Assert.Equal("A", again.Mode);
            Assert.Equal("exe", again.PayloadKind);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CDP_PLUGINS_ROOT", prev);
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Install_nested_jar_is_mode_a_and_mode_d_disabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-plugins-nest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var prev = Environment.GetEnvironmentVariable("CDP_PLUGINS_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("CDP_PLUGINS_ROOT", root);
            var ext = Path.Combine(Path.GetTempPath(), "cdp-ext-nest-" + Guid.NewGuid().ToString("N"), "extension");
            var toolDir = Path.Combine(ext, "resources", "checkstyle-9.0");
            Directory.CreateDirectory(toolDir);
            File.WriteAllText(Path.Combine(ext, "package.json"),
                """{"name":"checker","displayName":"Checker","version":"1.0.0","publisher":"acme","contributes":{"languages":[]}}""",
                Encoding.UTF8);
            File.WriteAllBytes(Path.Combine(toolDir, "checkstyle-9.0-all.jar"), Encoding.ASCII.GetBytes("PK fake-all"));
            File.WriteAllBytes(Path.Combine(ext, "resources", "findsecbugs-plugin.jar"),
                Encoding.ASCII.GetBytes("PK fake-plugin"));

            var a = CdpPluginQuarantine.InstallFromUnpacked(ext);
            Assert.True(a.Ok, a.Hint);
            Assert.Equal("A", a.Plugin!.Mode);
            Assert.True(a.Plugin.Enabled);
            Assert.Contains("checkstyle-9.0-all.jar", a.Plugin.PayloadPath!, StringComparison.OrdinalIgnoreCase);

            var refuseExt = Path.Combine(Path.GetTempPath(), "cdp-ext-refuse-" + Guid.NewGuid().ToString("N"), "extension");
            Directory.CreateDirectory(refuseExt);
            File.WriteAllText(Path.Combine(refuseExt, "package.json"),
                """{"name":"pretty","displayName":"Pretty","version":"1.0.0","publisher":"acme","contributes":{"snippets":[]}}""",
                Encoding.UTF8);
            var d = CdpPluginQuarantine.InstallFromUnpacked(refuseExt);
            Assert.True(d.Ok, d.Hint);
            Assert.Equal("D", d.Plugin!.Mode);
            Assert.False(d.Plugin.Enabled);
            Assert.False(d.Plugin.Attention);
            Assert.Empty(CdpPluginQuarantine.List(attentionOnly: true).Where(p => p.Mode == "D"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CDP_PLUGINS_ROOT", prev);
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }


    [Fact]
    public void Ccl_plugins_preview_sets_op()
    {
        var applied = IdeRepl.Apply("plugins preview", new Dictionary<string, JsonElement>());
        Assert.NotNull(applied);
        Assert.Null(applied!.Value.Direct);
        Assert.True(applied.Value.Args.TryGetValue("go", out var go));
        Assert.Equal("plugins", go.GetString());
        Assert.True(applied.Value.Args.TryGetValue("go_args", out var ga));
        Assert.Equal("preview", ga.GetProperty("op").GetString());
    }

    [Fact]
    public void Ccl_plugins_search_and_install_id()
    {
        var search = IdeRepl.Apply("plugins search plantuml", new Dictionary<string, JsonElement>());
        Assert.NotNull(search);
        Assert.Equal("search", search!.Value.Args["go_args"].GetProperty("op").GetString());
        Assert.Equal("plantuml", search.Value.Args["go_args"].GetProperty("q").GetString());

        var install = IdeRepl.Apply("plugins install jebbs.plantuml", new Dictionary<string, JsonElement>());
        Assert.NotNull(install);
        var ga = install!.Value.Args["go_args"];
        Assert.Equal("install", ga.GetProperty("op").GetString());
        Assert.Equal("jebbs.plantuml", ga.GetProperty("id").GetString());

        var row = IdeRepl.Apply("plugins s1", new Dictionary<string, JsonElement>());
        Assert.Equal("install", row!.Value.Args["go_args"].GetProperty("op").GetString());
        Assert.Equal("s1", row.Value.Args["go_args"].GetProperty("row").GetString());
    }

    [Fact]
    public void Ccl_plugins_want_sets_op()
    {
        var want = IdeRepl.Apply("plugins want plantuml", new Dictionary<string, JsonElement>());
        Assert.NotNull(want);
        var ga = want!.Value.Args["go_args"];
        Assert.Equal("want", ga.GetProperty("op").GetString());
        Assert.Equal("plantuml", ga.GetProperty("q").GetString());
    }

    [Fact]
    public void Want_feature_fit_rejects_mega_tag_only()
    {
        var shell = new OpenVsxClient.Hit("timonwong", "shellcheck", "1.0.0", "ShellCheck", null, null);
        Assert.True(IdePluginsChannel.WantHitNameMatch(shell, "shellcheck"));
        Assert.True(IdePluginsChannel.WantHitFitsFeature(shell, "shellcheck", plugin: null));

        var trunk = new OpenVsxClient.Hit("trunk", "io", "3.0.0", "Trunk Code Quality", null, null);
        Assert.False(IdePluginsChannel.WantHitNameMatch(trunk, "shellcheck"));

        var megaGroups = Enumerable.Range(0, 40).Select(i => "tag" + i).Append("shellcheck").ToArray();
        var mega = new CdpPluginQuarantine.PluginInfo(
            "openvsx:trunk.io",
            "Trunk",
            "3.0.0",
            "A",
            "/tmp",
            "/tmp/bin",
            "bin",
            "/tmp/cdp-plugin.json",
            true,
            megaGroups,
            true);
        Assert.False(IdePluginsChannel.WantHitFitsFeature(trunk, "shellcheck", mega));

        var fair = new OpenVsxClient.Hit("FairTree", "java-code-checker", "1.0.0", "Java Code Checker", null, null);
        var fairPlugin = new CdpPluginQuarantine.PluginInfo(
            "openvsx:FairTree.java-code-checker",
            "Java Code Checker",
            "1.0.0",
            "A",
            "/tmp",
            "/tmp/checkstyle.jar",
            "jar",
            "/tmp/cdp-plugin.json",
            true,
            ["checker", "checkstyle", "java", "lint"],
            true);
        Assert.True(IdePluginsChannel.WantHitFitsFeature(fair, "checkstyle", fairPlugin));
    }


    sealed class StubOpenVsxHandler(byte[] vsix) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "/";
            HttpResponseMessage resp;
            if (path.Contains("/search", StringComparison.OrdinalIgnoreCase))
            {
                resp = Json(HttpStatusCode.OK,
                    """{"extensions":[{"namespace":"jebbs","name":"plantuml","version":"2.18.1","displayName":"PlantUML","description":"mock"}]}""");
            }
            else if (path.Contains("/latest", StringComparison.OrdinalIgnoreCase)
                     || (path.Contains("/jebbs/plantuml", StringComparison.OrdinalIgnoreCase)
                         && !path.Contains("/file/", StringComparison.OrdinalIgnoreCase)))
            {
                resp = Json(HttpStatusCode.OK,
                    """{"namespace":"jebbs","name":"plantuml","version":"2.18.1","displayName":"PlantUML","description":"mock","files":{"download":"http://ovsx.test/api/jebbs/plantuml/2.18.1/file/jebbs.plantuml-2.18.1.vsix"}}""");
            }
            else if (path.EndsWith(".vsix", StringComparison.OrdinalIgnoreCase)
                     || path.Contains("/file/", StringComparison.OrdinalIgnoreCase))
            {
                resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(vsix)
                };
                resp.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            }
            else
            {
                resp = new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("not found: " + path)
                };
            }

            return Task.FromResult(resp);
        }

        static HttpResponseMessage Json(HttpStatusCode code, string body)
        {
            var resp = new HttpResponseMessage(code)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            return resp;
        }
    }
}
