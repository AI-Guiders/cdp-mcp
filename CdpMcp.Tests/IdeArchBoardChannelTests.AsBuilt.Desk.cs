using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;
public partial class IdeArchBoardChannelTests
{
    [Fact]
    public void AsBuilt_cdp_desk_promotes_transport_and_instrument_when_present()
    {
        var tmp = Directory.CreateTempSubdirectory("cdp-arch-desk-peels-");
        try
        {
            var root = tmp.FullName;
            File.WriteAllText(Path.Combine(root, "IdeCockpit.cs"), "// stub\n");
            File.WriteAllText(Path.Combine(root, "IdeCockpit.Build.cs"), "class X { void BuildAsync() {} }\n");
            File.WriteAllText(Path.Combine(root, "IdeCockpit.Transport.cs"), "class X { void IngestCockpitRequest() {} void TransportPulse() {} }\n");
            File.WriteAllText(Path.Combine(root, "IdeCockpit.Instrument.cs"), "class X { void DescribeSeatsInstrumentDeck() {} void InstrumentPulse() {} }\n");
            Touch(root, "Cockpit/Transport/DeskIngestionBus.cs");
            Touch(root, "Cockpit/Instrument/DeskInstrumentMountRegistry.cs");
            var session = new SessionContext
            {
                ProjectRoot = root
            };
            var built = IdeArchBoardChannel.Handle(session, new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("as_built") });
            var json = JsonSerializer.Serialize(built);
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), json);
            var roles = doc.RootElement.GetProperty("board").GetProperty("roles");
            JsonElement? transport = null;
            JsonElement? instrument = null;
            foreach (var r in roles.EnumerateArray())
            {
                var id = r.GetProperty("id").GetString();
                if (id is "transport-core" or "transport-ingest")
                    transport = r;
                if (id is "instr-core" or "instr-seats")
                    instrument = r;
            }

            Assert.True(transport.HasValue, json);
            Assert.Equal("promoted", transport.Value.GetProperty("status").GetString());
            Assert.True(instrument.HasValue, json);
            Assert.Equal("promoted", instrument.Value.GetProperty("status").GetString());
        }
        finally
        {
            try
            {
                tmp.Delete(recursive: true);
            }
            catch
            { /* ignore */
            }
        }
    }

    [Fact]
    public void AsBuilt_hybrid_cockpit_cds_plus_IdeCockpit_prefers_cdp_desk()
    {
        // Regression: Cds alone used to force cide and drop DeskIngestionBus/Instrument peels.
        var tmp = Directory.CreateTempSubdirectory("cdp-arch-hybrid-");
        try
        {
            var root = tmp.FullName;
            File.WriteAllText(Path.Combine(root, "IdeCockpit.cs"), "// stub\n");
            File.WriteAllText(Path.Combine(root, "IdeCockpit.Build.cs"), "class X { void BuildAsync() {} }\n");
            Touch(root, "Cockpit/Channels/IChannel.cs");
            Touch(root, "Cockpit/Composition/ISurfaceCompositor.cs");
            Touch(root, "Cockpit/Cds/ICdsRouter.cs");
            Touch(root, "Cockpit/Transport/DeskIngestionBus.cs");
            Touch(root, "Cockpit/Instrument/DeskInstrumentMountRegistry.cs");
            var session = new SessionContext
            {
                ProjectRoot = root
            };
            var built = IdeArchBoardChannel.Handle(session, new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("as_built") });
            var json = JsonSerializer.Serialize(built);
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), json);
            Assert.Equal("cdp_desk", doc.RootElement.GetProperty("profile").GetString());
            var roles = doc.RootElement.GetProperty("board").GetProperty("roles");
            JsonElement? transport = null;
            JsonElement? instrument = null;
            foreach (var r in roles.EnumerateArray())
            {
                var id = r.GetProperty("id").GetString();
                if (id is "transport-core" or "transport-ingest")
                    transport = r;
                if (id is "instr-core" or "instr-seats")
                    instrument = r;
            }

            Assert.True(transport.HasValue, json);
            Assert.Equal("promoted", transport.Value.GetProperty("status").GetString());
            Assert.True(instrument.HasValue, json);
            Assert.Equal("promoted", instrument.Value.GetProperty("status").GetString());
        }
        finally
        {
            try
            {
                tmp.Delete(recursive: true);
            }
            catch
            { /* ignore */
            }
        }
    }
}