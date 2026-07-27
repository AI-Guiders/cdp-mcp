using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CdpMcp.ArchitectureAnalyzers;

/// <summary>
/// CDPCOPE020 — CASCOPE020 spirit: Channel/Cds/Compositor/CCU peels must not do external I/O;
/// acquisition lives in <c>Cockpit/DataAcquisition</c> (ADR 0102 / 0198).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DeskIoBoundaryAnalyzer : DiagnosticAnalyzer
{
    public const string ForbiddenExternalAccessId = "CDPCOPE020";

    static readonly ImmutableHashSet<string> ForbiddenExternalTypeNames =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "File",
            "Directory",
            "FileInfo",
            "DirectoryInfo",
            "Process",
            "HttpClient");

    static readonly DiagnosticDescriptor ForbiddenExternalAccessRule = new(
        ForbiddenExternalAccessId,
        "Wire peels must not acquire external data directly",
        "In '{0}' direct access via '{1}' is forbidden — put I/O in Cockpit/DataAcquisition (ADR 0102 / CDPCOPE)",
        "Architecture",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Channel/CDS/compositor/CCU consume prepared inputs; fs/process/http/json parse belongs in DAL.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(ForbiddenExternalAccessRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
    }

    static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        if (!DeskLayerPaths.TryGetIoRestrictedLayer(context.Node.SyntaxTree.FilePath, out var layer))
            return;
        if (context.Node is not InvocationExpressionSyntax invocation)
            return;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        if (memberAccess.Expression is IdentifierNameSyntax id &&
            ForbiddenExternalTypeNames.Contains(id.Identifier.ValueText))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ForbiddenExternalAccessRule,
                memberAccess.GetLocation(),
                layer,
                id.Identifier.ValueText));
        }
    }

    static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        if (!DeskLayerPaths.TryGetIoRestrictedLayer(context.Node.SyntaxTree.FilePath, out var layer))
            return;
        if (context.Node is not ObjectCreationExpressionSyntax creation)
            return;
        var typeName = ExtractSimpleTypeName(creation.Type);
        if (typeName is null || !ForbiddenExternalTypeNames.Contains(typeName))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            ForbiddenExternalAccessRule,
            creation.GetLocation(),
            layer,
            typeName));
    }

    static string? ExtractSimpleTypeName(TypeSyntax typeSyntax) =>
        typeSyntax switch
        {
            IdentifierNameSyntax i => i.Identifier.ValueText,
            QualifiedNameSyntax q => q.Right.Identifier.ValueText,
            GenericNameSyntax g => g.Identifier.ValueText,
            AliasQualifiedNameSyntax a => a.Name.Identifier.ValueText,
            _ => null
        };
}
