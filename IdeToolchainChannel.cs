#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;
using CdpMcp.Cockpit.DataAcquisition;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=toolchain</c> / Meta <c>cdp_toolchain</c> (ADR 0198).
/// Runtime/compiler/SDK ensure — orthogonal to <c>lsp_ensure</c>. Hangs on DAL (ADR 0197).
/// </summary>
internal static partial class IdeToolchainChannel
{
    public const string SchemaVersion = "toolchain/v0";
    public const string ToolName = "cdp_toolchain";
    public const string GoName = "toolchain";
    const string UserRecipesKey = "toolchain.user_recipes_json";

    static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    static readonly JsonSerializerOptions Compact = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static ShellHabitat? _shell;
    static Func<ShellCwdDefaults>? _shellDefaults;

    public static void Configure(ShellHabitat shell, Func<ShellCwdDefaults> shellDefaults)
    {
        _shell = shell;
        _shellDefaults = shellDefaults;
    }

    public static string HandleJson(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null) =>
        JsonSerializer.Serialize(Handle(session, args), Pretty);

    public static object Handle(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        _ = session;
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = (Opt(args, "op") ?? Opt(args, "cmd") ?? "scene").Trim().ToLowerInvariant();
        var result = op switch
        {
            "scene" or "status" or "catalog" => Scene(),
            "probe" => Probe(args),
            "ensure" => Ensure(args),
            "install" => Install(args),
            "add" => Add(args),
            "which" => Which(args),
            _ => Fail("unknown_op", "op=scene|probe|ensure|install|add|which")
        };
        if (op is "probe" or "ensure" or "install" or "add")
            PublishGlass();
        return result;
    }

    public static string PulseLine(SessionContext? session = null)
    {
        _ = session;
        var rows = StatusRows();
        var ok = rows.Count(r => r.Ok);
        return $"toolchain · {ok}/{rows.Count} ok · go=toolchain";
    }

    /// <summary>Mirror toolchain health pulse to flat CIDE chrome latch (not EICAS).</summary>
    public static void PublishGlass()
    {
        try
        {
            var rows = StatusRows();
            var ok = rows.Count(r => r.Ok);
            var pulse = $"toolchain · {ok}/{rows.Count} ok · go=toolchain";
            // Dark Cockpit: chrome only while something is missing on PATH.
            var active = rows.Count > 0 && ok < rows.Count;
            CideToolchainLatch.Publish(active, pulse, ok, rows.Count);
        }
        catch
        {
            /* best-effort */
        }
    }

    static object Scene()
    {
        var rows = StatusRows();
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "scene",
            seam = "dal",
            adr = "0198",
            count = rows.Count,
            toolchains = rows.Select(RowCard),
            next = new object[]
            {
                new { go = "toolchain_ensure", label = "Ensure python", why = "id=python" },
                new { go = "toolchain_ensure", label = "Ensure gcc", why = "id=gcc" },
                new { go = "toolchain_probe", label = "Probe all", why = "op=probe" },
                new { go = "lsp_ensure", label = "LSP (other axis)", why = "intelligence ≠ toolchain" }
            },
            hint =
                "Any id: op=ensure id=python|gcc|javac|go|rust (or add custom). " +
                "Not lsp_ensure — runtime/compiler on PATH (DAL-adjacent)."
        };
    }

    static object Probe(IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = Opt(args, "id") ?? Opt(args, "toolchain") ?? Opt(args, "lang");
        var rows = StatusRows();
        if (id is { Length: > 0 })
            rows = rows.Where(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).ToList();

        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "probe",
            count = rows.Count,
            toolchains = rows.Select(RowCard),
            hint = rows.Any(r => !r.Ok)
                ? "Missing — op=ensure id=… or search + shell install."
                : "All probed toolchains resolve on PATH."
        };
    }

}
