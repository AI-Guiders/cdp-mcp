#nullable enable
using System.Collections.Frozen;
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Unified IdeCommandModule (ICM): single execute seam for MCP <c>CallTool</c>
/// and future CIDE projections. CDP owns the command language; CIDE adapters
/// call the same <c>command_id</c> surface (tool name / go Meta) — they do not
/// invent a parallel IdeCommands SSOT.
/// </summary>
/// <remarks>
/// See <c>docs/adr/CDP-ADR-0019-icm-cdp-first-command-module.md</c>.
/// Profiles <c>agent-only</c> vs <c>dual-cockpit</c> share this module;
/// Anchor Start/Stop (operator GUI) is deferred.
/// </remarks>
public static class IdeCommandModule
{
    public delegate Task<string> ExecuteHandler(
        string commandId,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken);

    static ExecuteHandler? _execute;

    public static bool IsBound => _execute is not null;

    public static void Bind(ExecuteHandler execute) =>
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));

    /// <summary>Test / shutdown helper — clears the host binding.</summary>
    public static void Unbind() => _execute = null;

    public static Task<string> ExecuteAsync(
        string commandId,
        IReadOnlyDictionary<string, JsonElement>? args = null,
        CancellationToken cancellationToken = default)
    {
        var handler = _execute
            ?? throw new InvalidOperationException(
                "IdeCommandModule is not bound. Call Bind from host startup.");
        return handler(
            commandId,
            args ?? FrozenDictionary<string, JsonElement>.Empty,
            cancellationToken);
    }
}
