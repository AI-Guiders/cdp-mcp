#nullable enable

using CdpMcp.Cockpit.DataAcquisition;

namespace CdpMcp.Cockpit.DataAcquisition;

public enum AgentNotesFilePathKind
{
    Unset,
    ParentDirForGlobalFile,
    FileExists,
    ParentMissing,
    InvalidPath
}

public enum AgentNotesConfigPathKind
{
    Unset,
    FileExists,
    FileMissing,
    InvalidPath
}

public enum NetcoreDbgPathKind
{
    UnsetFoundOnPath,
    UnsetNotOnPath,
    ExplicitResolved,
    InvalidPath,
    ExplicitBareNameNotInPath,
    ExplicitFilePathMissing
}

/// <summary>Fs/PATH logic for env readiness rows (CIDE quarry, headless).</summary>
public static class EnvironmentReadinessPathAcquisition
{
    public static AgentNotesFilePathKind ClassifyAgentNotesFilePath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return AgentNotesFilePathKind.Unset;

        try
        {
            var full = Path.GetFullPath(raw.Trim());
            var parent = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                return AgentNotesFilePathKind.ParentDirForGlobalFile;

            if (File.Exists(full))
                return AgentNotesFilePathKind.FileExists;

            return AgentNotesFilePathKind.ParentMissing;
        }
        catch
        {
            return AgentNotesFilePathKind.InvalidPath;
        }
    }

    public static AgentNotesConfigPathKind ClassifyAgentNotesConfigPath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return AgentNotesConfigPathKind.Unset;

        try
        {
            var full = Path.GetFullPath(raw.Trim());
            return File.Exists(full) ? AgentNotesConfigPathKind.FileExists : AgentNotesConfigPathKind.FileMissing;
        }
        catch
        {
            return AgentNotesConfigPathKind.InvalidPath;
        }
    }

    public static NetcoreDbgPathKind ClassifyNetcoreDbgPath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ToolchainPathProbe.Resolve("netcoredbg") is not null
                ? NetcoreDbgPathKind.UnsetFoundOnPath
                : NetcoreDbgPathKind.UnsetNotOnPath;
        }

        var trimmed = raw.Trim();
        if (ToolchainPathProbe.Resolve(trimmed) is not null)
            return NetcoreDbgPathKind.ExplicitResolved;

        try
        {
            _ = Path.GetFullPath(trimmed);
        }
        catch
        {
            return NetcoreDbgPathKind.InvalidPath;
        }

        if (!trimmed.Contains(Path.DirectorySeparatorChar) && !trimmed.Contains(Path.AltDirectorySeparatorChar))
            return NetcoreDbgPathKind.ExplicitBareNameNotInPath;

        return NetcoreDbgPathKind.ExplicitFilePathMissing;
    }
}
