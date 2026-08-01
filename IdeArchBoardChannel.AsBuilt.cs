#nullable enable
using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>
/// <c>op=as_built</c> — scan open <see cref="SessionContext.ProjectRoot"/> and write
/// <c>.cdp/arch-board/AS_BUILT.json</c> (plan board <c>LATEST.json</c> stays untouched).
/// Profiles: <c>cide</c> (Cockpit+IdeDisplay), <c>cdp_desk</c> (IdeCockpit peels).
/// </summary>
internal static partial class IdeArchBoardChannel
{
    static object AsBuilt(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var root = session.ProjectRoot ?? session.ScmRoot;
        if (root is null or { Length: 0 })
            return Err("project_required", "cdp_open a project first — as_built scans that ProjectRoot");

        root = Path.GetFullPath(root);
        var profile = ResolveArchProfile(root, args);
        var name = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var doc = profile switch
        {
            "cide" => BuildCideAsBuilt(root, name),
            "cdp_desk" => BuildCdpDeskAsBuilt(root, name),
            _ => BuildUnknownAsBuilt(root, name, profile)
        };

        SaveAsBuilt(session, doc);
        return OkCard(
            session,
            doc,
            "as_built",
            pulse: $"as_built · {profile} · {doc.Roles.Count} roles · {doc.Edges.Count} edges · {name}",
            boardPath: AsBuiltPath(session),
            primaryGo: new
            {
                go = GoName,
                label = "Scene as-built",
                why = "op=scene view=as_built"
            });
    }

    /// <summary>Explicit <c>profile=cide|cdp_desk</c> wins; else auto-detect from tree.</summary>
    static string ResolveArchProfile(string root, IReadOnlyDictionary<string, JsonElement> args)
    {
        var forced = (Opt(args, "profile") ?? Opt(args, "arch_profile") ?? "").Trim().ToLowerInvariant();
        if (forced is "cide" or "cdp_desk" or "unknown")
            return forced;
        return DetectArchProfile(root);
    }

    static string DetectArchProfile(string root)
    {
        // Desk peels win even when Cockpit/Channels+Cds exist (hybrid cdp-mcp).
        // Otherwise Cds alone auto-picks cide and silently drops DeskIngestionBus / Instrument.
        if (File.Exists(Path.Combine(root, "IdeCockpit.cs")) &&
            File.Exists(Path.Combine(root, "IdeCockpit.Build.cs")))
            return "cdp_desk";

        var cockpit = Path.Combine(root, "Cockpit");
        var hasCockpit =
            Directory.Exists(Path.Combine(cockpit, "Channels")) &&
            Directory.Exists(Path.Combine(cockpit, "Composition"));
        var hasIds =
            Directory.Exists(Path.Combine(root, "IdeDisplay")) ||
            Directory.Exists(Path.Combine(cockpit, "Cds"));
        if (hasCockpit && hasIds)
            return "cide";

        return "unknown";
    }

    static BoardDoc BuildCideAsBuilt(string root, string name)
    {
        var doc = NewAsBuiltDoc(name, "cide");
        var transport = AddSeedRole(doc, "transport-ingest", "transport", "Ingestion / stream transport (ADR 0094)", root,
            ["Services/BuildLogIngestion.cs",
             "Features/Intercom/Transport/IntercomTransportIngest.cs",
             "Cockpit/Transport/DeskIngestionBus.cs",
             "IdeCockpit.Transport.cs"]);
        var ccu = AddSeedRole(doc, "ccu-core", "ccu", "ComputingUnits (ADR 0097)", root,
            ["Cockpit/ComputingUnits/ICockpitComputeUnit.cs",
             "Cockpit/ComputingUnits/EnvironmentReadiness/EnvironmentReadinessSnapshotUnit.cs",
             "Cockpit/ComputingUnits/IdeHealth/IdeHealthFormattingUnit.cs"]);
        var databus = AddSeedRole(doc, "databus-core", "databus", "IDE DataBus typed events (ADR 0099)", root,
            ["Cockpit/DataBus/IDataBus.cs",
             "Cockpit/DataBus/InMemoryDataBus.cs",
             "Cockpit/DataBus/DataBusEventPolicy.cs"]);
        var ch = AddSeedRole(doc, "ch-core", "channel", "Channels (IChannel)", root,
            ["Cockpit/Channels/IChannel.cs",
             "Cockpit/Channels/EnvironmentReadiness/EnvironmentReadinessChannel.cs",
             "Cockpit/Channels/TraceFlow/CodeFlowTraceChannel.cs"]);
        var cds = AddSeedRole(doc, "cds-core", "cds", "CDS — куда в кабине", root,
            ["Cockpit/Cds/ICdsRouter.cs",
             "Cockpit/Cds/CockpitSurfaceSnapshotBuilder.cs",
             "Cockpit/Cds/TraceFlowCdsRouter.cs"]);
        var ids = AddSeedRole(doc, "ids-core", "ids", "IDS — IdeDisplay overlays (orthogonal to CDS)", root,
            ["IdeDisplay/IIdsSurfaceCompositor.cs",
             "IdeDisplay/CommandPalette/CommandPaletteSurfaceCompositor.cs",
             "IdeDisplay/CockpitCommandLine/CockpitCommandLineSurfaceCompositor.cs"]);
        var comp = AddSeedRole(doc, "comp-core", "compositor", "Composition → surface", root,
            ["Cockpit/Composition/ISurfaceCompositor.cs",
             "Cockpit/Composition/HostSurface/MainWindowHostSurfaceCompositor.cs",
             "Cockpit/Composition/Shell/MainWindowShellSurfaceCompositor.cs"]);
        var instrument = AddSeedRole(doc, "instr-core", "instrument", "Instrument deck / descriptor (ADR 0047/0063)", root,
            ["Cockpit/Composition/CockpitInstrumentDescriptor.cs",
             "Cockpit/InstrumentDeckDescriptor.cs",
             "Cockpit/Composition/IdeHealth/IdeHealthInstrumentDeck.cs",
             "Cockpit/Instrument/DeskInstrumentMountRegistry.cs",
             "IdeCockpit.Instrument.cs"]);
        var surf = AddSeedRole(doc, "surf-core", "surface", "Surface snapshot / mounts", root,
            ["Cockpit/Surface/UiLayoutSnapshot.cs",
             "Cockpit/Surface/MainWindowInstrumentMountRegistry.cs"]);
        // DAL: Cockpit/DataAcquisition locus (ADR 0102); toolchain ensure hangs here (ADR 0198)
        var dal = AddSeedRole(doc, "dal-core", "dal", "DataAcquisition (ADR 0102)", root,
            ["Cockpit/DataAcquisition/ToolchainPathProbe.cs",
             "IdeToolchainChannel.cs"])
            ?? AddGapRole(doc, "dal-gap", "dal",
                "missing locus — ADR 0102; toolchain ensure hangs here (ADR 0198)");

        // transport → CCU → channel → CDS → compositor → surface
        Wire(doc, transport, ccu, "feeds");
        Wire(doc, dal, ccu, "feeds");
        Wire(doc, ccu, ch, "feeds");
        Wire(doc, ch, cds, "feeds");
        Wire(doc, cds, comp, "projects");
        Wire(doc, comp, surf, "projects");
        // DataBus: domain events wire into channel/projections (orthogonal to CDS routing)
        Wire(doc, databus, ch, "wires");
        // Instruments mount onto surface slots
        Wire(doc, instrument, surf, "mounts");
        // ids orthogonal — no CDS→IDS edge
        _ = ids;
        doc.FocusRoleId = databus?.Id ?? ccu?.Id;
        return doc;
    }


