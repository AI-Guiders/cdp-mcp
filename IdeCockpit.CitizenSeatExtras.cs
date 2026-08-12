#nullable enable
using Cdp.Core;
using CdpMcp.IntentWorkspace;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>Fused SoftInstrumentSeatExtras for citizen host-execute (mirrors cockpit Channel/Surface).</summary>
internal static partial class IdeCockpit
{
    static readonly ShellSnap EmptyShell = new(0, 0, 0, Array.Empty<ShellTab>());

    internal static SoftInstrumentSeatExtras? TryBuildCitizenSeatExtras(
        SessionContext session,
        DocumentBufferStore docStore,
        Func<ShellHabitat?>? shellResolver,
        bool quietBandQuality = false)
    {
        ShellSnap shell;
        try
        {
            var habitat = shellResolver?.Invoke();
            shell = habitat is not null ? CollectShell(habitat.Scene()) : EmptyShell;
        }
        catch
        {
            shell = EmptyShell;
        }

        IdeStageCycle.TryWorkspace(out var workspaceStore, out var workspaceState, out _);
        var buffer = CollectBuffer(docStore.Scene());
        var probes = CollectProbeBundle(session, docStore, workspaceStore, workspaceState, git: null);
        var alertInputs = BuildAlertInputs(
            session,
            probes.Quality,
            buffer,
            probes.Debug,
            shell,
            null,
            probes.Problems,
            probes.Work,
            workspaceStore,
            workspaceState,
            probes.ChkSnap,
            quietBandQuality: quietBandQuality);

        return new SoftInstrumentSeatExtras(
            alertInputs,
            () => BuildSysOrgan(session, null, shell, buffer, probes.Debug, probes.Test, probes.Work),
            probes.ChkCtx,
            probes.ChkSnap,
            probes.GitDirty,
            probes.Problems,
            probes.TestsFailed,
            probes.Quality);
    }
}
