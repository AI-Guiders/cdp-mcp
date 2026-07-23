using System.Text.Json;
using Cdp.Core;

namespace CdpMcp.Backends;

/// <summary>
/// Injects default knowledge roots and rejects paths outside the facet's allowed roots.
/// </summary>
internal sealed class MemoryScopeGateway
{
    private static readonly HashSet<string> KnowledgeTools = new(StringComparer.Ordinal)
    {
        "knowledge_tags",
        "read_knowledge_file",
        "list_knowledge_files",
        "write_knowledge_file",
        "append_knowledge_file",
        "upsert_knowledge_section",
        "delete_knowledge_section",
        "delete_knowledge_file",
        "validate_sections",
        "normalize_sections"
    };

    private static readonly HashSet<string> PackTools = new(StringComparer.Ordinal)
    {
        "get_definition",
        "list_pack",
        "get_process",
        "get_procedure",
        "radius_gate_check"
    };

    private readonly string _domain;
    private readonly IReadOnlyList<string> _roots;
    private readonly bool _allowPlaybooksUnderWorlds;

    public MemoryScopeGateway(string domain, IReadOnlyList<string> roots, bool allowPlaybooksUnderWorlds = false)
    {
        _domain = domain;
        _roots = roots.Select(NormalizeRoot).Where(r => r.Length > 0).ToArray();
        if (_roots.Count == 0)
            throw new InvalidOperationException($"Facet '{domain}' has no roots configured.");
        _allowPlaybooksUnderWorlds = allowPlaybooksUnderWorlds;
    }

    public IReadOnlyList<string> Roots => _roots;

    public IReadOnlyDictionary<string, JsonElement> Apply(
        string underlyingName,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (PackTools.Contains(underlyingName))
            return ApplyPackTool(underlyingName, args);

        if (!KnowledgeTools.Contains(underlyingName))
            return args;

        var dict = args.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        // file_path: validate when set (read/write/validate knowledge); never invent a file.
        if (HasNonEmpty(dict, "file_path") || (dict.TryGetValue("file_path", out var fpEl)
            && fpEl.ValueKind == JsonValueKind.String))
        {
            ApplyPathArg(dict, "file_path", injectIfEmpty: false);
        }

        // subdir/path: inject default root for list/tags; validate when caller supplies them.
        var listLike = underlyingName is "list_knowledge_files" or "knowledge_tags";
        if (listLike || HasNonEmpty(dict, "subdir"))
            ApplyPathArg(dict, "subdir", injectIfEmpty: listLike);
        if (HasNonEmpty(dict, "path"))
            ApplyPathArg(dict, "path", injectIfEmpty: false);

        return dict;
    }

    private IReadOnlyDictionary<string, JsonElement> ApplyPackTool(
        string underlyingName,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var dict = args.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        dict["allowed_roots"] = JsonSerializer.SerializeToElement(_roots.ToArray());

        if (HasNonEmpty(dict, "pack_path"))
            ApplyPathArg(dict, "pack_path", injectIfEmpty: false);

        // Dogfood defaults: skill → consumer; world callers pass pack_id (epistemic-scene).
        if (_domain == CdpDomains.MemorySkill
            && underlyingName is "get_definition" or "get_process" or "get_procedure" or "list_pack"
            && !HasNonEmpty(dict, "pack_id")
            && !HasNonEmpty(dict, "pack_path"))
        {
            SetString(dict, "pack_id", "agent-operations-cdp");
        }

        if (_domain == CdpDomains.MemoryWorld
            && underlyingName is "get_definition" or "get_process" or "get_procedure" or "list_pack"
            && !HasNonEmpty(dict, "pack_id")
            && !HasNonEmpty(dict, "pack_path"))
        {
            SetString(dict, "pack_id", "epistemic-scene");
        }

        return dict;
    }

    private void ApplyPathArg(Dictionary<string, JsonElement> dict, string key, bool injectIfEmpty)
    {
        var present = dict.TryGetValue(key, out var el);
        var raw = present && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            if (!injectIfEmpty)
                return;
            SetString(dict, key, _roots[0]);
            return;
        }

        var normalized = NormalizeRelative(raw);
        EnsureAllowed(normalized);
        SetString(dict, key, normalized);
    }

    private void EnsureAllowed(string relative)
    {
        if (IsUnderAnyRoot(relative))
            return;

        if (_allowPlaybooksUnderWorlds && IsPlaybookUnderWorlds(relative))
            return;

        var roots = string.Join(", ", _roots);
        throw new ArgumentException(
            $"Path '{relative}' is outside {_domain} roots [{roots}]; " +
            $"use another memory_* facet (e.g. {CdpDomains.MemoryWorld}, {CdpDomains.MemoryProject}, {CdpDomains.MemorySkill}).");
    }

    private bool IsUnderAnyRoot(string relative)
    {
        foreach (var root in _roots)
        {
            if (relative.Equals(root, StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsPlaybookUnderWorlds(string relative)
    {
        if (!relative.StartsWith("worlds/", StringComparison.OrdinalIgnoreCase))
            return false;
        var name = Path.GetFileName(relative.Replace('\\', '/'));
        return name.StartsWith("playbook-", StringComparison.OrdinalIgnoreCase)
            || name.Contains("playbook", StringComparison.OrdinalIgnoreCase);
    }

    internal static string NormalizeRelative(string path)
    {
        var p = path.Replace('\\', '/').Trim();
        while (p.StartsWith("./", StringComparison.Ordinal))
            p = p[2..];
        p = p.Trim('/');
        if (p.Length == 0)
            return p;
        if (p.Contains("..", StringComparison.Ordinal)
            || Path.IsPathRooted(path)
            || p.StartsWith('/'))
        {
            throw new ArgumentException(
                $"Path '{path}' is invalid (no absolute paths or '..' segments). Use a relative knowledge/ path.");
        }

        while (p.Contains("//", StringComparison.Ordinal))
            p = p.Replace("//", "/", StringComparison.Ordinal);
        return p;
    }

    private static string NormalizeRoot(string root) =>
        string.IsNullOrWhiteSpace(root) ? "" : NormalizeRelative(root);

    private static bool HasNonEmpty(Dictionary<string, JsonElement> dict, string key) =>
        dict.TryGetValue(key, out var el)
        && el.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(el.GetString());

    private static void SetString(Dictionary<string, JsonElement> dict, string key, string value) =>
        dict[key] = JsonSerializer.SerializeToElement(value);
}
