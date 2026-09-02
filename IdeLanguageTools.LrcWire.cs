using System.Text.Json;
using AIGuiders.Platform.Modeling.Language;

namespace CdpMcp;

internal static partial class IdeLanguageTools
{
    static string SerializeLrcResult(object? result) =>
        result switch
        {
            DiagnosticsResult diagnostics => JsonSerializer.Serialize(WireDiagnostics(diagnostics), LrcJsonOptions),
            DocumentSymbolsResult symbols => JsonSerializer.Serialize(WireDocumentSymbols(symbols), LrcJsonOptions),
            LanguageNavigation navigation => JsonSerializer.Serialize(WireNavigation(navigation), LrcJsonOptions),
            null => "null",
            _ => JsonSerializer.Serialize(result, LrcJsonOptions),
        };

    static object WireDiagnostics(DiagnosticsResult result) => new
    {
        diagnostics = result.Diagnostics.Select(WireDiagnostic).ToArray(),
    };

    static object WireDiagnostic(LanguageDiagnostic diagnostic) => new
    {
        id = diagnostic.Id,
        severity = WireSeverity(diagnostic.Severity),
        message = diagnostic.Message,
        span = WireSpan(diagnostic.Span),
        tags = diagnostic.Tags,
        language = diagnostic.Language,
    };

    static object WireDocumentSymbols(DocumentSymbolsResult result) => new
    {
        root = WireSymbol(result.Root),
    };

    static object WireSymbol(LanguageSymbol symbol) => new
    {
        name = symbol.Name,
        kind = symbol.Kind,
        span = WireSpan(symbol.Span),
        container = symbol.Container,
        children = symbol.Children.Select(WireSymbol).ToArray(),
    };

    static object? WireNavigation(LanguageNavigation? navigation) =>
        navigation is null
            ? null
            : new
            {
                definition = WireSpan(navigation.Definition),
                declarations = navigation.Declarations.Select(WireSpan).ToArray(),
            };

    static object WireSpan(SourceSpan span) => new
    {
        path = span.Path,
        line = span.Line,
        column = span.Column,
        endLine = span.EndLine,
        endColumn = span.EndColumn,
    };

    static string WireSeverity(Severity severity) =>
        severity switch
        {
            { IsError: true } => "error",
            { IsWarning: true } => "warning",
            { IsInfo: true } => "info",
            { IsHint: true } => "hint",
            _ => "info",
        };
}
