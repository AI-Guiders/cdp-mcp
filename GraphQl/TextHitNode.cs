#nullable enable
using Cdp.ScriptableIde;

namespace CdpMcp.GraphQl;

/// <summary>Projection of FindInFiles.Hit — always carries typed Anchor (ADR-0233).</summary>
public sealed class TextHitNode
{
    public TextHitNode(Anchor anchor, string path, int line, int column, string preview)
    {
        Anchor = anchor;
        Path = path;
        Line = line;
        Column = column;
        Preview = preview;
    }

    public Anchor Anchor { get; }
    public string Path { get; }
    public int Line { get; }
    public int Column { get; }
    public string Preview { get; }
}

public sealed class TextHitConnection
{
    public TextHitConnection(IReadOnlyList<TextHitNode> nodes, int totalCount)
    {
        Nodes = nodes;
        TotalCount = totalCount;
    }

    public IReadOnlyList<TextHitNode> Nodes { get; }
    public int TotalCount { get; }
}

public sealed class PeekLineNode
{
    public PeekLineNode(int n, string text, Anchor anchor)
    {
        N = n;
        Text = text;
        Anchor = anchor;
    }

    public int N { get; }
    public string Text { get; }
    public Anchor Anchor { get; }
}

public sealed class PeekResult
{
    public PeekResult(string path, IReadOnlyList<PeekLineNode> lines)
    {
        Path = path;
        Lines = lines;
    }

    public string Path { get; }
    public IReadOnlyList<PeekLineNode> Lines { get; }
}

/// <summary>Projection of IdeProblemsChannel.Row with typed Anchor.</summary>
public sealed class DiagnosticNode
{
    public DiagnosticNode(
        string id,
        string severity,
        string message,
        string? code,
        string path,
        int line,
        int endLine,
        Anchor anchor,
        bool stale)
    {
        Id = id;
        Severity = severity;
        Message = message;
        Code = code;
        Path = path;
        Line = line;
        EndLine = endLine;
        Anchor = anchor;
        Stale = stale;
    }

    public string Id { get; }
    public string Severity { get; }
    public string Message { get; }
    public string? Code { get; }
    public string Path { get; }
    public int Line { get; }
    public int EndLine { get; }
    public Anchor Anchor { get; }
    public bool Stale { get; }
}

