#nullable enable
using Cdp.Core;
using Cdp.ScriptableIde;
using CdpMcp.IntentWorkspace;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>Resolve per-tenant deps during tool dispatch (ADR-0200).</summary>
internal static class CdpTenantDispatch
{
    static CdpTenantSlice? Slice => CdpTenantExecutionContext.CurrentSlice;

    public static SessionContext Session(ProgramHostDeps deps) =>
        Slice?.Session ?? deps.Session;

    public static DocumentBufferStore DocStore(ProgramHostDeps deps) =>
        Slice?.DocStore ?? deps.DocStore;

    public static ShellHabitat ShellHabitat(ProgramHostDeps deps) =>
        Slice?.Shell ?? deps.ShellHabitat;

    public static IdeSettingsHabitat IdeSettings(ProgramHostDeps deps) =>
        Slice?.IdeSettings ?? deps.IdeSettings;

    public static IntentWorkspaceStore? WorkspaceStore(ProgramHostDeps deps)
    {
        var slice = Slice;
        if (slice is null)
            return deps.WorkspaceStore;
        slice.Workspace.Ensure();
        return slice.Workspace.Store;
    }

    public static IntentWorkspaceState WorkspaceState(ProgramHostDeps deps)
    {
        var slice = Slice;
        return slice is null ? deps.WorkspaceState : slice.Workspace.State;
    }

    public static string WorkspaceDbPath(ProgramHostDeps deps)
    {
        var slice = Slice;
        return slice is null ? deps.WorkspaceDbPath : slice.Workspace.DatabasePath;
    }

    public static Func<IntentWorkspaceStore> RequireWorkspace(ProgramHostDeps deps)
    {
        var slice = Slice;
        return slice is null
            ? deps.RequireWorkspace
            : () => slice.Workspace.Require();
    }

    public static Action EnsureWorkspaceDb(ProgramHostDeps deps)
    {
        var slice = Slice;
        return slice is null ? deps.EnsureWorkspaceDb : slice.Workspace.Ensure;
    }
}
