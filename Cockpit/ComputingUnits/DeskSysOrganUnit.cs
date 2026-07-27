#nullable enable
namespace CdpMcp.Cockpit.ComputingUnits;

/// <summary>CCU: SYS soft-organ board (legacy mfd=sys pulse card).</summary>
public sealed class DeskSysOrganUnit : ICockpitComputeUnit
{
    public readonly record struct Input(
        string? ProjectRoot,
        string OpsPulse,
        string GitPulse,
        int BufferCount,
        int BufferDirty,
        int BufferDiskChanged,
        int ShellTabCount,
        int ShellRunning,
        int ShellFailed,
        bool DebugActiveDap,
        bool DebugStopped,
        int DebugBreakpointCount,
        bool TestAvailable,
        string? TestReason,
        bool? TestLastRun,
        bool TestSuccess,
        int TestPassed,
        int TestTotal,
        string? WorkPulse);

    public object Build(in Input input)
    {
        var pulse = $"{input.OpsPulse} · {input.GitPulse} · buf={input.BufferCount} dirty={input.BufferDirty}";
        return new
        {
            ok = true,
            go = "sys",
            schema = "sys_organ/v0",
            pulse,
            ops = input.OpsPulse,
            title = "SYS",
            project = input.ProjectRoot is null ? "no_project — cdp_open" : input.ProjectRoot,
            git = input.GitPulse,
            shell = $"tabs={input.ShellTabCount} running={input.ShellRunning} failed={input.ShellFailed}",
            buffer = $"open={input.BufferCount} dirty={input.BufferDirty} disk_changed={input.BufferDiskChanged}",
            debug = input.DebugActiveDap
                ? $"dap stopped={input.DebugStopped} bp={input.DebugBreakpointCount}"
                : $"idle bp={input.DebugBreakpointCount}",
            test = input.TestAvailable
                ? input.TestLastRun is null
                    ? "no last_run — go=test"
                    : $"last {(input.TestSuccess ? "ok" : "FAIL")} {input.TestPassed}/{input.TestTotal}"
                : input.TestReason,
            work = input.WorkPulse ?? "no plan",
            hint = "Soft organ (legacy mfd=sys). Slim status already in view.banner/board."
        };
    }
}
