#nullable enable
using Cdp.Core;
using Cdp.ScriptableIde;
using TerminalMcp.Core;

namespace CdpMcp;

internal static class CdpTenantSliceFactory
{
    internal static CdpTenantSlice Create(CdpSharedKernel kernel, CdpTenantKey key)
    {
        var tenantRoot = key.ResolveTenantStateRoot();
        var session = new SessionContext();
        ApplySessionDefaults(session, kernel.Settings);

        var docStore = new DocumentBufferStore();
        var diskSyncWatch = DocumentDiskSyncWatcher.Start(docStore);
        var workspace = new WorkspaceDbHost(kernel.Settings.IntentWorkspace.DatabasePath, session);
        var shell = new ShellHabitat();
        shell.Finished += IdeShellIgnite.OnShellFinished;
        var ideSettings = new IdeSettingsHabitat(
            kernel.ConfigPath,
            kernel.Settings,
            session,
            shell,
            () => ProgramHost.ShellDefaults(session));

        return new CdpTenantSlice(
            key,
            session,
            docStore,
            workspace,
            shell,
            ideSettings,
            diskSyncWatch,
            tenantRoot);
    }

    internal static CdpTenantSlice WrapLegacy(
        CdpTenantKey key,
        SessionContext session,
        DocumentBufferStore docStore,
        WorkspaceDbHost workspace,
        ShellHabitat shell,
        IdeSettingsHabitat ideSettings,
        IDisposable? diskSyncWatch,
        string tenantStateRoot) =>
        new(
            key,
            session,
            docStore,
            workspace,
            shell,
            ideSettings,
            diskSyncWatch,
            tenantStateRoot);

    static void ApplySessionDefaults(SessionContext session, CdpSettings settings)
    {
        if (CdpEnumParse.TryParsePhase(settings.DefaultPhase, out var dp))
            session.Phase = dp;
        if (CdpEnumParse.TryParseObject(settings.DefaultObject, out var dobj))
            session.Object = dobj;
        if (IdeSettingsStore.TryGet("session.default_phase", out var up)
            && CdpEnumParse.TryParsePhase(up, out var udp))
            session.Phase = udp;
        if (IdeSettingsStore.TryGet("session.default_object", out var uo)
            && CdpEnumParse.TryParseObject(uo, out var udo))
            session.Object = udo;
    }
}
