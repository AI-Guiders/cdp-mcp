using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CdpMcp.ArchitectureAnalyzers;

/// <summary>
/// CDPCOPE001 — CASCOPE001 spirit without Avalonia cabin: Channel/Cds/Compositor must not import Avalonia
/// (desk Surface is seats/TUI, not Views). Also covers CIDE-shaped Cockpit folders if present.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DeskCabinLayerAnalyzer : DiagnosticAnalyzer
{
    public const string AvaloniaId = "CDPCOPE001";

    static readonly DiagnosticDescriptor AvaloniaRule = new(
        AvaloniaId,
        "Cabin wire layers must not reference Avalonia",
        "Layer '{0}' (ADR 0036 / 0197) must not reference Avalonia; desk Surface is seats/TUI (found: {1})",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Channel, CDS, and compositor stay UI-framework-free; Avalonia belongs only to CIDE Views/Surface, not cdp-mcp peels.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(AvaloniaRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeUsingDirective, SyntaxKind.UsingDirective);
    }

    static void AnalyzeUsingDirective(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not UsingDirectiveSyntax u)
            return;
        if (!DeskLayerPaths.TryGetCabinWireLayer(context.Node.SyntaxTree.FilePath, out var layer))
            return;

        var name = u.Name?.ToString() ?? "";
        if (name.StartsWith("Avalonia", StringComparison.Ordinal) || name == "Avalonia")
            context.ReportDiagnostic(Diagnostic.Create(AvaloniaRule, u.GetLocation(), layer, name));
    }
}
