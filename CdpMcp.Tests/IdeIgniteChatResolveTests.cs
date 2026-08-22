using System.Text.Json;
using Cdp.Core;
using CdpMcp.Backends;
using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeIgniteChatResolveTests
{
    [Fact]
    public void ResolveChat_prefers_arm_field_then_conversation_latch_then_bridge_latch()
    {
        CdpTenantComposerLatch.TrySet("bridge-x", "Per-conv chat", "conv-1");
        CdpTenantComposerLatch.TrySet("bridge-x", "Bridge-wide chat");

        Assert.Equal("explicit", IdeIgniteArmHost.ResolveChatFromTenantLatch(
            "bridge-x:default:main", "conv-1", "explicit"));

        Assert.Equal("Per-conv chat", IdeIgniteArmHost.ResolveChatFromTenantLatch(
            "bridge-x:default:main", "conv-1", null));

        Assert.Equal("Bridge-wide chat", IdeIgniteArmHost.ResolveChatFromTenantLatch(
            "bridge-x:default:main", "conv-9", null));

        Assert.Null(IdeIgniteArmHost.ResolveChatFromTenantLatch(
            "bridge-missing:default:main", "conv-1", null));
    }

    [Fact]
    public void Arm_stamps_conversation_id_and_chat_from_latch()
    {
        var settings = CdpSettings.Load(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "config", "cdp-mcp.toml"));
        var kernel = new CdpSharedKernel
        {
            ConfigPath = "config/cdp-mcp.toml",
            Settings = settings,
            Modules = [],
            ByDomain = new Dictionary<string, ICdpBackendModule>(),
            AllAffordances = [],
            McpVersion = "0.0.0",
            Pretty = new JsonSerializerOptions(),
            McpOutlet = new McpOutletHabitat(),
            InternetBrowser = new InternetBrowserHabitat(),
            AnTools = new Dictionary<string, ModelContextProtocol.Protocol.Tool>(),
            TkTools = new Dictionary<string, ModelContextProtocol.Protocol.Tool>(),
            FindTools = new Dictionary<string, ModelContextProtocol.Protocol.Tool>(),
            FailTools = new Dictionary<string, ModelContextProtocol.Protocol.Tool>(),
            DbgTools = new Dictionary<string, ModelContextProtocol.Protocol.Tool>(),
            BtTools = new Dictionary<string, ModelContextProtocol.Protocol.Tool>(),
            RoslynTools = new Dictionary<string, ModelContextProtocol.Protocol.Tool>(),
            GitTools = new Dictionary<string, ModelContextProtocol.Protocol.Tool>(),
            HciTools = new Dictionary<string, ModelContextProtocol.Protocol.Tool>(),
            AnuiTools = new Dictionary<string, ModelContextProtocol.Protocol.Tool>()
        };
        var slice = CdpTenantSliceFactory.Create(
            kernel,
            CdpTenantKey.Normalize("bridge-chat", "ws1", "main"));
        CdpTenantComposerLatch.TrySet("bridge-chat", "Sierra CDP leaf", "pt:42");

        using (CdpTenantRoutingContext.Enter("pt:42"))
        using (CdpTenantExecutionContext.Enter(slice))
        {
            var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["when"] = JsonSerializer.SerializeToElement("timer"),
                ["in"] = JsonSerializer.SerializeToElement("1h"),
                ["task"] = JsonSerializer.SerializeToElement("chat resolve test"),
                ["force"] = JsonSerializer.SerializeToElement(true)
            };

            var json = JsonSerializer.Serialize(IdeIgniteArmHost.Arm(args));
            using var doc = JsonDocument.Parse(json);
            var arm = doc.RootElement.GetProperty("arm");
            Assert.Equal("Sierra CDP leaf", arm.GetProperty("chat").GetString());
            Assert.Equal("pt:42", arm.GetProperty("conversation_id").GetString());
            var armId = arm.GetProperty("id").GetString();
            if (armId is not null)
            {
                IdeIgniteArmHost.Disarm(new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["id"] = JsonSerializer.SerializeToElement(armId)
                });
            }
        }

        slice.Dispose();
    }
}
