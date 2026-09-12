namespace Cdp.Deploy;

/// <summary>REPL/CCL deploy steer — parsed command (go + deploy args).</summary>
public readonly record struct CdpDeployReplCommand(
    string Go,
    CdpDeployMode Mode,
    string? Target,
    bool DryRun);

/// <summary>
/// Chain-of-responsibility parser for cockpit deploy verbs (<c>deploy</c>, <c>hard_deploy</c>, …).
/// Head rules pick default mode; token rules mutate scan state (mode/target/dry_run).
/// </summary>
public sealed class CdpDeployReplParser
{
    public static CdpDeployReplParser Default { get; } = new();

    readonly IHeadRule[] _headRules;
    readonly ITokenRule[] _tokenRules;

    CdpDeployReplParser()
    {
        _headRules =
        [
            new HeadRule("soft_deploy", CdpDeployMode.Soft, "soft_deploy"),
            new HeadRule("hard_deploy", CdpDeployMode.Hard, "hard_deploy"),
            new HeadRule("deploy", CdpDeployMode.Ship, "deploy"),
        ];
        _tokenRules =
        [
            new KeyedValueTokenRule("target", static (scan, value) => scan.Target = value),
            new ModeTokenRule(),
            new FlagTokenRule(["dry", "dry_run", "peek"], static scan => scan.DryRun = true),
            new PositionalTargetTokenRule(["sibling", "self", "release", "debug"]),
        ];
    }

    public bool TryParse(string head, IReadOnlyList<string> tokens, out CdpDeployReplCommand command)
    {
        command = default;
        foreach (var rule in _headRules)
        {
            if (!rule.TryMatch(head, out var go, out var defaultMode))
                continue;

            var scan = new TokenScan(tokens, defaultMode);
            for (var i = 1; i < tokens.Count; i++)
            {
                scan.Index = i;
                foreach (var tokenRule in _tokenRules)
                {
                    if (!tokenRule.TryApply(scan))
                        continue;
                    i = scan.Index;
                    break;
                }
            }

            command = new CdpDeployReplCommand(go, scan.Mode, scan.Target, scan.DryRun);
            return true;
        }

        return false;
    }

    interface IHeadRule
    {
        bool TryMatch(string head, out string go, out CdpDeployMode defaultMode);
    }

    interface ITokenRule
    {
        bool TryApply(TokenScan scan);
    }

    sealed class HeadRule(string go, CdpDeployMode defaultMode, params string[] heads) : IHeadRule
    {
        public bool TryMatch(string head, out string goOut, out CdpDeployMode defaultModeOut)
        {
            foreach (var h in heads)
            {
                if (!head.Equals(h, StringComparison.OrdinalIgnoreCase))
                    continue;
                goOut = go;
                defaultModeOut = defaultMode;
                return true;
            }

            goOut = "";
            defaultModeOut = default;
            return false;
        }
    }

    sealed class TokenScan(IReadOnlyList<string> tokens, CdpDeployMode defaultMode)
    {
        public IReadOnlyList<string> Tokens { get; } = tokens;
        public int Index { get; set; }
        public CdpDeployMode Mode { get; set; } = defaultMode;
        public string? Target { get; set; }
        public bool DryRun { get; set; }

        public string Current => Tokens[Index];
    }

    sealed class KeyedValueTokenRule(string key, Action<TokenScan, string> assign) : ITokenRule
    {
        public bool TryApply(TokenScan scan)
        {
            var token = scan.Current;
            var prefix = key + "=";
            if (token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                assign(scan, token[prefix.Length..]);
                return true;
            }

            if (!token.Equals(key, StringComparison.OrdinalIgnoreCase) || scan.Index + 1 >= scan.Tokens.Count)
                return false;

            assign(scan, scan.Tokens[++scan.Index]);
            return true;
        }
    }

    sealed class ModeTokenRule : ITokenRule
    {
        static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
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

        public bool TryApply(TokenScan scan)
        {
            if (!Aliases.TryGetValue(scan.Current, out var wire))
                return false;
            scan.Mode = CdpDeployModeParser.Parse(wire);
            return true;
        }
    }

    sealed class FlagTokenRule(string[] flags, Action<TokenScan> apply) : ITokenRule
    {
        readonly HashSet<string> _flags = new(flags, StringComparer.OrdinalIgnoreCase);

        public bool TryApply(TokenScan scan)
        {
            if (!_flags.Contains(scan.Current))
                return false;
            apply(scan);
            return true;
        }
    }

    sealed class PositionalTargetTokenRule(string[] targets) : ITokenRule
    {
        readonly HashSet<string> _targets = new(targets, StringComparer.OrdinalIgnoreCase);

        public bool TryApply(TokenScan scan)
        {
            if (!_targets.Contains(scan.Current))
                return false;
            scan.Target ??= scan.Current;
            return true;
        }
    }
}
