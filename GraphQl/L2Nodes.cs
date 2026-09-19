#nullable enable
using Cdp.ScriptableIde;

namespace CdpMcp.GraphQl;

/// <summary>SessionContextWire projection (ADR-0233) — empty projectRoot is honest null, not silent invent.</summary>
public sealed class SessionNode
{
    public SessionNode(
        string phase,
        string @object,
        string? intent,
        string? language,
        string? projectRoot,
        string? projectKind,
        string? solutionOrProjectPath,
        string? scmRoot)
    {
        Phase = phase;
        Object = @object;
        Intent = intent;
        Language = language;
        ProjectRoot = projectRoot;
        ProjectKind = projectKind;
        SolutionOrProjectPath = solutionOrProjectPath;
        ScmRoot = scmRoot;
    }

    public string Phase { get; }
    public string Object { get; }
    public string? Intent { get; }
    public string? Language { get; }
    public string? ProjectRoot { get; }
    public string? ProjectKind { get; }
    public string? SolutionOrProjectPath { get; }
    public string? ScmRoot { get; }
}

public sealed class CorrespondenceDocNode
{
    public CorrespondenceDocNode(Anchor anchor, string path, string? abs, string? kind, string? title, string role)
    {
        Anchor = anchor;
        Path = path;
        Abs = abs;
        Kind = kind;
        Title = title;
        Role = role;
    }

    public Anchor Anchor { get; }
    public string Path { get; }
    public string? Abs { get; }
    public string? Kind { get; }
    public string? Title { get; }
    public string Role { get; }
}

public sealed class CorrespondenceResult
{
    public CorrespondenceResult(
        string path,
        string? workspaceRoot,
        IReadOnlyList<CorrespondenceDocNode> docs,
        IReadOnlyList<CorrespondenceDocNode> reverse)
    {
        Path = path;
        WorkspaceRoot = workspaceRoot;
        Docs = docs;
        Reverse = reverse;
    }

    public string Path { get; }
    public string? WorkspaceRoot { get; }
    public IReadOnlyList<CorrespondenceDocNode> Docs { get; }
    public IReadOnlyList<CorrespondenceDocNode> Reverse { get; }
}

/// <summary>Slim git_scene/v0 envelope — pulse fields from ToolHandlers.HandleScene.</summary>
public sealed class GitSceneNode
{
    public GitSceneNode(string schema, bool ok, string rawJson)
    {
        Schema = schema;
        Ok = ok;
        RawJson = rawJson;
    }

    public string Schema { get; }
    public bool Ok { get; }
    public string RawJson { get; }
}

/// <summary>Knowledge recall envelope parity with memory_world_recall_knowledge (ADR-0218/0233).</summary>
public sealed class KnowledgeHitNode
{
    public KnowledgeHitNode(string? path, string? title, string? preview, double? score, Anchor? anchor)
    {
        Path = path;
        Title = title;
        Preview = preview;
        Score = score;
        Anchor = anchor;
    }

    public string? Path { get; }
    public string? Title { get; }
    public string? Preview { get; }
    public double? Score { get; }
    public Anchor? Anchor { get; }
}

public sealed class KnowledgeRecallResult
{
    public KnowledgeRecallResult(
        string query,
        int total,
        IReadOnlyList<string> layersUsed,
        IReadOnlyList<KnowledgeHitNode> hits,
        string rawJson)
    {
        Query = query;
        Total = total;
        LayersUsed = layersUsed;
        Hits = hits;
        RawJson = rawJson;
    }

    public string Query { get; }
    public int Total { get; }
    public IReadOnlyList<string> LayersUsed { get; }
    public IReadOnlyList<KnowledgeHitNode> Hits { get; }
    public string RawJson { get; }
}