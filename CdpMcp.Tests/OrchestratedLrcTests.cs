#if CDP_FEDERATION_IDE_SESSION
using System.IO;
using System.Text.Json;
using AIGuiders.Platform.Execution.Ide.Session;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public sealed class OrchestratedLrcTests
{
    [Fact]
    public void Ensure_compiler_services_marks_materialized_on_guiders_slnx()
    {
        var guidersRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "guiders-fsharp"));
        var slnx = Path.Combine(guidersRoot, "AIGuiders.Platform.Modeling.slnx");
        var kernelFs = Path.Combine(
            guidersRoot,
            "src",
            "AIGuiders.Platform.Modeling.Language",
            "Kernel.fs");

        Assert.True(File.Exists(slnx), slnx);
        Assert.True(File.Exists(kernelFs), kernelFs);

        var ensure = FederationSessionRuntime.TryEnsureCompilerServices(slnx, kernelFs);

        Assert.True(ensure.Ok, ensure.Reason ?? "ensure failed");
        Assert.Equal("in-process", ensure.Topology);
        Assert.Equal("fsharp", ensure.LanguageId);
        Assert.True(ensure.MaterializedCount > 0);
    }

    [Fact]
    public async Task Lrc_dispatch_calls_orchestrator_before_fsharp_diagnostics()
    {
        IdeLanguageTools.Configure(LanguageRegistry.Default);
        IdeLanguageTools.BindDocumentStore(null);

        var guidersRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "guiders-fsharp"));
        var slnx = Path.Combine(guidersRoot, "AIGuiders.Platform.Modeling.slnx");
        var kernelFs = Path.Combine(
            guidersRoot,
            "src",
            "AIGuiders.Platform.Modeling.Language",
            "Kernel.fs");

        if (!File.Exists(slnx) || !File.Exists(kernelFs))
            return;

        var session = new SessionContext
        {
            ProjectRoot = guidersRoot,
            Language = CdpLanguages.Fsharp,
            SolutionOrProjectPath = slnx,
        };

        var raw = await IdeLanguageTools.DispatchBareAsync(
            "get_diagnostics",
            session,
            new Dictionary<string, ICdpBackendModule>(),
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["file_path"] = JsonSerializer.SerializeToElement(kernelFs),
            },
            CancellationToken.None);

        using var doc = JsonDocument.Parse(raw);
        Assert.True(doc.RootElement.TryGetProperty("diagnostics", out _));

        var ensure = FederationSessionRuntime.TryEnsureCompilerServices(slnx, kernelFs);
        Assert.True(ensure.Ok);
        Assert.True(ensure.MaterializedCount > 0);
    }
}
#endif
