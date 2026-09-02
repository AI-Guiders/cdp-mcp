#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.Backends;
using Microsoft.AspNetCore.Http;
using TerminalMcp.Core;
using Xunit;

namespace CdpMcp.Tests;

public sealed class CdpTenantMultiplexTests
{
    [Fact]
    public void TenantKey_wire_and_sanitize()
    {
        var key = CdpTenantKey.Normalize("bridge-1", "wsAbc", "composer_main");
        Assert.Equal("bridge-1:wsAbc:composer_main", key.Wire);
        Assert.False(key.IsLegacyDefault);
    }

    [Fact]
    public void TenantHeaders_parse_from_http()
    {
        var headers = new HeaderDictionary
        {
            [CdpTenantHeaders.BridgeSession] = "b1",
            [CdpTenantHeaders.WorkspaceKey] = "cdp",
            [CdpTenantHeaders.Composer] = "main"
        };

        var key = CdpTenantHeaders.TryParse(headers);
        Assert.NotNull(key);
        Assert.Equal("b1:cdp:main", key.Value.Wire);
    }

    [Fact]
    public void ComposerLatch_per_conversation_isolated()
    {
        Assert.True(CdpTenantComposerLatch.TrySet("bridge-a", "Chat Alpha", "conv-a"));
        Assert.True(CdpTenantComposerLatch.TrySet("bridge-a", "Chat Beta", "conv-b"));
        Assert.Equal("Chat Alpha", CdpTenantComposerLatch.ResolveDefaultChat("bridge-a", "conv-a"));
        Assert.Equal("Chat Beta", CdpTenantComposerLatch.ResolveDefaultChat("bridge-a", "conv-b"));
        Assert.Null(CdpTenantComposerLatch.ResolveDefaultChat("bridge-a", "conv-unknown"));
    }

    [Fact]
    public void TenantHeaders_resolve_latched_composer_per_conversation()
    {
        CdpTenantComposerLatch.TrySet("b1", "My Feature Chat", "pt:turn-1");
        var headers = new HeaderDictionary
        {
            [CdpTenantHeaders.BridgeSession] = "b1",
            [CdpTenantHeaders.WorkspaceKey] = "cdp",
            [CdpTenantHeaders.Composer] = "main",
            [CdpTenantHeaders.ConversationId] = "pt:turn-1"
        };

        var key = CdpTenantHeaders.TryParse(headers);
        Assert.NotNull(key);
        Assert.Contains("MyFeatureChat", key.Value.Composer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ComposerLatch_tracks_bridge_session()
    {
        Assert.True(CdpTenantComposerLatch.TrySet("bridge-a", "chat-b"));
        Assert.Equal("chat-b", CdpTenantComposerLatch.Get("bridge-a"));
        Assert.Equal("chat-b", CdpTenantComposerLatch.ResolveDefaultChat("bridge-a"));
    }

    [Fact]
    public void Registry_creates_distinct_slices_with_isolated_shell()
    {
        var settings = CdpSettings.Load(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "config", "cdp-mcp.toml"));
        var session = new SessionContext();
        var doc = new DocumentBufferStore();
        var ws = new WorkspaceDbHost(settings.IntentWorkspace.DatabasePath, session);
        var shell = new ShellHabitat();
        var ideSettings = new IdeSettingsHabitat(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "config", "cdp-mcp.toml"),
            settings,
            session,
            shell,
            () => ProgramHost.ShellDefaults(session));
        var kernel = new CdpSharedKernel
        {
            ConfigPath = "config/cdp-mcp.toml",
            Settings = settings,
            Modules = [],
            ByDomain = new Dictionary<string, ICdpBackendModule>(),
            AllAffordances = [],
            McpVersion = "0.0.0",
            Pretty = new System.Text.Json.JsonSerializerOptions(),
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
        var registry = new CdpTenantRegistry(
            kernel,
            CdpTenantSliceFactory.WrapLegacy(
                CdpTenantKey.LegacyDefault,
                session,
                doc,
                ws,
                shell,
                ideSettings,
                null,
                CdpProfile.StateRoot));

        var a = registry.Resolve(CdpTenantKey.Normalize("a", "ws1", "main"));
        var b = registry.Resolve(CdpTenantKey.Normalize("b", "ws1", "main"));
        Assert.NotSame(a, b);
        Assert.NotSame(a.Session, b.Session);
        Assert.NotSame(a.Shell, b.Shell);
        Assert.Equal(2, registry.ActiveCount);
    }

    [Fact]
    public void Registry_snapshot_active_lists_wire_and_project_root()
    {
        var settings = CdpSettings.Load(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "config", "cdp-mcp.toml"));
        var session = new SessionContext();
        var doc = new DocumentBufferStore();
        var ws = new WorkspaceDbHost(settings.IntentWorkspace.DatabasePath, session);
        var shell = new ShellHabitat();
        var ideSettings = new IdeSettingsHabitat(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "config", "cdp-mcp.toml"),
            settings,
            session,
            shell,
            () => ProgramHost.ShellDefaults(session));
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
        var registry = new CdpTenantRegistry(
            kernel,
            CdpTenantSliceFactory.WrapLegacy(
                CdpTenantKey.LegacyDefault,
                session,
                doc,
                ws,
                shell,
                ideSettings,
                null,
                CdpProfile.StateRoot));