    static BoardDoc BuildUnknownAsBuilt(string root, string name, string profile)
    {
        var doc = NewAsBuiltDoc(name, profile);
        doc.Title = $"as-built · {name} · unknown profile";
        // Best-effort: any IdeCockpit* or Cockpit folder note
        var note = Directory.Exists(Path.Combine(root, "Cockpit"))
            ? "Cockpit/ present but Channels+Composition incomplete — not CIDE profile"
            : "No CIDE Cockpit/ or IdeCockpit peels — add_role manually or extend DetectArchProfile";
        doc.Roles.Add(new RoleSlot
        {
            Id = "hint",
            Role = "instrument",
            Status = "open",
            Note = note
        });
        return doc;
    }

    static BoardDoc NewAsBuiltDoc(string name, string profile) => new()
    {
        Title = $"as-built · {name}",
        Mode = "as_built",
        Profile = profile,
        UpdatedUtc = DateTimeOffset.UtcNow
    };

static RoleSlot AddGapRole(BoardDoc doc, string id, string role, string note)
    {
        var slot = new RoleSlot
        {
            Id = id,
            Role = role,
            Status = "open",
            Note = $"as_built · {note}"
        };
        doc.Roles.Add(slot);
        return slot;
    }

        static RoleSlot? AddSeedRole(
        BoardDoc doc,
        string id,
        string role,
        string note,
        string root,
        string[] relPaths)
    {
        var pairs = relPaths.Select(p => (p, (string?)null)).ToArray();
        return AddSeedRole(doc, id, role, note, root, pairs);
    }

    static RoleSlot? AddSeedRole(
        BoardDoc doc,
        string id,
        string role,
        string note,
        string root,
        (string Rel, string? Member)[] seeds)
    {
        var slot = new RoleSlot
        {
            Id = id,
            Role = role,
            Status = "open",
            Note = $"as_built · {note}"
        };

        foreach (var (rel, member) in seeds)
        {
            var full = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full))
                continue;

            var relNorm = RelUnderRoot(root, full);
            var symbol = member ?? Path.GetFileNameWithoutExtension(full);
            var wire = member is { Length: > 0 }
                ? BracketLocate.Format(new BracketLocate.Span(relNorm, member, null, null))
                : BracketLocate.Format(new BracketLocate.Span(relNorm, symbol, null, null));

            var c = new Candidate
            {
                Id = ShortId("c"),
                Label = symbol,
                Anchor = wire,
                Path = relNorm,
                Symbol = symbol,
                Status = "candidate"
            };
            slot.Candidates.Add(c);
        }

        if (slot.Candidates.Count == 0)
            return null;

        // Elect first hit — as-built means "this is what's in the tree"
        var elect = slot.Candidates[0];
        elect.Status = "elected";
        slot.ElectedCandidateId = elect.Id;
        slot.Status = "promoted";
        slot.Note = $"{slot.Note} · auto-promoted";
        doc.Roles.Add(slot);
        return slot;
    }

    static void Wire(BoardDoc doc, RoleSlot? from, RoleSlot? to, string kind)
    {
        if (from is null || to is null)
            return;
        doc.Edges.Add(new BoardEdge
        {
            Id = ShortId("e"),
            FromRoleId = from.Id,
            ToRoleId = to.Id,
            Kind = kind
        });
    }

    static string RelUnderRoot(string root, string full)
    {
        var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var f = Path.GetFullPath(full);
        if (f.StartsWith(r, StringComparison.OrdinalIgnoreCase))
        {
            var rel = f[(r.Length)..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return rel.Replace('\\', '/');
        }

        return Path.GetFileName(full);
    }
}
