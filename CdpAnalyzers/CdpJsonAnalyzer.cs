using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace CdpAnalyzers;

/// <summary>
/// JSON serialization SSOT teeth (CDP): all JsonSerializerOptions instances must carry
/// the relaxed encoder (or come from CdpJson), and Utf8JsonWriter must be constructed
/// with options. Default JavaScriptEncoder escapes every non-ASCII char as \uXXXX —
/// payloads here are read by LLMs, not HTML embedders.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CdpJsonAnalyzer : DiagnosticAnalyzer
{
    public const string AdHocOptionsRuleId = "CDPJ001";
    public const string BareWriterRuleId = "CDPJ002";

    static readonly DiagnosticDescriptor AdHocOptions = new(
        AdHocOptionsRuleId,
        "JsonSerializerOptions without the SSOT relaxed encoder",
        "JsonSerializerOptions must set Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping (or come from CdpJson) — default escapes non-ASCII as \\uXXXX",
        "CdpJson",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Payloads are read by LLMs, not HTML embedders. Route through CdpJson or set the encoder explicitly.");

    static readonly DiagnosticDescriptor BareWriter = new(
        BareWriterRuleId,
        "Utf8JsonWriter constructed without options",
        "Utf8JsonWriter needs options with the SSOT relaxed encoder (CdpJson.WriterOptions[Indented]) — default escapes non-ASCII as \\uXXXX",
        "CdpJson",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Route through CdpJson.WriterOptions[Indented].");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [AdHocOptions, BareWriter];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeObjectCreation, OperationKind.ObjectCreation);
    }

    static void AnalyzeObjectCreation(OperationAnalysisContext context)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        var typeName = creation.Type.ToDisplayString();
        var filePath = context.Operation.Syntax.SyntaxTree.FilePath;

        // The SSOT modules themselves are exempt (both the CdpMcp one and the bridge mirror).
        if (filePath.EndsWith("CdpJson.cs", StringComparison.OrdinalIgnoreCase))
            return;

        if (typeName == "System.Text.Json.JsonSerializerOptions")
        {
            // Exempt when the initializer assigns Encoder explicitly.
            if (creation.Initializer is not null && creation.Syntax is ObjectCreationExpressionSyntax { Initializer: { } init })
            {
                foreach (var a in init.Expressions)
                {
                    if (a is AssignmentExpressionSyntax { Left: IdentifierNameSyntax { Identifier.ValueText: "Encoder" } })
                        return;
                }
            }

            context.ReportDiagnostic(Diagnostic.Create(AdHocOptions, creation.Syntax.GetLocation()));
            return;
        }

        if (typeName == "System.Text.Json.Utf8JsonWriter" && creation.Arguments.Length == 0)
            context.ReportDiagnostic(Diagnostic.Create(BareWriter, creation.Syntax.GetLocation()));
    }
}
