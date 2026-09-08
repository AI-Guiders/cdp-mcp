#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CdpMcp;

/// <summary>
/// Static facade over <see cref="CdpIgniteArmHost"/> — ADR-0219 L2b transition seam.
/// Legacy static API preserved for host call sites + tests; L3 removes this seam.
/// </summary>
internal static partial class IdeIgniteArmHost
{
    // ── Composition root (transition seam — L3 removes) ──
    public static Func<CdpIgniteArmHost>? InstanceOverrideForTests { get; set; }

    static readonly Lazy<CdpIgniteArmHost> _default = new(
        () => InstanceOverrideForTests?.Invoke() ?? new CdpIgniteArmHost(),
        LazyThreadSafetyMode.ExecutionAndPublication);

    internal static CdpIgniteArmHost Default => _default.Value;

    public static void WarmDefault() => _ = Default;

    // ── Nested model — test surface: new IdeIgniteArmHost.IgniteArm ──
    internal sealed class IgniteArm
    {
        public string Id { get; set; } = "";
        public string Event { get; set; } = "timer";
        public string Message { get; set; } = "";
        /// <summary>minimal (default): fire canonical wake charge; custom/expand/legacy: stored message templates (discouraged).</summary>
        public string ChargeMode { get; set; } = "minimal";
        public string? Task { get; set; }
        /// <summary>Wake provenance for agent (e.g. oom) — not TM body.</summary>
        public string? Reason { get; set; }
        public string? Chat { get; set; }
        /// <summary>ADR-0200: MCP conversation id stamped at arm — fire-time latch lookup.</summary>
        public string? ConversationId { get; set; }
        public int Port { get; set; } = IdeIgniteChannel.DefaultPort;
        public bool Once { get; set; } = true;
        /// <summary>Await-operator latch: after successful fire → status=awaiting; block repeat last_once arms.</summary>
        public bool LastOnce { get; set; }
        public bool OkOnly { get; set; } = true;
        public int SettleSeconds { get; set; } = 8;
        public int WaitSeconds { get; set; } = 90;
        public DateTimeOffset? DueUtc { get; set; }
        public string? InRaw { get; set; }
        /// <summary>ADR-0200: tenant wire stamped at arm time — fire enters slice for TM/wake/flight.</summary>
        public string? TenantWire { get; set; }
        /// <summary>
        /// Wake seat: cursor (CDT Composer, default) | opencode (sidecar HTTP) | citizen (Completions).
        /// Agent stamps harness= at arm — no env/heuristic routing. session= required when harness=opencode.
        /// </summary>
        public string Harness { get; set; } = "cursor";
        /// <summary>OpenCode session id when Harness=opencode (ADR-0205).</summary>
        public string? OpencodeSession { get; set; }
        public string Status { get; set; } = "armed";
        public string? LastError { get; set; }
        public DateTimeOffset CreatedUtc { get; set; }
        public DateTimeOffset? FiredUtc { get; set; }
        public DateTimeOffset? SendInvokedUtc { get; set; }
        public bool? SendOk { get; set; }
        public string? SendError { get; set; }
        public DateTimeOffset? TranscriptObservedUtc { get; set; }
        public string? TranscriptPath { get; set; }
    }

    // ── Const aliases (compile-time) ──
    public const string StoreSchema = CdpIgniteArmHost.StoreSchema;
    public const string DeliveryNeedle = CdpIgniteArmHost.DeliveryNeedle;
    public const string LeafWakeArmId = CdpIgniteArmHost.LeafWakeArmId;
    public const string AutonomousSeedArmId = CdpIgniteArmHost.AutonomousSeedArmId;
    public const string AutonomousStoreSchema = CdpIgniteArmHost.AutonomousStoreSchema;
    public const string HildArmIdPrefix = CdpIgniteArmHost.HildArmIdPrefix;
    public const string HildAwayArmId = CdpIgniteArmHost.HildAwayArmId;
    public const string HildEscalateArmIdPrefix = CdpIgniteArmHost.HildEscalateArmIdPrefix;
    public const string HildEscalateArmId = CdpIgniteArmHost.HildEscalateArmId;
    public const string HildEscalateChargeMode = CdpIgniteArmHost.HildEscalateChargeMode;
    internal const string ProviderBlockedStatus = CdpIgniteArmHost.ProviderBlockedStatus;
    internal const string NewThreadRequiredError = CdpIgniteArmHost.NewThreadRequiredError;

    // ── Static API delegates ──
    public static string Seat => Default.Seat;
    public static string StorePath => Default.StorePath;
    public static string LegacyStorePath => Default.LegacyStorePath;
    public static string AutonomousStorePath => Default.AutonomousStorePath;

