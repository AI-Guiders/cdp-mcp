#nullable enable
using Cdp.Core;

namespace CdpMcp;

internal static partial class IdeReviewChannel
{
    public const string SchemaVersion = "review_organ/v0";
    public const int MaxFiles = 32;

    public sealed record FileCard(
        string Path,
        string Status,
        string Risk,
        string Why,
        string Go);

    public sealed record Inputs(
        SessionContext Session,
        bool GitDirty,
        int ProblemErrors,
        bool TestsFailed,
        int QualityFail,
        int QualityWarn,
        IdeChkChannel.Snap? Ecl = null);

    public sealed record Snap(
        bool Ok,
        string Pulse,
        int FileCount,
        int HighRisk,
        bool MachineOk,
        IReadOnlyList<FileCard> Files);

}
