using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public class IdeArchBoardChannelTests
{
    [Fact]
    public void ToolName_is_cdp_arch() =>
        Assert.Equal("cdp_arch", IdeArchBoardChannel.ToolName);

    [Fact]
    public void AddRole_candidates_elect_edge_promote_writes_ssot()
    {
        var tmp = Directory.CreateTempSubdirectory("cdp-arch-");
        try
        {
            var session = new SessionContext { ProjectRoot = tmp.FullName };

            var addRole = IdeArchBoardChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("add_role"),
                ["role"] = JsonSerializer.SerializeToElement("ccu"),
                ["id"] = JsonSerializer.SerializeToElement("ccu-build"),
                ["note"] = JsonSerializer.SerializeToElement("BuildAsync ownership")
            });
            AssertOk(addRole);

            var addCand = IdeArchBoardChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("add_candidates"),
                ["role"] = JsonSerializer.SerializeToElement("ccu-build"),
                ["anchors"] = JsonSerializer.SerializeToElement(new[]
                {
                    "[F:IdeCockpit.Build.cs;M:BuildAsync]",
                    "IdeCockpit.cs::BuildAsync"
                })
            });
            AssertOk(addCand);

            var elect = IdeArchBoardChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("elect"),
                ["role"] = JsonSerializer.SerializeToElement("ccu-build"),
                ["candidate"] = JsonSerializer.SerializeToElement("[F:IdeCockpit.Build.cs;M:BuildAsync]")
            });
            AssertOk(elect);

            IdeArchBoardChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("add_role"),
                ["role"] = JsonSerializer.SerializeToElement("channel"),
                ["id"] = JsonSerializer.SerializeToElement("ch-build")
            });

            var edge = IdeArchBoardChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("edge"),
                ["from"] = JsonSerializer.SerializeToElement("ccu-build"),
                ["to"] = JsonSerializer.SerializeToElement("ch-build"),
                ["kind"] = JsonSerializer.SerializeToElement("feeds")
            });
            AssertOk(edge);

            var promote = IdeArchBoardChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("promote"),
                ["role"] = JsonSerializer.SerializeToElement("ccu-build")
            });
            var promoteJson = JsonSerializer.Serialize(promote);
            using (var doc = JsonDocument.Parse(promoteJson))
            {
                Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), promoteJson);
                Assert.Contains("plan only", doc.RootElement.GetProperty("pulse").GetString());
            }

            var latest = Path.Combine(tmp.FullName, ".cdp", "arch-board", "LATEST.json");
            Assert.True(File.Exists(latest), latest);
            var boardText = File.ReadAllText(latest);
            Assert.Contains("ccu-build", boardText);
            Assert.Contains("BuildAsync", boardText);
            Assert.Contains("promoted", boardText);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Bare_path_candidate_is_label_only_until_wire()
    {
        var tmp = Directory.CreateTempSubdirectory("cdp-arch-bare-");
        try
        {
            var session = new SessionContext { ProjectRoot = tmp.FullName };
            IdeArchBoardChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("add_role"),
                ["role"] = JsonSerializer.SerializeToElement("surface")
            });
            var add = IdeArchBoardChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("add_candidates"),
                ["role"] = JsonSerializer.SerializeToElement("surface"),
                ["anchors"] = JsonSerializer.SerializeToElement("JustALabel")
            });
            AssertOk(add);
            var latest = File.ReadAllText(Path.Combine(tmp.FullName, ".cdp", "arch-board", "LATEST.json"));
            Assert.Contains("JustALabel", latest);
            Assert.DoesNotContain("[F:JustALabel", latest);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Promote_without_role_uses_focus_after_elect()
    {
        var tmp = Directory.CreateTempSubdirectory("cdp-arch-focus-");
        try
        {
            var session = new SessionContext { ProjectRoot = tmp.FullName };
            IdeArchBoardChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("add_role"),
                ["role"] = JsonSerializer.SerializeToElement("ids"),
                ["id"] = JsonSerializer.SerializeToElement("ids-overlay")
            });
            IdeArchBoardChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("add_candidates"),
                ["role"] = JsonSerializer.SerializeToElement("ids-overlay"),
                ["anchors"] = JsonSerializer.SerializeToElement("IdeDisplay/Palette.cs::Compose")
            });
            AssertOk(IdeArchBoardChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("elect"),
                ["role"] = JsonSerializer.SerializeToElement("ids-overlay"),
                ["candidate"] = JsonSerializer.SerializeToElement("Compose")
            }));

            var promote = IdeArchBoardChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("promote")
            });
            var json = JsonSerializer.Serialize(promote);
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), json);
            Assert.Equal("ids-overlay", doc.RootElement.GetProperty("focus_role_id").GetString());
            Assert.Contains("plan only", doc.RootElement.GetProperty("pulse").GetString());

            var board = doc.RootElement.GetProperty("board").GetProperty("roles")[0];
            Assert.True(board.TryGetProperty("id", out _), "roles[].id snake_case");
            Assert.False(board.TryGetProperty("Id", out _), "no PascalCase Id");
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void AsBuilt_cide_profile_writes_AS_BUILT_leaves_plan()
    {
        var tmp = Directory.CreateTempSubdirectory("cdp-arch-cide-");
        try
        {
            var root = tmp.FullName;
            Touch(root, "Cockpit/Channels/IChannel.cs");
            Touch(root, "Cockpit/Composition/ISurfaceCompositor.cs");
            Touch(root, "Cockpit/Cds/ICdsRouter.cs");
            Touch(root, "Cockpit/ComputingUnits/ICockpitComputeUnit.cs");
            Touch(root, "IdeDisplay/IIdsSurfaceCompositor.cs");
            Touch(root, "Cockpit/Surface/UiLayoutSnapshot.cs");
            Touch(root, "Cockpit/DataBus/IDataBus.cs");
            Touch(root, "Cockpit/Composition/CockpitInstrumentDescriptor.cs");
            Touch(root, "Services/BuildLogIngestion.cs");

            var session = new SessionContext { ProjectRoot = root };
            IdeArchBoardChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("add_role"),
                ["role"] = JsonSerializer.SerializeToElement("ccu"),
                ["id"] = JsonSerializer.SerializeToElement("plan-only")
            });

            var built = IdeArchBoardChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("as_built")
            });
            var json = JsonSerializer.Serialize(built);
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), json);
            Assert.Equal("as_built", doc.RootElement.GetProperty("mode").GetString());
            Assert.Equal("cide", doc.RootElement.GetProperty("profile").GetString());
            Assert.True(doc.RootElement.GetProperty("board").GetProperty("roles").GetArrayLength() >= 8, json);
            var roles = doc.RootElement.GetProperty("board").GetProperty("roles");
            var hasDatabus = false;
            foreach (var r in roles.EnumerateArray())
            {
                if (r.GetProperty("role").GetString() == "databus")
                {
                    hasDatabus = true;
                    break;
                }
            }

            Assert.True(hasDatabus, json);

            Assert.True(File.Exists(Path.Combine(root, ".cdp", "arch-board", "AS_BUILT.json")));
            Assert.True(File.Exists(Path.Combine(root, ".cdp", "arch-board", "LATEST.json")));

            var plan = IdeArchBoardChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("scene")
            });
            using var planDoc = JsonDocument.Parse(JsonSerializer.Serialize(plan));
            var planRoles = planDoc.RootElement.GetProperty("board").GetProperty("roles");
            Assert.Equal(1, planRoles.GetArrayLength());
            Assert.Equal("plan-only", planRoles[0].GetProperty("id").GetString());
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void AsBuilt_cdp_desk_profile_anchors_BuildAsync()
    {
        var tmp = Directory.CreateTempSubdirectory("cdp-arch-desk-");
        try
        {
            var root = tmp.FullName;
            File.WriteAllText(Path.Combine(root, "IdeCockpit.cs"), "// stub\n");
            File.WriteAllText(Path.Combine(root, "IdeCockpit.Build.cs"), "class X { void BuildAsync() {} }\n");
            File.WriteAllText(Path.Combine(root, "IdeCockpit.Channel.cs"), "class X { void PeekDeferredSoftWants() {} }\n");
            File.WriteAllText(Path.Combine(root, "IdeCockpit.Cds.cs"), "class X { void NormalizeAttentionRouting() {} }\n");
            File.WriteAllText(Path.Combine(root, "IdeCockpit.Ids.cs"), "class X { void SearchFeatures() {} }\n");
            File.WriteAllText(Path.Combine(root, "IdeCockpit.Compositor.cs"), "class X { void ComposeSeatsSurface() {} }\n");
            File.WriteAllText(Path.Combine(root, "IdeCockpit.Surface.cs"), "class X { void BuildSeatsDeskSurfaceAsync() {} }\n");

            var session = new SessionContext { ProjectRoot = root };
            var built = IdeArchBoardChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("as_built")
            });
            var json = JsonSerializer.Serialize(built);
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), json);
            Assert.Equal("cdp_desk", doc.RootElement.GetProperty("profile").GetString());

            var roles = doc.RootElement.GetProperty("board").GetProperty("roles");
            Assert.True(roles.GetArrayLength() >= 9, json);
            JsonElement? dal = null;
            JsonElement? ccu = null;
            foreach (var r in roles.EnumerateArray())
            {
                var id = r.GetProperty("id").GetString();
                if (id == "dal-gap") dal = r;
                if (id == "ccu-build") ccu = r;
            }

            Assert.True(dal.HasValue, json);
            Assert.Equal("open", dal.Value.GetProperty("status").GetString());
            Assert.Contains("GAP", dal.Value.GetProperty("note").GetString(), StringComparison.Ordinal);

            Assert.True(ccu.HasValue, json);
            var anchor = ccu.Value.GetProperty("candidates")[0].GetProperty("anchor").GetString();
            Assert.Contains("BuildAsync", anchor, StringComparison.Ordinal);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { /* ignore */ }
        }
    }

    static void Touch(string root, string rel)
    {
        var full = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, $"// {rel}\n");
    }

    static void AssertOk(object result)
    {
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), json);
    }
}
