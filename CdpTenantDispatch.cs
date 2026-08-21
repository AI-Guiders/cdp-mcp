#nullable enable
using Cdp.Core;
using Cdp.ScriptableIde;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>Resolve per-tenant deps during tool dispatch (ADR-0200).</summary>
internal static class CdpTenantDispatch
{
    public static SessionContext Session(ProgramHostDeps deps) =>
        CdpTenantExecutionContext.CurrentSlice?.Session ?? deps.Session;

    public static DocumentBufferStore DocStore(ProgramHostDeps deps) =>
        CdpTenantExecutionContext.CurrentSlice?.DocStore ?? deps.DocStore;

    public static IntentWorkspaceStore? WorkspaceStore(ProgramHostDeps deps)
    {
        var slice = CdpTenantExecutionContext.CurrentSlice;
        if (slice is null)
            return deps.WorkspaceStore;
        slice.Workspace.Ensure();
        return slice.Workspace.Store;
    }

    public static IntentWorkspaceState WorkspaceState(ProgramHostDeps deps)
    {
        var slice = CdpTenantExecutionContext.CurrentSlice;
        return slice is null ? deps.WorkspaceState : slice.Workspace.State;
    }

    public static string WorkspaceDbPath(ProgramHostDeps deps)
    {
        var slice = CdpTenantExecutionContext.CurrentSlice;
        return slice is null ? deps.WorkspaceDbPath : slice.Workspace.DatabasePath;
    }

    public static Func<IntentWorkspaceStore> RequireWorkspace(ProgramHostDeps deps)
    {
        var slice = CdpTenantExecutionContext.CurrentSlice;
        return slice is null
            ? deps.RequireWorkspace
            : () => slice.Workspace.Require();
    }

    public static Action EnsureWorkspaceDb(ProgramHostDeps deps)
    {
        var slice = CdpTenantExecutionContext.CurrentSlice;
        return slice is null ? deps.EnsureWorkspaceDb : slice.Workspace.Ensure;
    }
}
