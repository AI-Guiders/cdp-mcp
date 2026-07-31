#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Autonomous Continuity latch — blocks auto LeafPlateau→await_operator while armed.
/// Default armed=true (contract: ~99% without operator). Explicit await_operator still works.
/// Persist: %LocalAppData%/cdp-mcp/autonomous-continuity-{seat}.json
/// </summary>
internal static partial class IdeIgniteArmHost
{
    public const string AutonomousSeedArmId = "autonomous-seed-wake";
    public const string AutonomousStoreSchema = "autonomous_continuity/v1";

    static readonly object AutonomousGate = new();
    static bool AutonomousLoaded;
    static bool AutonomousArmed = true;
    static bool? AutonomousOverride;

    public static string AutonomousStorePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "cdp-mcp",
        Seat switch
        {
            "cdp-debug" => "autonomous-continuity-cdp-debug.json",
            "cdp" => "autonomous-continuity-cdp.json",
            _ => "autonomous-continuity-other.json"
        });

    /// <summary>Tests: force armed state without touching disk. null = use store.</summary>
    internal static void BindAutonomous(bool? armed) => AutonomousOverride = armed;

    public static bool IsAutonomousArmed()
    {
        if (AutonomousOverride is { } o)
            return o;
        EnsureAutonomousLoaded();
        lock (AutonomousGate)
            return AutonomousArmed;
    }

    public static object Autonomous(IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        var opHint = (Opt(args, "mode") ?? Opt(args, "state") ?? "").Trim().ToLowerInvariant();
        var armedArg = OptBool(args, "armed");

        if (opHint is "off" or "disarm" or "clear" || armedArg == false)
            return SetAutonomous(false, Opt(args, "why") ?? "operator/explicit off");
        if (opHint is "on" or "arm" || armedArg == true)
            return SetAutonomous(true, Opt(args, "why") ?? "operator/explicit on");

        EnsureAutonomousLoaded();
        return AutonomousStatusPayload();
    }

    public static object SetAutonomous(bool armed, string? why = null)
    {
        EnsureAutonomousLoaded();
        lock (AutonomousGate)
        {
            AutonomousArmed = armed;
            PersistAutonomousUnlocked();
        }

        PublishGlass();
        return AutonomousStatusPayload(why);
    }

    /// <summary>
    /// Last leaf done under autonomous: do not latch await_operator — re-arm seed wake.
    /// </summary>
    public static object AutonomousContinue(string reason)
    {
        EnsureStarted();
        // Drop awaiting latch if any — auto-plateau must not stick while autonomous.
        Resume(new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase));

        var args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["when"] = JsonSerializer.SerializeToElement("timer"),
            ["in"] = JsonSerializer.SerializeToElement("3s"),
            ["task"] = JsonSerializer.SerializeToElement("Autonomous continuity — seed next leaf"),
            ["id"] = JsonSerializer.SerializeToElement(AutonomousSeedArmId),
            ["once"] = JsonSerializer.SerializeToElement(true),
            ["charge"] = JsonSerializer.SerializeToElement("minimal"),
            ["settle_seconds"] = JsonSerializer.SerializeToElement(1),
            ["force"] = JsonSerializer.SerializeToElement(true)
        };

        var arm = Arm(args);
        return new
        {
            schema = IdeIgniteChannel.Schema,
            ok = true,
            op = "autonomous_continue",
            go = IdeIgniteChannel.GoName,
            tool = IdeIgniteChannel.ToolName,
            plateau = false,
            autonomous = true,
            need_seed = true,
            reason,
            pulse = "ignite · autonomous · seed next leaf · armed 3s",
            arm,
            continuity = ContinuitySlice(),
            explain = ExplainCardObject(IdeExplainability.New(
                "ignite.autonomous",
                "need_seed",
                $"leaf exhausted under autonomous ({reason}) — do not await operator; seed next leaf",
                "seed TM leaf from epic/DoD/research then keep flying")),
            hint = "Autonomous Continuity: empty board ≠ stop. Seed next leaf (investigate/build/KB if needed). AutoI wake in 3s."
        };
    }

    static object AutonomousStatusPayload(string? why = null)
    {
        var armed = IsAutonomousArmed();
        return new
        {
            schema = IdeIgniteChannel.Schema,
            ok = true,
            op = "autonomous",
            go = IdeIgniteChannel.GoName,
            tool = IdeIgniteChannel.ToolName,
            armed,
            why,
            store = AutonomousStorePath,
            pulse = armed ? "ignite · autonomous · ARMED" : "ignite · autonomous · off",
            continuity = ContinuitySlice(),
            explain = ExplainCardObject(IdeExplainability.New(
                "ignite.autonomous",
                armed ? "armed" : "off",
                armed
                    ? "auto LeafPlateau will not latch await_operator"
                    : "auto LeafPlateau latches await_operator (legacy solo plateau)",
                armed ? "op=autonomous armed=false to allow auto-plateau" : "op=autonomous armed=true")),
            hint = "Default ARMED. Explicit cdp_ignite/TM await_operator still works. Contract: playbook-autonomous-continuity-contract-v1."
        };
    }

    static void EnsureAutonomousLoaded()
    {
        lock (AutonomousGate)
        {
            if (AutonomousLoaded) return;
            AutonomousLoaded = true;
            if (!File.Exists(AutonomousStorePath))
            {
                AutonomousArmed = true;
                PersistAutonomousUnlocked();
                return;
            }

            try
            {
                var doc = JsonSerializer.Deserialize<AutonomousStoreDoc>(
                    File.ReadAllText(AutonomousStorePath), JsonOpts);
                AutonomousArmed = doc?.Armed ?? true;
            }
            catch
            {
                AutonomousArmed = true;
            }
        }
    }

    static void PersistAutonomousUnlocked()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AutonomousStorePath)!);
        var doc = new AutonomousStoreDoc
        {
            Schema = AutonomousStoreSchema,
            Armed = AutonomousArmed,
            SavedUtc = DateTimeOffset.UtcNow
        };
        var tmp = AutonomousStorePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(doc, JsonOpts));
        File.Move(tmp, AutonomousStorePath, overwrite: true);
    }

    sealed class AutonomousStoreDoc
    {
        public string? Schema { get; set; }
        public bool Armed { get; set; } = true;
        public DateTimeOffset? SavedUtc { get; set; }
    }
}
