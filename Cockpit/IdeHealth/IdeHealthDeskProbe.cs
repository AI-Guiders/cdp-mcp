#nullable enable

using System.Diagnostics;
using AIGuiders.Platform.Cockpit.DataBus;
using AIGuiders.Platform.Cockpit.DataBus.Debug;
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
        bus.Publish(new StartupProjectPathChanged(session.SolutionOrProjectPath));
    }

    static void PublishIdeHost(IDataBus bus)
    {
        var host = EnvironmentReadinessLspProbe.ProbeHostPresence();
        bus.Publish(host with
        {
            CSharpLspProcessActive = host.CSharpLspHostPresent,
            MarkdownLspProcessActive = host.MarkdownLspHostPresent,
        });
    }

    static void PublishTests(IDataBus bus)
    {
        var doc = CideTestDeskLatch.TryRead();
        if (doc is null || !doc.Active)
        {
            bus.Publish(new TestsStateChanged("", 0));
            return;
        }

        var summary = doc.Failed > 0
            ? $"{doc.Failed} failed / {doc.TotalCount}"
            : doc.TotalCount > 0
                ? $"{doc.OkCount} passed / {doc.TotalCount}"
                : doc.Pulse ?? "idle";
        bus.Publish(new TestsStateChanged(summary, doc.Failed));
    }

    static void PublishDebug(IDataBus bus)
    {
        var doc = CideDebugDeskLatch.TryRead();
        if (doc is null || !doc.ActiveDap)
        {
            bus.Publish(new DebugStateChanged(DebugSessionSnapshot.Empty));
            return;
        }

        var stack = doc.Stack?
            .Select(f => (f.Name, f.File, f.Line))
            .ToList() ?? [];
        var locals = doc.Locals?
            .Select(v => new DebugVariableRow(v.Name, v.Value, null))
            .ToList() ?? [];
        IReadOnlyList<DebugVariableRootScope> scopes = locals.Count > 0
            ? new List<DebugVariableRootScope> { new("Locals", locals) }
            : Array.Empty<DebugVariableRootScope>();

        var snap = new DebugSessionSnapshot(
            HasActiveSession: doc.ActiveDap,
            IsExecutionStopped: doc.Stopped,
            StoppedFile: stack.Count > 0 ? stack[0].File : null,
            StoppedLine: stack.Count > 0 ? stack[0].Line : 0,
            ExceptionText: null,
            Breakpoints: Array.Empty<DebugBreakpointSnapshot>(),
            StackFrames: stack,
            VariableRootScopes: scopes,
            VariablesFrameIndex: 0);
        bus.Publish(new DebugStateChanged(snap));
    }

    static void PublishGit(SessionContext session, IDataBus bus)
    {
        var root = session.ScmRoot ?? session.ProjectRoot;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            bus.Publish(new GitStateChanged("Git: —", "GIT · —"));
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
                bus.Publish(new GitStateChanged("Git: unavailable", "GIT · ?"));
                return;
            }

            var line = p.StandardOutput.ReadLine() ?? "";
            p.WaitForExit(3000);
            if (string.IsNullOrWhiteSpace(line))
            {
                bus.Publish(new GitStateChanged("Git: clean", "GIT · ok"));
                return;
            }

            var shortLine = line.Length > 24 ? string.Concat(line.AsSpan(0, 21), "…") : line;
            bus.Publish(new GitStateChanged($"Git: {line.Trim()}", shortLine.Trim()));
        }
        catch
        {
            bus.Publish(new GitStateChanged("Git: error", "GIT · err"));
        }
    }
}
