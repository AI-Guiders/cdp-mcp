#nullable enable

using System.Diagnostics;
using AIGuiders.Platform.Execution.Cockpit.DataBus;
using AIGuiders.Platform.Modeling.Cockpit.DataBus;
using Cdp.Core;
using CdpMcp.Cockpit.DataBus;
using CdpMcp.Cockpit.EnvironmentReadiness;

namespace CdpMcp.Cockpit.IdeHealth;

/// <summary>Publish IdeHealth DataBus events from CDP habitat probes (build/tests/debug/git/LSP).</summary>
internal static class IdeHealthDeskProbe
{
    public static void PublishFromHabitat(SessionContext session, IDataBus bus)
    {
        PublishGit(session, bus);
        PublishIdeHost(bus);
        PublishDebug(bus);
        PublishTests(bus);
        PublishStartup(session, bus);
    }

    static void PublishStartup(SessionContext session, IDataBus bus)
    {
        bus.Publish(new StartupProjectPathChanged { ProjectPath = session.SolutionOrProjectPath });
    }

    static void PublishIdeHost(IDataBus bus)
    {
        var host = EnvironmentReadinessLspProbe.ProbeHostPresence();
        bus.Publish(new IdeHostStateChanged
        {
            CSharpLspProcessActive = host.CSharpLspHostPresent,
            MarkdownLspProcessActive = host.MarkdownLspHostPresent,
            CSharpLspHostPresent = host.CSharpLspHostPresent,
            MarkdownLspHostPresent = host.MarkdownLspHostPresent,
        });
    }

    static void PublishTests(IDataBus bus)
    {
        var doc = CideTestDeskLatch.TryRead();
        if (doc is null || !doc.Active)
        {
            bus.Publish(new TestsStateChanged { Summary = "", ImpactedBadge = 0 });
            return;
        }

        var summary = doc.Failed > 0
            ? $"{doc.Failed} failed / {doc.TotalCount}"
            : doc.TotalCount > 0
                ? $"{doc.OkCount} passed / {doc.TotalCount}"
                : doc.Pulse ?? "idle";
        bus.Publish(new TestsStateChanged { Summary = summary, ImpactedBadge = doc.Failed });
    }

    static void PublishDebug(IDataBus bus)
    {
        var doc = CideDebugDeskLatch.TryRead();
        if (doc is null || !doc.ActiveDap)
        {
            bus.Publish(new DebugStateChanged { Snapshot = DebugSessionSnapshot.Empty });
            return;
        }

        var stack = doc.Stack?
            .Select(f => (f.Name, f.File, f.Line))
            .ToList() ?? [];
        var locals = doc.Locals?
            .Select(v => new DebugVariableRow
            {
                Name = v.Name,
                Value = v.Value,
                Type = "",
                VariablesReference = 0,
            })
            .ToList() ?? [];
        IReadOnlyList<DebugVariableRootScope> scopes = locals.Count > 0
            ? new List<DebugVariableRootScope> { new() { ScopeName = "Locals", Roots = locals.ToArray() } }
            : Array.Empty<DebugVariableRootScope>();

        var snap = new DebugSessionSnapshot
        {
            HasActiveSession = doc.ActiveDap,
            IsExecutionStopped = doc.Stopped,
            StoppedFile = stack.Count > 0 ? stack[0].File ?? "" : "",
            StoppedLine = stack.Count > 0 ? stack[0].Line : 0,
            ExceptionText = "",
            Breakpoints = Array.Empty<DebugBreakpointSnapshot>(),
            StackFrames = stack.Select(f => Tuple.Create(f.Name, f.File, f.Line)).ToArray(),
            VariableRootScopes = scopes.ToArray(),
            VariablesFrameIndex = 0,
        };
        bus.Publish(new DebugStateChanged { Snapshot = snap });
    }

    static void PublishGit(SessionContext session, IDataBus bus)
    {
        var root = session.ScmRoot ?? session.ProjectRoot;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            bus.Publish(new GitStateChanged { Line = "Git: —", CockpitShort = "GIT · —" });
            return;
        }

        try
        {
            var psi = new ProcessStartInfo("git", "-C \"" + root + "\" status -sb")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null)
            {
                bus.Publish(new GitStateChanged { Line = "Git: unavailable", CockpitShort = "GIT · ?" });
                return;
            }

            var line = p.StandardOutput.ReadLine() ?? "";
            p.WaitForExit(3000);
            if (string.IsNullOrWhiteSpace(line))
            {
                bus.Publish(new GitStateChanged { Line = "Git: clean", CockpitShort = "GIT · ok" });
                return;
            }

            var shortLine = line.Length > 24 ? string.Concat(line.AsSpan(0, 21), "…") : line;
            bus.Publish(new GitStateChanged { Line = $"Git: {line.Trim()}", CockpitShort = shortLine.Trim() });
        }
        catch
        {
            bus.Publish(new GitStateChanged { Line = "Git: error", CockpitShort = "GIT · err" });
        }
    }
}
