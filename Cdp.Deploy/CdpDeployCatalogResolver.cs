using AIGuiders.Platform.Execution.CommandPlane;
using AIGuiders.Platform.Modeling.Notations.Argument;
using AIGuiders.Platform.Notations;
using AIGuiders.Platform.Notations.Argument.Kv;
using Cdp.Deploy.Generated;

namespace Cdp.Deploy;

/// <summary>
/// Federation deploy command resolve — console path/kv split + catalog longest-prefix match
/// (GUIDERS-ADR-0065 / ADR-0015; mirrors ConsoleCommandNotation + SlashLineResolver).
/// </summary>
public sealed class CdpDeployCatalogResolver
{
    public static CdpDeployCatalogResolver Default { get; } = new();

    static readonly Dictionary<string, string> ModeAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["soft"] = "soft",
        ["hard"] = "hard",
        ["ship"] = "ship",
        ["slot"] = "ship",
        ["apply"] = "apply",
        ["pending"] = "apply",
        ["rollout"] = "rollout",
        ["r"] = "rollout",
        ["dual"] = "rollout",
    };

    static readonly HashSet<string> DryFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "dry",
        "dry_run",
        "peek",
    };

    static readonly HashSet<string> PositionalTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        "sibling",
        "self",
        "release",
        "debug",
    };

    readonly CommandCatalogIndex _catalog;

    CdpDeployCatalogResolver() => _catalog = CdpDeployCatalogBuilder.Build();

    public bool TryParse(string head, IReadOnlyList<string> tokens, out CdpDeployReplCommand command)
    {
        command = default;
        if (tokens.Count == 0)
            return false;

        var line = string.Join(' ', tokens);
        if (!TryParseLine(line, out command))
            return false;

        return head.Equals(command.Go, StringComparison.OrdinalIgnoreCase);
    }

    public bool TryParseLine(string line, out CdpDeployReplCommand command)
    {
        command = default;
        if (string.IsNullOrWhiteSpace(line))
            return false;

        if (!DeployCommandNotation.TryParse(line, out var pathWire, out var kvArgs, out var kvTailRaw))
            return false;

        var body = string.Join(' ', pathWire.Tokens);
        if (!DeployCatalogPathResolver.TryResolveBody(body, _catalog, out var resolution))
            return false;

        if (!CdpDeployCatalog.Heads.TryGetValue(resolution.CanonicalPath, out var meta))
            return false;

        var scan = ScanArgs(resolution.ArgTail, kvTailRaw, meta.DefaultMode, kvArgs);
        command = new CdpDeployReplCommand(meta.Go, scan.Mode, scan.Target, scan.DryRun);
        return true;
    }

    static ArgScan ScanArgs(string argTail, string kvTailRaw, CdpDeployMode defaultMode, NormalizedArguments kvArgs)
    {
        var scan = new ArgScan(defaultMode);
        ApplyKvSlots(scan, kvArgs);

        if (!string.IsNullOrWhiteSpace(argTail))
        {
            var tailKv = KvArgumentNotation.Parse(argTail);
            ApplyKvSlots(scan, tailKv);
        }

        ScanArgTokens(scan, argTail);
        ScanArgTokens(scan, kvTailRaw);
        return scan;
    }

    static void ScanArgTokens(ArgScan scan, string? segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return;

        var tokens = segment.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (NotationKvPair.TrySplitFirst(token, '=', out var kv, out _))
            {
                ApplyKv(scan, kv.Key, kv.Value);
                continue;
            }

            if (token.Equals("target", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Length)
            {
                scan.Target = tokens[++i];
                continue;
            }

            if (DryFlags.Contains(token))
            {
                scan.DryRun = true;
                continue;
            }

            if (ModeAliases.TryGetValue(token, out var wire))
            {
                scan.Mode = CdpDeployModeParser.Parse(wire);
                continue;
            }

            if (PositionalTargets.Contains(token))
                scan.Target ??= token;
        }
    }

    static void ApplyKvSlots(ArgScan scan, NormalizedArguments args)
    {
        if (args.Slots is not { } slots)
            return;

        foreach (var (key, value) in slots)
            ApplyKv(scan, key, value);
    }

    static void ApplyKv(ArgScan scan, string key, string value)
    {
        if (key.Equals("target", StringComparison.OrdinalIgnoreCase))
            scan.Target = value;
        else if (key.Equals("mode", StringComparison.OrdinalIgnoreCase))
            scan.Mode = CdpDeployModeParser.Parse(value);
        else if (key.Equals("dry", StringComparison.OrdinalIgnoreCase)
                 || key.Equals("dry_run", StringComparison.OrdinalIgnoreCase))
            scan.DryRun = IsTruthy(value);
    }

    static bool IsTruthy(string value) =>
        !value.Equals("false", StringComparison.OrdinalIgnoreCase)
        && !value.Equals("0", StringComparison.OrdinalIgnoreCase);

    sealed class ArgScan(CdpDeployMode defaultMode)
    {
        public CdpDeployMode Mode { get; set; } = defaultMode;
        public string? Target { get; set; }
        public bool DryRun { get; set; }
    }
}
