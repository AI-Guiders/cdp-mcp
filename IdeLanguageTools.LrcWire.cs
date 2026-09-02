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
            FindUsagesResult usages => JsonSerializer.Serialize(WireFindUsages(usages), LrcJsonOptions),
            CompletionsResult completions => JsonSerializer.Serialize(WireCompletions(completions), LrcJsonOptions),
            SymbolAtPositionResult symbol => JsonSerializer.Serialize(WireSymbolAtPosition(symbol), LrcJsonOptions),
            RenameSymbolResult rename => JsonSerializer.Serialize(WireRename(rename), LrcJsonOptions),
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

    static object WireFindUsages(FindUsagesResult result) => new
    {
        references = result.References.Select(WireReference).ToArray(),
    };

    static object WireReference(LanguageReference reference) => new
    {
        span = WireSpan(reference.Span),
        target = WireSpan(reference.Target),
        kind = reference.Kind,
    };

    static object WireCompletions(CompletionsResult result) => new
    {
        items = result.Items.Select(WireCompletion).ToArray(),
    };

    static object WireCompletion(LanguageCompletion item) => new
    {
        label = item.Label,
        kind = item.Kind,
        detail = item.Detail,
        insertText = item.InsertText,
    };

    static object WireSymbolAtPosition(SymbolAtPositionResult symbol) => new
    {
        kind = symbol.Kind,
        name = symbol.Name,
        qualifiedName = symbol.QualifiedName,
        span = WireSpan(symbol.Span),
    };

    static object WireRename(RenameSymbolResult result) => new
    {
        oldName = result.OldName,
        newName = result.NewName,
        symbolKind = result.SymbolKind,
        applied = result.Applied,
        message = string.IsNullOrWhiteSpace(result.Message) ? null : result.Message,
        files = result.Files,
        changes = result.Changes.Select(c => new { path = c.Path, newText = c.NewText }).ToArray(),
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
