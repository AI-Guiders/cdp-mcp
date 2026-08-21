#nullable enable

using CdpMcp.Cockpit.EnvironmentReadiness;

namespace CdpMcp.Cockpit.DataAcquisition;

/// <summary>Env vars snapshot for ER rows (ADR 0023 DAL).</summary>
public readonly record struct EnvironmentReadinessEnvSnapshot(
    string? AgentNotesFile,
    string? AgentNotesConfigPath,
    string? NetcoreDbgPath)
{
    public static EnvironmentReadinessEnvSnapshot FromProcess(string? notesConfigPath) =>
        new(
            Environment.GetEnvironmentVariable(WellKnownEnv.AgentNotesFile),
            notesConfigPath,
            Environment.GetEnvironmentVariable(WellKnownEnv.NetcoreDbgPath));
}
