#nullable enable
using Cdp.Core;

namespace CdpMcp;

/// <summary>Alert/SA DTOs — Level, Sit, Inputs, Snap.</summary>
internal static partial class IdeAlertChannel
{
    public const string SchemaVersion = "alert_channel/v1.1";

    public enum Level
    {
        Clear = 0,
        Warn = 1,
        Fail = 2
    }

    /// <summary>Attention zones — phase/intent/locus/layout — not EICAS severity.</summary>
    public sealed record Sit(
        string PhaseObject,
        string? Intent,
        string? Locus,
        string? LayoutHint,
        string? SeatNote);

    public sealed record Inputs(
        QualityGates.QualitySnap Quality,
        int DiskChanged,
        bool DapActive,
        bool DapStopped,
        int ProblemErrors = 0,
        int ProblemWarnings = 0,
        int ShellRunning = 0,
        int ShellFailed = 0,
        bool GitDirty = false,
        Sit? Sit = null,
        string? StagePhaseMismatch = null,
        int ChkOpenRequired = 0,
        string? ChkPulse = null);

    public sealed record Snap(
        Level Level,
        bool Ok,
        string Pulse,
        string[] Lines,
        int QualityFail,
        int QualityWarn,
        int DiskChanged,
        bool DapStopped,
        bool DapActive,
        int ProblemErrors,
        int ProblemWarnings,
        int ShellRunning,
        int ShellFailed,
        bool GitDirty,
        Sit? Sit);

}
