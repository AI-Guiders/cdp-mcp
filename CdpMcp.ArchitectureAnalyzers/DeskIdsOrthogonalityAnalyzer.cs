using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CdpMcp.ArchitectureAnalyzers;

/// <summary>
/// CDPCOPE016 — IDS peel stays orthogonal and UI-framework-free (CASCOPE013/014 spirit, no Avalonia desk).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DeskIdsOrthogonalityAnalyzer : DiagnosticAnalyzer
{
    public const string AvaloniaInIdsId = "CDPCOPE016";

    static readonly DiagnosticDescriptor AvaloniaInIdsRule = new(
        AvaloniaInIdsId,
        "IDS peel must not reference Avalonia",
        "IdeCockpit.Ids / IdeDisplay must not reference Avalonia (found: {0}); IDS is orthogonal desk overlay, not Avalonia Views",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(AvaloniaInIdsRule);

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
        if (!DeskLayerPaths.IsIdsLayer(context.Node.SyntaxTree.FilePath))
            return;

        var name = u.Name?.ToString() ?? "";
        if (name.StartsWith("Avalonia", StringComparison.Ordinal) || name == "Avalonia")
            context.ReportDiagnostic(Diagnostic.Create(AvaloniaInIdsRule, u.GetLocation(), name));
    }
}
