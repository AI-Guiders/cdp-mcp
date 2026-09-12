#nullable enable
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Serialization;

namespace Cdp.Config;

public sealed class CdpConfigValidationResult
{
    public List<string> Warnings { get; } = [];
    public List<string> Errors { get; } = [];
    public bool Ok => Errors.Count == 0;
}

/// <summary>ADR-0222 bootstrap validator — unknown keys warn; strict mode fails CI.</summary>
public static class CdpConfigValidator
{
    static readonly HashSet<string> BootstrapSections = new(StringComparer.OrdinalIgnoreCase)
    {
        "tower", "slots", "bridge", "service", "tools"
    };

    static readonly Dictionary<string, HashSet<string>> KnownKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tower"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "listen_port", "registry", "target_cache_ms", "probe_timeout_ms"
        },
        ["slots"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "install_dir", "auto_start", "port_range", "heartbeat_seconds", "registry", "enabled", "bind"
        },
        ["bridge"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "base_url", "token_path", "auto_start_slot",
            "capabilities_poll_ms", "deploy_wait_ms", "deploy_poll_ms", "deploy_gap_retry_ms", "service_ready_ms"
        },
        ["tools"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "rg", "lynx", "openvsx_base", "plugins_root", "forum_root"
        },
        ["service"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "enabled", "bind", "port", "token_path", "install_dir", "auto_start"
        },
    };

    public static CdpConfigValidationResult Validate(string toml, bool strict = false)
    {
        var result = new CdpConfigValidationResult();
        if (string.IsNullOrWhiteSpace(toml))
            return result;

        TomlTable model;
        try
        {
            model = TomlSerializer.Deserialize<TomlTable>(toml)
                ?? throw new InvalidOperationException("Empty TOML document.");
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Invalid TOML: {ex.Message}");
            return result;
        }

        if (model.TryGetValue("service", out _))
        {
            var msg = "[service] is deprecated (ADR-0222); use [tower], [slots], and [bridge].";
            if (strict)
                result.Errors.Add(msg);
            else
                result.Warnings.Add(msg);
        }

        foreach (var section in BootstrapSections)
        {
            if (!model.TryGetValue(section, out var node) || node is not TomlTable table)
                continue;

            if (!KnownKeys.TryGetValue(section, out var allowed))
                continue;

            foreach (var key in table.Keys)
            {
                if (allowed.Contains(key))
                    continue;

                var msg = $"Unknown bootstrap key '{section}.{key}' in cdp-mcp.toml.";
                if (strict)
                    result.Errors.Add(msg);
                else
                    result.Warnings.Add(msg);
            }
        }

        return result;
    }

    public static bool IsStrictMode =>
        string.Equals(
            Environment.GetEnvironmentVariable("CDP_CONFIG_STRICT"),
            "1",
            StringComparison.OrdinalIgnoreCase);
}
