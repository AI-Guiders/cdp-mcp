#nullable enable
using System.Text.Json;
using Tomlyn;

namespace CdpMcp;

/// <summary>
/// Load <c>.cdp/assertions.toml</c> and evaluate ADX kernels (go=quality scope=assert).
/// </summary>
internal static class AdxAssertions
{
    public const string SchemaVersion = "adx_assertions/v0";

    public static object Evaluate(string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
        {
            return new
            {
                schema = SchemaVersion,
                ok = false,
                scope = "assert",
                error = "project_root_missing",
                hint = "cdp_open first — then go=quality scope=assert"
            };
        }

        var path = Path.Combine(projectRoot, ".cdp", "assertions.toml");
        if (!File.Exists(path))
        {
            return new
            {
                schema = SchemaVersion,
                ok = false,
                scope = "assert",
                error = "assertions_toml_missing",
                path,
                hint = "Create .cdp/assertions.toml (ADX catalog)"
            };
        }

        var catalog = LoadCatalog(path);
        var findings = new List<object>();
        var fail = 0;
        var warn = 0;
        var deferred = 0;

        foreach (var item in catalog)
        {
            if (string.Equals(item.Status, "deferred", StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Engine, "deferred", StringComparison.OrdinalIgnoreCase))
            {
                deferred++;
                findings.Add(new
                {
                    id = item.Id,
                    severity = "info",
                    status = "deferred",
                    topic = item.Topic,
                    statement = item.Statement,
                    pulse = $"{item.Id} deferred"
                });
                continue;
            }

            var card = RunCheck(item.Check);
            if (!card.Ok) fail++;
            findings.Add(new
            {
                id = item.Id,
                severity = card.Ok ? "ok" : "fail",
                status = card.Ok ? "pass" : "fail",
                topic = item.Topic,
                statement = item.Statement,
                engine = item.Engine,
                detail = card.Detail,
                pulse = card.Pulse
            });

            if (item.Check?.Trim().ToLowerInvariant() is "habitat_mutate" or "session_trace")
            {
                var trObj = AdxMutateTrace.EvaluateRecent();
                using var trDoc = System.Text.Json.JsonDocument.Parse(
                    System.Text.Json.JsonSerializer.Serialize(trObj));
                var tr = trDoc.RootElement;
                var trOk = tr.GetProperty("ok").GetBoolean();
                if (!trOk) warn++;
                findings.Add(new
                {
                    id = "ADX-HX-001.trace",
                    severity = trOk ? "ok" : "warn",
                    status = trOk ? "pass" : "warn",
                    topic = "habitat.mutate.trace",
                    statement = "Recent harness mutates — set_text on existing = warn",
                    detail = trObj,
                    pulse = tr.GetProperty("pulse").GetString()
                });
            }
        }

        var pulse = fail > 0 ? $"assert FAIL×{fail}"
            : warn > 0 ? $"assert ok · WARN×{warn}"
            : deferred > 0 ? $"assert ok · deferred×{deferred}"
            : "assert ok";

        return new
        {
            schema = SchemaVersion,
            ok = fail == 0,
            scope = "assert",
            pulse,
            fail,
            warn,
            deferred,
            catalog = path,
            findings = findings.ToArray(),
            next = fail > 0
                ? new { go = "quality", label = "Re-check", why = "scope=assert after fix" }
                : new { go = "pressure_desk", label = "Stash", why = "guidelines machine-green" },
            hint = "ADX: scope=assert runs kernels; Z3 proofs live in tests (no native Z3 in MCP publish)."
        };
    }

    static CheckResult RunCheck(string? check) => check?.Trim().ToLowerInvariant() switch
    {
        "recall_gate" => RecallSelfCheck(),
        "ignite_latch" => IgniteSelfCheck(),
        "habitat_mutate" or "session_trace" => HabitatSelfCheck(),
        _ => new CheckResult(false, "unknown_check", $"unknown check={check}")
    };

