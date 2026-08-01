#nullable enable

namespace CdpMcp;

internal static partial class IdeOnboardChannel
{
    public sealed class ScanDoc
    {
        public string Schema { get; set; } = SchemaVersion;
        public string Title { get; set; } = "onboard";
        public string? ProjectName { get; set; }
        public string? Root { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
        public string ProfileHint { get; set; } = "unknown";
        public DocsHint Docs { get; set; } = new();
        public List<Hit> Entrypoints { get; set; } = [];
        public List<FolderHit> TopFolders { get; set; } = [];
        public List<VerticalHit> Verticals { get; set; } = [];
        public List<string> Solutions { get; set; } = [];
        public int CsprojCount { get; set; }
        public int FilesScanned { get; set; }
        public bool Truncated { get; set; }
    }

    public sealed class DocsHint
    {
        public bool HasReadme { get; set; }
        public bool HasDocsDir { get; set; }
        public int AdrCount { get; set; }
        public string? ReadmePath { get; set; }
    }

    public sealed class Hit
    {
        public string Kind { get; set; } = "entrypoint";
        public string Label { get; set; } = "";
        public string? Path { get; set; }
        public string? Anchor { get; set; }
        public int Score { get; set; }
    }

    public sealed class FolderHit
    {
        public string Path { get; set; } = "";
        public int FileCount { get; set; }
    }

    public sealed class VerticalHit
    {
        public string Name { get; set; } = "";
        public int FileCount { get; set; }
        public string? SamplePath { get; set; }
        public string? SampleAnchor { get; set; }
    }
}
