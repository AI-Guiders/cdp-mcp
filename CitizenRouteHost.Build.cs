#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent build — sync wait IdeSessionLifecycle.BuildAsync (organ parity; not cockpit W-spray).</summary>
internal static partial class CitizenRouteHost
{
    internal static Func<SessionContext?>? SessionResolver { get; set; }
    internal static Func<ICdpBackendModule?>? BuildModuleResolver { get; set; }

    /// <summary>Tests: inject fake edit JSON; live uses DocumentEditPlane.DispatchAsync.</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, object>? EditCallOverride { get; set; }
    internal static Func<IReadOnlyDictionary<string, JsonElement>, object>? UndoCallOverride { get; set; }
    internal static Func<IReadOnlyDictionary<string, JsonElement>, object>? ClipCallOverride { get; set; }
    internal static Func<IReadOnlyDictionary<string, JsonElement>, object>? ReplaceAllCallOverride { get; set; }
    internal static Func<IReadOnlyDictionary<string, JsonElement>, object>? NavCallOverride { get; set; }
    internal static Func<IReadOnlyDictionary<string, JsonElement>, object>? PutCallOverride { get; set; }
    internal static Func<IReadOnlyDictionary<string, JsonElement>, object>? ScratchCallOverride { get; set; }
    internal static Func<IReadOnlyDictionary<string, JsonElement>, object>? TakeCallOverride { get; set; }
    internal static Func<IReadOnlyDictionary<string, JsonElement>, object>? ShareCallOverride { get; set; }
    internal static Func<IReadOnlyDictionary<string, JsonElement>, object>? DiskCallOverride { get; set; }
    internal static Func<IReadOnlyDictionary<string, JsonElement>, object>? SniperCallOverride { get; set; }
    internal static Func<IReadOnlyDictionary<string, JsonElement>, object>? BufferCallOverride { get; set; }
    internal static Func<IReadOnlyDictionary<string, JsonElement>, object>? FindBufCallOverride { get; set; }

    internal static void PutIfPresent(Dictionary<string, JsonElement> args, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            args[key] = JsonSerializer.SerializeToElement(value);
    }

    internal static void PutBoolIfPresent(Dictionary<string, JsonElement> args, string key, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return;
        if (!TryParseLooseBool(raw.Trim(), out var b))
            return;
        args[key] = JsonSerializer.SerializeToElement(b);
    }

