namespace Cdp.Deploy;

/// <summary>REPL/CCL deploy steer — parsed command (go + deploy args).</summary>
public readonly record struct CdpDeployReplCommand(
    string Go,
    CdpDeployMode Mode,
    string? Target,
    bool DryRun);