    public static void EnsureStarted() => Default.EnsureStarted();
    public static void Notify(string eventName, bool ok, string? pulse = null, string? detail = null) => Default.Notify(eventName, ok, pulse, detail);
    public static void NotifyPeerShip(string? pulse = null, string? detail = null) => Default.NotifyPeerShip(pulse, detail);
    internal static bool HasContinuityArms() => Default.HasContinuityArms();
    internal static async Task WaitForArmDeliveryAsync(string armId, TimeSpan timeout) => await Default.WaitForArmDeliveryAsync(armId, timeout).ConfigureAwait(false);
    public static IReadOnlyList<IgniteArm> Snapshot() => Default.Snapshot();

    public static object Arm(IReadOnlyDictionary<string, JsonElement> args) => Default.Arm(args);
    public static object Disarm(IReadOnlyDictionary<string, JsonElement> args) => Default.Disarm(args);
    public static object Delivery(IReadOnlyDictionary<string, JsonElement> args) => Default.Delivery(args);
    public static object Watchdog(IReadOnlyDictionary<string, JsonElement> args) => Default.Watchdog(args);
    public static object Resume(IReadOnlyDictionary<string, JsonElement> args) => Default.Resume(args);
    public static object List() => Default.List();
    public static object Hygiene() => Default.Hygiene();
    public static object Plateau() => Default.Plateau();
    public static object Continuity() => Default.Continuity();
    public static object SceneSlice() => Default.SceneSlice();
    public static object AwaitOperator(IReadOnlyDictionary<string, JsonElement>? args = null) => Default.AwaitOperator(args);
    public static object AwaitPartner(IReadOnlyDictionary<string, JsonElement>? args = null) => Default.AwaitPartner(args);
    public static object WakeAfterHardDeploy() => Default.WakeAfterHardDeploy();
    public static object ArmForLeaf(string taskTitle, string reason) => Default.ArmForLeaf(taskTitle, reason);
    public static object Autonomous(IReadOnlyDictionary<string, JsonElement>? args = null) => Default.Autonomous(args);
    public static object Hild(IReadOnlyDictionary<string, JsonElement>? args = null) => Default.Hild(args);
    public static object AutonomousContinue(string reason) => Default.AutonomousContinue(reason);
    public static bool TryParseDuration(string raw, out TimeSpan span) => Default.TryParseDuration(raw, out span);
    public static string NormalizeEvent(string? raw) => Default.NormalizeEvent(raw);

    public static bool IsAutonomousArmed() => Default.IsAutonomousArmed();
    public static void BindAutonomous(bool? armed) => Default.BindAutonomous(armed);
    public static object SetAutonomous(bool armed, string? why = null) => Default.SetAutonomous(armed, why);
    public static object SetHild(bool armed, string? why = null) => Default.SetHild(armed, why);
    internal static void StartHildWatch() => Default.StartHildWatch();
    public static void BindFlightProbe(Func<ContinuityFlight> probe) => Default.BindFlightProbe(probe);
    public static void BindCitizenFocusLane(Action bind) => Default.BindCitizenFocusLane(bind);
    public static void BindTaskFocus(Func<bool> probe) => Default.BindTaskFocus(probe);
    internal static void TryApplyCitizenFocusLane() => Default.TryApplyCitizenFocusLane();
    internal static void BindPrimaryAutoiSeat(bool? primary) => Default.BindPrimaryAutoiSeat(primary);
    internal static void BindTenantResolver(Func<CdpTenantKey, CdpTenantSlice> resolve) => Default.BindTenantResolver(resolve);
    internal static void BindIncompleteLeafTitleProbe(Func<string?>? probe) => Default.BindIncompleteLeafTitleProbe(probe);
    internal static void RegisterProviderBlockedHook(Action<IgniteArm>? hook) => Default.RegisterProviderBlockedHook(hook);
    public static void PublishGlass() => Default.PublishGlass();