    internal static void PutIntIfPresent(Dictionary<string, JsonElement> args, string key, string? raw)
    {
        if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw.Trim(), out var n))
            args[key] = JsonSerializer.SerializeToElement(n);
    }

    static bool TryParseLooseBool(string raw, out bool value)
    {
        if (bool.TryParse(raw, out value))
            return true;
        if (raw.Equals("1", StringComparison.Ordinal) || raw.Equals("yes", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (raw.Equals("0", StringComparison.Ordinal) || raw.Equals("no", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        value = false;
        return false;
    }

    /// <summary>Tests / remount isolation.</summary>
    internal static void UnbindLifecycle()
    {
        SessionResolver = null;
        BuildModuleResolver = null;
        McpDispatchOverride = null;
        ShellHabitatResolver = null;
        ShellDefaultsResolver = null;
        ShellRunOverride = null;
        ShellOrganOverride = null;
        ByDomainResolver = null;
        DebugDispatchOverride = null;
        KbCallOverride = null;
        HciCallOverride = null;
        GitCallOverride = null;
        FindCallOverride = null;
        IdeCallOverride = null;
        IgniteHandleOverride = null;
        PressureHandleOverride = null;
        EditCallOverride = null;
        DeployCallOverride = null;
        UndoCallOverride = null;
        ClipCallOverride = null;
        ReplaceAllCallOverride = null;
        NavCallOverride = null;
        PutCallOverride = null;
        ScratchCallOverride = null;
        TakeCallOverride = null;
        ShareCallOverride = null;
        DiskCallOverride = null;
        SniperCallOverride = null;
        BufferCallOverride = null;
        FindBufCallOverride = null;
        BrowserHabitatResolver = null;
        BrowserDispatchOverride = null;
        RunLifecycleOverride = null;
        ScriptDispatchOverride = null;
        Ps1DispatchOverride = null;
        IcmHandleOverride = null;
        FilesHandleOverride = null;
        OnboardHandleOverride = null;
        PeelHandleOverride = null;
        EditPlanDispatchOverride = null;
        AnalysisDispatchOverride = null;
        TestPlanDispatchOverride = null;
        TestSceneDispatchOverride = null;
        GotoAllDispatchOverride = null;
        EditorSceneDispatchOverride = null;
        ManDispatchOverride = null;
        HealthDispatchOverride = null;
        ContextDispatchOverride = null;
        QualityHandleOverride = null;
        SessionDispatchOverride = null;
        CalendarHandleOverride = null;
        LandDispatchOverride = null;
        PkgDispatchOverride = null;
        ProjectDispatchOverride = null;
        SettingsDispatchOverride = null;
        RestoreDispatchOverride = null;
        IntercomHandleOverride = null;
        PresentationHandleOverride = null;
        ToolchainHandleOverride = null;
        CockpitHostHandleOverride = null;
        QrhHandleOverride = null;
        WebcamHandleOverride = null;
        EvidenceDispatchOverride = null;
        DomainHandleOverride = null;
        SaDispatchOverride = null;
        WorkDispatchOverride = null;
        CockpitDispatchOverride = null;
        MetaDispatchResolver = null;
    }

    static Applied RunBuild(CitizenIntentRouter.Route route)
    {
        var session = SessionResolver?.Invoke();
        if (session is null)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "build",
                Go: "build",
                Path: route.Path,
                Reason: "no_session");
        }

        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (route.Path is { Length: > 0 } path)
            args["path"] = JsonSerializer.SerializeToElement(path);

        var buildMod = BuildModuleResolver?.Invoke();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var json = IdeSessionLifecycle.BuildAsync(session, args, buildMod, cts.Token)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();

            var ok = TryReadLifecycleOk(json);
            var pulse = TryReadLifecyclePulse(json);
            var seat = IdeDeskSeats.PlaceOrgan("build");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "build",
                Seat: seat,
                Go: "build",
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "build_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "build",
                Go: "build",
                Path: route.Path,
                Reason: "build_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "build",
                Go: "build",
                Path: route.Path,
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static bool TryReadLifecycleOk(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var ok))
                return ok.ValueKind == JsonValueKind.True;
            if (root.TryGetProperty("success", out var success))
                return success.ValueKind == JsonValueKind.True;
            if (root.TryGetProperty("exit_code", out var code) && code.TryGetInt32(out var n))
                return n == 0;
            if (root.TryGetProperty("error_count", out var ec) && ec.TryGetInt32(out var errors))
                return errors == 0;
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static string? TryReadLifecycleError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var e)
                && e.ValueKind == JsonValueKind.String
                && e.GetString() is { Length: > 0 } err)
                return TruncPulse(err);
        }
        catch
        {
            /* best-effort */
        }

        return null;
    }

    static string? TryReadLifecyclePulse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String)
                return TruncPulse(p.GetString());
            if (root.TryGetProperty("ok", out var ok))
                return ok.ValueKind == JsonValueKind.True ? "ok" : "fail";
            if (root.TryGetProperty("exit_code", out var code) && code.TryGetInt32(out var n))
                return "exit=" + n;
            return TruncPulse(json);
        }
        catch
        {
            return TruncPulse(json);
        }
    }

    /// <summary>Observe inventory gaps×N — must fit full list (was 240/280; ×9 ≈277+).</summary>
    internal const int InventoryObservePulseMax = 480;

    internal static string? TruncPulse(string? s, int max = 240)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;
        s = s.Trim().Replace('\r', ' ').Replace('\n', ' ');
        // Default 240 for build/git; inventory observe passes InventoryObservePulseMax.
        // PeerAck EventPulseMax must stay ≥ inventory budget.
        if (max < 8)
            max = 8;
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }
}
