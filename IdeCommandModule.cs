#nullable enable
using System.Collections.Frozen;
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Unified IdeCommandModule (ICM): single execute seam for MCP <c>CallTool</c>
/// and future on-demand GUI / human HCI. CDP owns the command language.
/// </summary>
/// <remarks>
/// <para>
/// Dual HCI, one drive (CDP-ADR-0019): agent organs (<c>go=</c> / soft desk) and
/// human organs (Intent Melody / chords / palette / GUI) both resolve to the same
/// <c>command_id</c> surface. No permanent IdeCommands→ICM adapter — GUI becomes
/// an optional CDP client/shell. Preserve CIDE Melody + <c>CascadeIdeSettings</c>
/// when relocating chrome.
/// </para>
/// <para>
/// Navigation Anchor (<c>cdp_land</c>) is agent-ready; GUI may project the same
/// Family:navigation wire later. SoftDispatch stays behind this module.
/// </para>
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
