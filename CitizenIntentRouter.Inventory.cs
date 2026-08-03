#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent inventory|gaps|cdp_inventory — IdeInventoryChannel.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteInventory(string raw)
    {
        var work = NormalizeInventoryCompound(raw);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op)
            && (work.StartsWith("inventory ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("inventory_desk ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("gaps ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("cdp_inventory ", StringComparison.OrdinalIgnoreCase)))
        {
            var sp = work.IndexOf(' ');
            var rest = sp < 0 ? "" : work[(sp + 1)..].Trim();
            var headSp = rest.IndexOf(' ');
            var head = headSp < 0 ? rest : rest[..headSp];
            if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                op = head;
        }

        op = string.IsNullOrWhiteSpace(op) ? "scene" : op.Trim().ToLowerInvariant();
        if (op is not ("scene" or "pulse" or "a"))
            return new Route(Verb.Inventory, raw, Ok: false, Reason: "inventory_op_unknown");
        if (op == "a") op = "pulse";

        return new Route(Verb.Inventory, raw, Ok: true, Op: op, Go: "inventory");
    }

    static string NormalizeInventoryCompound(string raw)
    {
        foreach (var (prefix, op) in InventoryCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "inventory " + op;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;
            return "inventory " + op + raw[prefix.Length..];
        }

        return raw;
    }

    static readonly (string Prefix, string Op)[] InventoryCompounds =
    [
        ("cdp_inventory_scene", "scene"),
        ("cdp_inventory_pulse", "pulse"),
        ("inventory_scene", "scene"),
        ("inventory_pulse", "pulse"),
        ("gaps_scene", "scene"),
        ("gaps_pulse", "pulse")
    ];
}