    internal static string Verdict(IgniteArm a) => Default.Verdict(a);
    internal static bool IsSoftDeliveredError(string? error) => Default.IsSoftDeliveredError(error);
    internal static bool IsSystemWakeArmId(string? id) => Default.IsSystemWakeArmId(id);
    internal static bool IsHabitatPartnerLive(DateTimeOffset? nowUtc = null) => Default.IsHabitatPartnerLive(nowUtc);
    internal static bool ShouldEnterProviderBlockedContinuity(string? fireError) => Default.ShouldEnterProviderBlockedContinuity(fireError);
    internal static bool ArmTenantWireEquals(IgniteArm arm, string? scopeWire) => Default.ArmTenantWireEquals(arm, scopeWire);
    internal static bool IsComposerBusyKind(string kind) => Default.IsComposerBusyKind(kind);
    internal static string FormatHabitatIntercomRadio(IgniteArm? arm, string charge) => Default.FormatHabitatIntercomRadio(arm, charge);
    internal static bool ShouldSkipCdtAfterIntercomMirror(bool sampleOk, string kind) => Default.ShouldSkipCdtAfterIntercomMirror(sampleOk, kind);
    internal static Task<object?> TryDeliverMirroredWhenComposerBusyAsync(IgniteArm arm, string charge, bool intercomMirrored, CancellationToken ct) => Default.TryDeliverMirroredWhenComposerBusyAsync(arm, charge, intercomMirrored, ct);
    internal static bool TryMutateForTests(string id, Action<IgniteArm> mutate) => Default.TryMutateForTests(id, mutate);
    internal static object? TryScheduleRemountInitializedWake(string? seatOverride = null) => Default.TryScheduleRemountInitializedWake(seatOverride);
    internal static bool TrySuppressAutonomousSeedBeforeDelivery(IgniteArm arm) => Default.TrySuppressAutonomousSeedBeforeDelivery(arm);
    internal static bool LooksLikePeerShipSignal(string? body, string? kind, string? name) => Default.LooksLikePeerShipSignal(body, kind, name);
    internal static bool IsPrimaryAutoiSeat() => Default.IsPrimaryAutoiSeat();
    internal static bool TryClaimSharedWakeMirror(string armId) => Default.TryClaimSharedWakeMirror(armId);
    internal static string SharedWakeMirrorClaimPath() => CdpIgniteArmHost.SharedWakeMirrorClaimPath();
    public const int SlimMessageKeepChars = 160;
    internal static bool LooksLikeHabitatRadioPointer(string? body) => Default.LooksLikeHabitatRadioPointer(body);
    internal static string? ResolveChatFromTenantLatch(string? tenantWire, string? conversationId, string? armChat) => Default.ResolveChatFromTenantLatch(tenantWire, conversationId, armChat);
    internal static bool IsEventTriggeredArm(string? eventName) => Default.IsEventTriggeredArm(eventName);
    internal static object? TryScheduleOomWake(string lastError = "cdt_recovered_after_down") => Default.TryScheduleOomWake(lastError);
    internal static string? TryArmId(object? slim) => Default.TryArmId(slim);
    public static object Halt(IReadOnlyDictionary<string, JsonElement>? args = null) => Default.Halt(args);
    internal static object ContinuitySlice(IReadOnlyList<IgniteArm>? list = null) => Default.ContinuitySlice(list);
    public static object HildStatusPayload(string? why = null) => Default.HildStatusPayload(why);
    internal static string ContinuityPulseLine(IReadOnlyList<IgniteArm>? list = null) => Default.ContinuityPulseLine(list);
    internal static Guid? ResolveWakeLeafId(IntentWorkspace.IntentWorkspaceStore store, IntentWorkspace.IntentWorkspaceState state) => Default.ResolveWakeLeafId(store, state);
    internal static IdeHildDetector HildDetectorForTests => Default.HildDetectorForTests;
    internal static bool HasArmedRemountWake() => Default.HasArmedRemountWake();
    internal static bool HasArmedInventOnlyHoldInsurance() => Default.HasArmedInventOnlyHoldInsurance();
    internal static bool HasArmedLastOnceInsurance() => Default.HasArmedLastOnceInsurance();
    internal const string HildEscalateReason = CdpIgniteArmHost.HildEscalateReason;
    internal static void BindHild(bool? armed) => Default.BindHild(armed);
    internal static void CancelInFlightFire(string id) => Default.CancelInFlightFire(id);
    internal static CancellationTokenSource AttachFireTokenForTests(string id) => Default.AttachFireTokenForTests(id);
    internal static bool MayPreferHabitatOverComposer(IgniteArm arm) => Default.MayPreferHabitatOverComposer(arm);
    internal static bool IsIntercomVoiceCannonArmId(string? id) => Default.IsIntercomVoiceCannonArmId(id);
    internal static bool IsRemountWakeArm(IgniteArm arm) => Default.IsRemountWakeArm(arm);
    internal static bool IsHildEscalateWakeArm(IgniteArm arm) => Default.IsHildEscalateWakeArm(arm);
    internal static bool IsHildAwayWakeArm(IgniteArm arm) => Default.IsHildAwayWakeArm(arm);
    internal static bool IsOomWakeArm(IgniteArm arm) => Default.IsOomWakeArm(arm);
    internal static bool IsToolWakeArmId(string? id) => Default.IsToolWakeArmId(id);
    internal static bool IsSupersedableContinuityWorkTimer(IgniteArm a) => Default.IsSupersedableContinuityWorkTimer(a);
    internal static bool ShouldLatchAwaitingPartnerAfterSuccessfulFire(bool lastOnce, bool autonomousArmed) => Default.ShouldLatchAwaitingPartnerAfterSuccessfulFire(lastOnce, autonomousArmed);
    internal static bool ShouldRequeueBusy(string eventName, string? error) => Default.ShouldRequeueBusy(eventName, error);
    internal static bool ShouldKeepVisibleErrorOnFireFail(bool once, bool lastOnce) => Default.ShouldKeepVisibleErrorOnFireFail(once, lastOnce);
    internal static TimeSpan BusyBackoff(int waitSeconds) => Default.BusyBackoff(waitSeconds);
    internal static IReadOnlyList<string?> DistinctTenantWiresFromArmedWorkTimers() => Default.DistinctTenantWiresFromArmedWorkTimers();
    internal static object? TryScheduleHildEscalateWake() => Default.TryScheduleHildEscalateWake();
    internal static object? TryDeliverHabitatWake(IgniteArm arm, string charge) => Default.TryDeliverHabitatWake(arm, charge);
    internal static string FormatCitizenWakeIntercom(IgniteArm arm, string body) => Default.FormatCitizenWakeIntercom(arm, body);
    internal static bool MirrorTimerWakeToIntercom(IgniteArm arm, string charge) => Default.MirrorTimerWakeToIntercom(arm, charge);
    internal static string MirrorClaimKey(IgniteArm arm) => Default.MirrorClaimKey(arm);
    internal static bool ShouldHabitatSkipWhenComposerUnavailable(IgniteArm arm, bool sampleOk, string kind, bool autonomousArmed, bool duplexLive) => Default.ShouldHabitatSkipWhenComposerUnavailable(arm, sampleOk, kind, autonomousArmed, duplexLive);
    internal static bool MayDeliverHabitatWhenComposerUnavailable(IgniteArm arm) => Default.MayDeliverHabitatWhenComposerUnavailable(arm);
    internal static Task<object?> TryDeliverHabitatWhenComposerUnavailableAsync(IgniteArm arm, string charge, CancellationToken ct) => Default.TryDeliverHabitatWhenComposerUnavailableAsync(arm, charge, ct);
    internal static IReadOnlyList<string> ReclaimOverdue(TimeSpan? settle = null) => Default.ReclaimOverdue(settle);
    internal static (bool Observed, string? Path, int Scanned) ScanTranscriptsForNeedle(string needle, DateTimeOffset afterUtc, string? rootOverride, int maxFiles = 40) => Default.ScanTranscriptsForNeedle(needle, afterUtc, rootOverride, maxFiles);
    internal static TimeSpan ClampAutonomousLastOnceInsurance(TimeSpan requested, bool lastOnce, bool autonomous, bool force, out string? clampNote, bool partnerAway = false, bool leafFlying = false, bool inventOnlyHold = false) => Default.ClampAutonomousLastOnceInsurance(requested, lastOnce, autonomous, force, out clampNote, partnerAway, leafFlying, inventOnlyHold);
    internal static bool TryComputeHildAwayPullForwardDue(DateTimeOffset? dueUtc, bool lastOnce, bool isAutonomyMeans, string status, string? eventKind, DateTimeOffset now, out DateTimeOffset newDue, out string? note) => Default.TryComputeHildAwayPullForwardDue(dueUtc, lastOnce, isAutonomyMeans, status, eventKind, now, out newDue, out note);
    internal static bool TryComputeLeafFlyPullForwardDue(DateTimeOffset? dueUtc, bool lastOnce, bool isAutonomyMeans, string status, string? eventKind, DateTimeOffset now, out DateTimeOffset newDue, out string? note) => Default.TryComputeLeafFlyPullForwardDue(dueUtc, lastOnce, isAutonomyMeans, status, eventKind, now, out newDue, out note);
    internal static string LastOnceArmNextStep(bool autonomous) => Default.LastOnceArmNextStep(autonomous);
    internal static string LastOnceArmHint(bool autonomous) => Default.LastOnceArmHint(autonomous);
    internal static string ArmForLeafHint(bool autonomous, bool inventOnlyHold = false) => Default.ArmForLeafHint(autonomous, inventOnlyHold);
    internal static string ContinuityArmedNextStep(bool autonomous) => Default.ContinuityArmedNextStep(autonomous);
    internal static bool IsInventOnlyHoldTask(string? task) => Default.IsInventOnlyHoldTask(task);
    internal static bool TrySuppressLiveAutonomousSeedBeforeDelivery() => Default.TrySuppressLiveAutonomousSeedBeforeDelivery();
}