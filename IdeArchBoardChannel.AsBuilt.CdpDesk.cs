#nullable enable
using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>cdp_desk profile builder for <c>op=as_built</c> (IdeCockpit peels).</summary>
internal static partial class IdeArchBoardChannel
{
    static BoardDoc BuildCdpDeskAsBuilt(string root, string name)
    {
        var doc = NewAsBuiltDoc(name, "cdp_desk");
        var ccu = AddSeedRole(doc, "ccu-core", "ccu", "ComputingUnits + Build/Loci peel (ADR 0097)", root,
            [("Cockpit/ComputingUnits/ICockpitComputeUnit.cs", (string?)null),
             ("Cockpit/ComputingUnits/AttentionRoutingUnit.cs", "Compute"),
             ("Cockpit/ComputingUnits/DeskDetailUnit.cs", "Compute"),
             ("Cockpit/ComputingUnits/WorldSceneGoUnit.cs", "Compute"),
             ("Cockpit/ComputingUnits/FocusLocusUnit.cs", "Build"),
             ("Cockpit/ComputingUnits/DeskLociBuildUnit.cs", "Build"),
             ("Cockpit/ComputingUnits/DeskNextBuildUnit.cs", "Build"),
             ("Cockpit/ComputingUnits/DeskSniperLocusUnit.cs", "TryBuild"),
             ("Cockpit/ComputingUnits/DeskSysOrganUnit.cs", "Build"),
             ("Cockpit/ComputingUnits/GoVerbsCatalogUnit.cs", "Merge"),
             ("Cockpit/ComputingUnits/GoResultSlimUnit.cs", "Slim"),
             ("Cockpit/ComputingUnits/OrganJsonPulseUnit.cs", "FromJson"),
             ("IdeCockpit.Build.cs", "BuildAsync"),
             ("IdeCockpit.Build.Ingress.cs", "PrepareBuildIngress"),
             ("IdeCockpit.Build.Nav.cs", "BuildDeskNavigation"),
             ("IdeCockpit.Build.WorldGo.cs", "ApplyWorldOrGoAsync"),
             ("IdeCockpit.Build.LegacyTiles.cs", "BuildLegacyTilesDeskAsync"),
             ("IdeCockpit.Loci.cs", "BuildLoci"),
             ("IdeCockpit.Next.cs", "BuildNext")])
            ?? AddSeedRole(doc, "ccu-build", "ccu", "IdeCockpit.Build peel", root,
                [("IdeCockpit.Build.cs", "BuildAsync")]);
        var ch = AddSeedRole(doc, "ch-core", "channel", "IChannel + DeferredSoftOrgan (ADR 0036)", root,
            [("Cockpit/Channels/IChannel.cs", (string?)null),
             ("Cockpit/Channels/DeferredSoftOrganChannel.cs", "Peek"),
             ("IdeCockpit.Channel.cs", "PeekDeferredSoftWants"),
             ("IdeCockpit.Channel.cs", "ApplyDeferredSoftOrgans")])
            ?? AddSeedRole(doc, "ch-desk", "channel", "IdeCockpit.Channel peel", root,
                [("IdeCockpit.Channel.cs", "PeekDeferredSoftWants"),
                 ("IdeCockpit.Channel.cs", "ApplyDeferredSoftOrgans")]);
        var cds = AddSeedRole(doc, "cds-core", "cds", "ICdsRouter + AttentionCdsRouter", root,
            [("Cockpit/Cds/ICdsRouter.cs", (string?)null),
             ("Cockpit/Cds/AttentionCdsRouter.cs", "Route"),
             ("Cockpit/Cds/DeskGoMapCatalog.cs", "TryGet"),
             ("IdeCockpit.Cds.cs", "NormalizeAttentionRouting")])
            ?? AddSeedRole(doc, "cds-route", "cds", "NormalizeAttentionRouting / ResolveDeskDetail", root,
                [("IdeCockpit.Cds.cs", "NormalizeAttentionRouting"),
                 ("IdeCockpit.Cds.cs", "ResolveDeskDetail")]);
        var ids = AddSeedRole(doc, "ids-palette", "ids", "IIdsFeatureSearch + FeatureSearchUnit", root,
            [("Cockpit/Ids/IIdsFeatureSearch.cs", (string?)null),
             ("Cockpit/Ids/FeatureSearchUnit.cs", "Search"),
             ("IdeCockpit.Ids.cs", "SearchFeatures")])
            ?? AddSeedRole(doc, "ids-peel", "ids", "SearchFeatures peel", root,
                [("IdeCockpit.Ids.cs", "SearchFeatures")]);
        var comp = AddSeedRole(doc, "comp-core", "compositor", "ISurfaceCompositor + Seats/Tiles compositors", root,
            [("Cockpit/Composition/ISurfaceCompositor.cs", (string?)null),
             ("Cockpit/Composition/SeatsSurfaceCompositor.cs", "Compose"),
             ("Cockpit/Composition/TilesSurfaceCompositor.cs", "Compose"),
             ("IdeCockpit.Compositor.cs", "ComposeSeatsSurface")])
            ?? AddSeedRole(doc, "comp-cockpit", "compositor", "IdeCockpit.Compositor peel", root,
                [("IdeCockpit.Compositor.cs", "ComposeSeatsSurface"),
                 ("IdeCockpit.Compositor.cs", "ComposeTilesSurface")]);
        var surf = AddSeedRole(doc, "surf-seats", "surface", "SeatsDetailGate + SoftOrganAlias + SoftOrgans", root,
            [("Cockpit/Surface/SeatsDetailGateUnit.cs", "Compute"),
             ("Cockpit/Surface/SeatOrganArgsSanitizer.cs", "Sanitize"),
             ("Cockpit/Surface/SeatFullPaneMatchUnit.cs", "Matches"),
             ("Cockpit/Surface/SeatOrganPanePresenter.cs", "FullOr"),
             ("Cockpit/Surface/SoftOrganAliasCatalog.cs", "TryResolve"),
             ("Cockpit/Surface/DeskPinAliasCatalog.cs", "Canonical"),
             ("Cockpit/Surface/DeskLayoutPresetCatalog.cs", "TryGet"),
             ("Cockpit/Surface/DeskPlaceableOrganUnit.cs", "IsPlaceable"),
             ("Cockpit/Surface/SoftOrganBoardMetaCatalog.cs", "Require"),
             ("Cockpit/Surface/ISoftOrganBoard.cs", "Build"),
             ("IdeSoftOrganBoard.cs", "Build"),
             ("Cockpit/Surface/SoftOrganPresentMode.cs", null),
             ("Cockpit/Surface/SeatFallbackSnapUnit.cs", "Classify"),
             ("Cockpit/Surface/SeatOrganPanePresenter.cs", "Present"),
             ("Cockpit/Surface/WorldSnapPaneUnit.cs", "Build"),
             ("Cockpit/Surface/EditorSnapPaneUnit.cs", "Build"),
             ("Cockpit/ComputingUnits/OrganJsonPulseUnit.cs", "FromJson"),
             ("IdeCockpit.Surface.cs", "BuildSeatsDeskSurfaceAsync"),
             ("IdeCockpit.Surface.SoftOrgans.cs", "ResolveSeatOrganPaneAsync"),
             ("IdeCockpit.Dispatch.cs", "DispatchGoAsync"),
             ("Cockpit/Surface/SoftOrganKind.cs", "Ps1Desk"),
             ("Ps1Scene.cs", "Pulse"),
             ("IdeCockpitSoftDispatch.Operators.cs", "TryDispatchPs1"),
             ("Cockpit/Surface/SoftOrganKind.cs", "Report"),
             ("ScriptScene.cs", "Pulse"),
             ("IdeReportBoard.cs", "Handle")])
            ?? AddSeedRole(doc, "surf-peel", "surface", "BuildSeatsDeskSurfaceAsync", root,
                [("IdeCockpit.Surface.cs", "BuildSeatsDeskSurfaceAsync")]);

        var transport = AddSeedRole(doc, "transport-core", "transport", "DeskIngestionBus Channel<T> (ADR 0094)", root,
            ["Cockpit/Transport/DeskIngestionBus.cs",
             "IdeCockpit.Transport.cs"])
            ?? AddGapRole(doc, "transport-gap", "transport",
                "GAP — no IdeCockpit.Transport peel (CIDE ADR 0094)");
        var dal = AddSeedRole(doc, "dal-core", "dal", "DataAcquisition + toolchain (ADR 0102/0198)", root,
            ["Cockpit/DataAcquisition/ToolchainPathProbe.cs",
             "IdeToolchainChannel.cs"])
            ?? AddGapRole(doc, "dal-gap", "dal",
                "GAP — DAL missing; toolchain ensure hangs here (ADR 0198)");
        var databus = AddSeedRole(doc, "databus-core", "databus", "IDE DataBus + DeskDataBusHost (ADR 0099)", root,
            ["Cockpit/DataBus/IDataBus.cs",
             "Cockpit/DataBus/InMemoryDataBus.cs",
             "Cockpit/DataBus/DeskDataBusHost.cs"])
            ?? AddGapRole(doc, "databus-gap", "databus",
                "GAP — no IDataBus in cdp-mcp desk (CIDE ADR 0099)");
        var instrument = AddSeedRole(doc, "instr-core", "instrument", "DeskInstrumentMountRegistry (ADR 0063)", root,
            ["Cockpit/Instrument/DeskInstrumentMountRegistry.cs",
             "IdeCockpit.Instrument.cs"])
            ?? AddGapRole(doc, "instr-gap", "instrument",
                "GAP — no instrument deck peel in desk profile");

        Wire(doc, transport, ccu, "feeds");
        Wire(doc, dal, ccu, "feeds");
        Wire(doc, ccu, ch, "feeds");
        Wire(doc, ch, cds, "feeds");
        Wire(doc, cds, comp, "projects");
        Wire(doc, comp, surf, "projects");
        Wire(doc, databus, ch, "wires");
        Wire(doc, instrument, surf, "mounts");
        _ = ids;
        doc.FocusRoleId = dal.Id;
        return doc;
    }
}
