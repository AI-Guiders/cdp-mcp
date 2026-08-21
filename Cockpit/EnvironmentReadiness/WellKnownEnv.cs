#nullable enable

namespace CdpMcp.Cockpit.EnvironmentReadiness;

/// <summary>Env var names for ER DAL (aligned with AgentNotes.Core / CIDE ADR 0023).</summary>
public static class WellKnownEnv
{
    public const string AgentNotesFile = "AGENT_NOTES_FILE";
    public const string NetcoreDbgPath = "NETCOREDBG_PATH";
}