    static CheckResult RecallSelfCheck()
    {
        var cases = new (AdxRecallGateKernel.Gate From, AdxRecallGateKernel.Gate To, bool Ssot, bool Strict, bool Expect)[]
        {
            (AdxRecallGateKernel.Gate.Pull, AdxRecallGateKernel.Gate.Reconcile, false, false, true),
            (AdxRecallGateKernel.Gate.Reconcile, AdxRecallGateKernel.Gate.Align, false, false, true),
            (AdxRecallGateKernel.Gate.Align, AdxRecallGateKernel.Gate.Ready, false, false, true),
            (AdxRecallGateKernel.Gate.Pull, AdxRecallGateKernel.Gate.Ready, false, false, false),
            (AdxRecallGateKernel.Gate.Pull, AdxRecallGateKernel.Gate.Ready, true, false, true),
            (AdxRecallGateKernel.Gate.None, AdxRecallGateKernel.Gate.Pull, false, false, true),
            (AdxRecallGateKernel.Gate.None, AdxRecallGateKernel.Gate.Ready, true, false, true),
            (AdxRecallGateKernel.Gate.Ready, AdxRecallGateKernel.Gate.Pull, false, true, true),
        };

        foreach (var c in cases)
        {
            var got = AdxRecallGateKernel.IsAllowed(c.From, c.To, c.Ssot, c.Strict);
            if (got != c.Expect)
            {
                return new CheckResult(
                    false,
                    AdxRecallGateKernel.CheckCard(c.From, c.To, c.Ssot, c.Strict),
                    $"recall_gate mismatch {c.From}→{c.To} ssot={c.Ssot}");
            }

            if (AdxRecallGateKernel.IsForbiddenSkip(c.From, c.To, c.Ssot) && c.Expect)
                return new CheckResult(false, null, "forbidden_skip marked allowed");
        }

        return new CheckResult(true, new { cases = cases.Length }, "recall_gate ok");
    }

    static CheckResult IgniteSelfCheck()
    {
        if (!AdxIgniteLatchKernel.HaltWorldOk(autonomous: false, hild: false, awaitPartner: true))
            return new CheckResult(false, null, "halt happy path failed");
        if (AdxIgniteLatchKernel.HaltWorldOk(autonomous: true, hild: false, awaitPartner: true))
            return new CheckResult(false, null, "halt must clear autonomous");
        if (!AdxIgniteLatchKernel.LastOnceFireAwaitingOk(lastOnce: true, fired: true, awaiting: true))
            return new CheckResult(false, null, "last_once awaiting happy path failed");
        if (AdxIgniteLatchKernel.LastOnceFireAwaitingOk(lastOnce: true, fired: true, awaiting: false))
            return new CheckResult(false, null, "last_once fire without awaiting must fail");
        if (!AdxIgniteLatchKernel.NotArmedWhileAwaiting(hasArmedTimer: false, awaitPartner: true))
            return new CheckResult(false, null, "await-only must be ok");
        if (AdxIgniteLatchKernel.NotArmedWhileAwaiting(hasArmedTimer: true, awaitPartner: true))
            return new CheckResult(false, null, "armed+await must fail");

        return new CheckResult(true, new { halt = true, last_once = true }, "ignite_latch ok");
    }

    static CheckResult HabitatSelfCheck()
    {
        if (!AdxHabitatMutateKernel.GuidelineOk(isCreate: true, pathExistedBefore: false, editOp: "create"))
            return new CheckResult(false, null, "create bootstrap failed");
        if (!AdxHabitatMutateKernel.GuidelineOk(isCreate: false, pathExistedBefore: false, editOp: "set_text"))
            return new CheckResult(false, null, "first-write set_text failed");
        if (!AdxHabitatMutateKernel.GuidelineOk(isCreate: false, pathExistedBefore: true, editOp: "anchor"))
            return new CheckResult(false, null, "anchor on existing failed");
        if (AdxHabitatMutateKernel.GuidelineOk(isCreate: false, pathExistedBefore: true, editOp: "set_text"))
            return new CheckResult(false, null, "set_text on existing must fail guideline");

        var trace = AdxMutateTrace.EvaluateRecent();
        return new CheckResult(true, new { kernel = true, trace }, "habitat_mutate ok");
    }

    static List<CatalogItem> LoadCatalog(string path)
    {
        try
        {
            var doc = TomlSerializer.Deserialize<AssertionsToml>(
                File.ReadAllText(path),
                new TomlSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
            var items = doc?.Assertions?.Item;
            if (items is null || items.Count == 0)
                return [];

            return items
                .Select(i => new CatalogItem(
                    i.Id ?? "?",
                    i.Kind,
                    i.Topic,
                    i.Engine,
                    i.Statement,
                    i.Check,
                    i.Status))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    sealed record CatalogItem(
        string Id,
        string? Kind,
        string? Topic,
        string? Engine,
        string? Statement,
        string? Check,
        string? Status);

    sealed record CheckResult(bool Ok, object? Detail, string Pulse);

    sealed class AssertionsToml
    {
        public AssertionsSection? Assertions { get; set; }
    }

    sealed class AssertionsSection
    {
        public string? Schema { get; set; }
        public List<AssertionItemToml>? Item { get; set; }
    }

    sealed class AssertionItemToml
    {
        public string? Id { get; set; }
        public string? Kind { get; set; }
        public string? Topic { get; set; }
        public string? Engine { get; set; }
        public string? Statement { get; set; }
        public string? Check { get; set; }
        public string? Status { get; set; }
    }
}