        var forgeKey = CdpTenantKey.Normalize("bridge-forge", "cdp", "forge-chat");
        var cursorKey = CdpTenantKey.Normalize("bridge-cursor", "default", "cursor-chat");
        var forgeSlice = registry.Resolve(forgeKey);
        var cursorSlice = registry.Resolve(cursorKey);
        forgeSlice.Session.ProjectRoot = @"D:\repo\agent-forge";
        cursorSlice.Session.ProjectRoot = @"D:\repo\cdp-mcp";

        var snap = registry.SnapshotActive();
        Assert.Equal(2, snap.Count);
        Assert.Contains(snap, s => s.BridgeSession == "bridge-forge" && s.WorkspaceKey == "cdp" && s.ProjectRoot == @"D:\repo\agent-forge");
        Assert.Contains(snap, s => s.BridgeSession == "bridge-cursor" && s.WorkspaceKey == "default" && s.ProjectRoot == @"D:\repo\cdp-mcp");
        Assert.True(snap[0].LastTouchUtc >= snap[1].LastTouchUtc);
    }

    [Fact]
    public void ComposerLatch_resolve_default_chat_skips_main()
    {
        CdpTenantComposerLatch.TrySet("bridge-a", "CDP ADR continuation");
        Assert.Equal("CDP ADR continuation", CdpTenantComposerLatch.ResolveDefaultChat("bridge-a"));
        Assert.Null(CdpTenantComposerLatch.ResolveDefaultChat("unknown"));
        Assert.Null(CdpTenantComposerLatch.ResolveDefaultChat(null));
        CdpTenantComposerLatch.TrySet("bridge-main", "main");
        Assert.Null(CdpTenantComposerLatch.ResolveDefaultChat("bridge-main"));
    }

    [Fact]
    public void Arm_defaults_chat_from_tenant_composer_latch()
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
            CdpTenantKey.Normalize("bridge-arm", "ws1", "main"));
        CdpTenantComposerLatch.TrySet("bridge-arm", "CDP ADR continuation");

        using (CdpTenantExecutionContext.Enter(slice))
        {
            var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["when"] = JsonSerializer.SerializeToElement("timer"),
                ["in"] = JsonSerializer.SerializeToElement("5m"),
                ["task"] = JsonSerializer.SerializeToElement("test leaf"),
                ["force"] = JsonSerializer.SerializeToElement(true)
            };
            var json = JsonSerializer.Serialize(IdeIgniteArmHost.Arm(args));
            using var doc = JsonDocument.Parse(json);
            var chat = doc.RootElement.GetProperty("arm").GetProperty("chat").GetString();
            Assert.Equal("CDP ADR continuation", chat);
            var armId = doc.RootElement.GetProperty("arm").GetProperty("id").GetString();
            if (armId is not null)
                IdeIgniteArmHost.Disarm(new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["id"] = JsonSerializer.SerializeToElement(armId)
                });
        }

        slice.Dispose();
    }

    [Fact]
    public void Profile_tenant_scope_overrides_state_root()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CDP_PROFILE") ?? "default",
                "default",
                StringComparison.OrdinalIgnoreCase))
            return;

        var baseRoot = CdpProfile.StateRoot;
        var tenantRoot = Path.Combine(baseRoot, "tenant-test");
        using (CdpProfile.EnterTenantStateRoot(tenantRoot))
        {
            Assert.Equal(Path.GetFullPath(tenantRoot), Path.GetFullPath(CdpProfile.StateRoot));
        }

        Assert.Equal(baseRoot, CdpProfile.StateRoot);
    }

    [Fact]
    public async Task Parallel_tenant_resolvers_stay_isolated_without_global_swap()
    {
        var legacy = new SessionContext { ProjectRoot = @"D:\legacy" };
        var prior = CitizenRouteHost.SessionResolver;
        CitizenRouteHost.SessionResolver = () =>
            CdpTenantExecutionContext.CurrentSlice?.Session ?? legacy;

        try
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
            var registry = new CdpTenantRegistry(
                kernel,
                CdpTenantSliceFactory.Create(kernel, CdpTenantKey.LegacyDefault));

            var forge = registry.Resolve(CdpTenantKey.Normalize("bridge-forge", "cdp", "main"));
            var cursor = registry.Resolve(CdpTenantKey.Normalize("bridge-cursor", "default", "main"));
            forge.Session.ProjectRoot = @"D:\repo\agent-forge";
            cursor.Session.ProjectRoot = @"D:\repo\cdp-mcp";

            async Task<string?> ProbeAsync(CdpTenantSlice slice)
            {
                using var _ = CdpTenantExecutionContext.Enter(slice);
                await Task.Delay(Random.Shared.Next(1, 15)).ConfigureAwait(false);
                return CitizenRouteHost.SessionResolver?.Invoke()?.ProjectRoot;
            }

            var tasks = Enumerable.Range(0, 48)
                .Select(i => ProbeAsync(i % 2 == 0 ? forge : cursor))
                .ToArray();
            var roots = await Task.WhenAll(tasks).ConfigureAwait(false);

            Assert.All(roots, r => Assert.True(
                string.Equals(r, @"D:\repo\agent-forge", StringComparison.Ordinal)
                || string.Equals(r, @"D:\repo\cdp-mcp", StringComparison.Ordinal)));

            Assert.NotSame(forge.DocStore, cursor.DocStore);
            using (CdpTenantExecutionContext.Enter(forge))
                Assert.Same(forge.DocStore, IdeLanguageTools.TryGetDocumentStore());
            using (CdpTenantExecutionContext.Enter(cursor))
                Assert.Same(cursor.DocStore, IdeLanguageTools.TryGetDocumentStore());
        }
        finally
        {
            CitizenRouteHost.SessionResolver = prior;
        }
    }

    [Fact]
    public async Task Parallel_tenant_stage_cycle_reads_isolated_witdb()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CDP_PROFILE") ?? "default",
                "default",
                StringComparison.OrdinalIgnoreCase))
            return;

        var prior = CitizenRouteHost.SessionResolver;
        try
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
            var registry = new CdpTenantRegistry(
                kernel,
                CdpTenantSliceFactory.Create(kernel, CdpTenantKey.LegacyDefault));

            var forge = registry.Resolve(CdpTenantKey.Normalize("bridge-forge", "cdp", "main"));
            var cursor = registry.Resolve(CdpTenantKey.Normalize("bridge-cursor", "default", "main"));

            using (forge.EnterScope())
            {
                forge.Workspace.Ensure();
                var store = forge.Workspace.Require();
                var state = forge.Workspace.State;
                store.IntentUpsert(state, "Forge Epic", null);
                var leaf = store.StageUpsert(state, "Tenant TM peel", null, null, null).stage_id;
                state.ActiveStageId = leaf;
            }

            using (cursor.EnterScope())
            {
                cursor.Workspace.Ensure();
                var store = cursor.Workspace.Require();
                var state = cursor.Workspace.State;
                store.IntentUpsert(state, "CDP Platform", null);
                var leaf = store.StageUpsert(state, "Tenant TM peel", null, null, null).stage_id;
                state.ActiveStageId = leaf;
            }

            async Task<string?> ProbeFeatureTitleAsync(CdpTenantSlice slice)
            {
                using var profile = slice.EnterScope();
                using var exec = CdpTenantExecutionContext.Enter(slice);
                await Task.Delay(Random.Shared.Next(1, 15)).ConfigureAwait(false);
                if (!IdeStageCycle.TryWorkspace(out var store, out var state, out _))
                    return null;
                var snap = store.TaskManagerSnapshot(state);
                return snap.Features.Count == 0 ? null : snap.Features[0].Title;
            }

            var titles = await Task.WhenAll(
                Enumerable.Range(0, 48)
                    .Select(i => ProbeFeatureTitleAsync(i % 2 == 0 ? forge : cursor))
                    .ToArray()).ConfigureAwait(false);

            foreach (var (title, i) in titles.Select((t, idx) => (t, idx)))
            {
                if (i % 2 == 0)
                    Assert.StartsWith("Forge", title ?? "", StringComparison.Ordinal);
                else
                    Assert.StartsWith("CDP", title ?? "", StringComparison.Ordinal);
            }

            using (forge.EnterScope())
            using (CdpTenantExecutionContext.Enter(forge))
            {
                var preflight = IdeIgniteChannel.WakeChargePreflight.Probe();
                Assert.Contains("feature=Forge Epic", preflight.TmStatusLine, StringComparison.Ordinal);
            }

            using (cursor.EnterScope())
            using (CdpTenantExecutionContext.Enter(cursor))
            {
                var preflight = IdeIgniteChannel.WakeChargePreflight.Probe();
                Assert.Contains("feature=CDP Platform", preflight.TmStatusLine, StringComparison.Ordinal);
            }

            forge.Dispose();
            cursor.Dispose();
        }
        finally
        {
            CitizenRouteHost.SessionResolver = prior;
            IdeStageCycle.Unbind();
        }
    }

    [Fact]
    public void Arm_stamps_tenant_wire_when_slice_active()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CDP_PROFILE") ?? "default",
                "default",
                StringComparison.OrdinalIgnoreCase))
            return;

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
        var registry = new CdpTenantRegistry(
            kernel,
            CdpTenantSliceFactory.Create(kernel, CdpTenantKey.LegacyDefault));
        IdeIgniteArmHost.BindTenantResolver(key => registry.Resolve(key));

        var slice = registry.Resolve(CdpTenantKey.Normalize("bridge-arm-stamp", "cdp", "main"));
        try
        {
            using (slice.EnterScope())
            using (CdpTenantExecutionContext.Enter(slice))
            {
                IdeIgniteArmHost.EnsureStarted();
                var armId = "tenant-wire-test-" + Guid.NewGuid().ToString("N")[..8];
                var result = IdeIgniteArmHost.Arm(new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["id"] = JsonSerializer.SerializeToElement(armId),
                    ["task"] = JsonSerializer.SerializeToElement("tenant wire stamp"),
                    ["when"] = JsonSerializer.SerializeToElement("manual"),
                    ["force"] = JsonSerializer.SerializeToElement(true),
                });
                using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
                Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());

                var armed = IdeIgniteArmHost.Snapshot().First(a => a.Id == armId);
                Assert.Equal(slice.Key.Wire, armed.TenantWire);

                IdeIgniteArmHost.Disarm(new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["id"] = JsonSerializer.SerializeToElement(armId),
                    ["force"] = JsonSerializer.SerializeToElement(true),
                });
            }
        }
        finally
        {
            slice.Dispose();
        }
    }
}
