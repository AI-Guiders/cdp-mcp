using System.Diagnostics;
using System.Net.Http.Json;
using System.Collections.Frozen;
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace CdpMcpBridge;

/// <summary>
/// Bridge CallTool router — deploy gap survival, durable lifecycle fallback, deploy waiter (ADR-0203).
/// </summary>
internal sealed class CdpBridgeInvokeRouter
{
    readonly CdpBridgeSettings _settings;
    readonly HttpClient _http;
    readonly CdpBridgeServiceEnsurer _ensurer;
    readonly JsonSerializerOptions _jsonOptions;
    readonly CdpBridgeTiming _timing;

    internal CdpBridgeInvokeRouter(
        CdpBridgeSettings settings,
        HttpClient http,
        CdpBridgeServiceEnsurer ensurer,
        JsonSerializerOptions jsonOptions,
        CdpBridgeTiming? timing = null)
    {
        _settings = settings;
        _http = http;
        _ensurer = ensurer;
        _jsonOptions = jsonOptions;
        _timing = timing ?? CdpBridgeTiming.Resolve();
    }

    internal async Task<CallToolResult> InvokeAsync(
        string toolName,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        if (toolName.Equals("cdp_deploy", StringComparison.OrdinalIgnoreCase)
            && CdpBridgeDeployPolicy.ShouldBridgeWait(args))
        {
            return await DeployWithBridgeWaitAsync(args, cancellationToken).ConfigureAwait(false);
        }

        if (toolName.Equals("cdp_lifecycle_last", StringComparison.OrdinalIgnoreCase))
            return await LifecycleLastAsync(args, cancellationToken).ConfigureAwait(false);

        if (toolName.Equals("cdp_lifecycle_scene", StringComparison.OrdinalIgnoreCase))
            return await LifecycleSceneAsync(cancellationToken).ConfigureAwait(false);

        if (toolName.Equals("cdp_health", StringComparison.OrdinalIgnoreCase))
            return await HealthAsync(args, cancellationToken).ConfigureAwait(false);

        return await ForwardAsync(toolName, args, CdpBridgeInvokeContext.Default, cancellationToken)
            .ConfigureAwait(false);
    }

    async Task<CallToolResult> DeployWithBridgeWaitAsync(
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var forwardArgs = CdpBridgeDeployPolicy.PrepareForwardDeployArgs(args);
        CallToolResult enqueue;
        try
        {
            enqueue = await ForwardAsync(
                    "cdp_deploy",
                    forwardArgs,
                    CdpBridgeInvokeContext.Default,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (CdpBridgeServiceEnsurer.IsConnectionFailure(ex))
        {
            return await DeployViaLocalWorkerAsync(forwardArgs, started, cancellationToken)
                .ConfigureAwait(false);
        }

        if (enqueue.IsError == true)
            return enqueue;

        var enqueueBody = Text(enqueue);
        var jobId = CdpBridgeDurableAccess.TryParseJobId(enqueueBody);
        if (jobId is null || !CdpBridgeDurableAccess.IsRunningState(enqueueBody))
            return AnnotateBridgeWait(enqueueBody, started.ElapsedMilliseconds, polls: 0, waitedForService: false);

        var polls = 0;
        string? finalJson = null;
        var deadline = DateTime.UtcNow + _timing.DeployWaitTimeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            polls++;
            finalJson = CdpBridgeDurableAccess.ReadLifecycleLast(
                new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                {
                    ["job_id"] = JsonSerializer.SerializeToElement(jobId)
                });

            if (!CdpBridgeDurableAccess.IsRunningState(finalJson))
                break;

            await Task.Delay(_timing.DeployPollInterval, cancellationToken).ConfigureAwait(false);
        }

        if (finalJson is null || CdpBridgeDurableAccess.IsRunningState(finalJson))
        {
            return Error(
                $"Bridge deploy wait timed out after {_timing.DeployWaitTimeout.TotalSeconds:0}s " +
                $"(job_id={jobId}). Durable supervisor may still be running — poll cdp_lifecycle_last job_id={jobId}.");
        }

        var deployOk = CdpBridgeDurableAccess.TryParseJobOk(finalJson, out var ok) && ok;
        var waitedForService = false;
        string? healthBody = null;
        if (deployOk)
        {
            waitedForService = await WaitForServiceHealthyAsync(cancellationToken).ConfigureAwait(false);
            if (waitedForService)
            {
                try
                {
                    var health = await ForwardAsync(
                            "cdp_health",
                            new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase),
                            CdpBridgeInvokeContext.Default,
                            cancellationToken)
                        .ConfigureAwait(false);
                    healthBody = Text(health);
                }
                catch
                {
                    /* best-effort */
                }
            }
        }

        var merged = MergeDeployWait(finalJson, started.ElapsedMilliseconds, polls, waitedForService, healthBody, jobId);
        var failed = !deployOk;
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = merged }],
            IsError = failed
        };
    }

    async Task<CallToolResult> DeployViaLocalWorkerAsync(
        IReadOnlyDictionary<string, JsonElement> args,
        Stopwatch started,
        CancellationToken cancellationToken)
    {
        var deployJson = await CdpBridgeDeployRunner.RunViaWorkerAsync(args, cancellationToken)
            .ConfigureAwait(false);
        var deployOk = CdpBridgeDurableAccess.TryParseJobOk(deployJson, out var ok) && ok;
        var waitedForService = false;
        string? healthBody = null;
        if (deployOk)
        {
            waitedForService = await WaitForServiceHealthyAsync(cancellationToken).ConfigureAwait(false);
            if (waitedForService)
            {
                try
                {
                    var health = await ForwardAsync(
                            "cdp_health",
                            new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase),
                            CdpBridgeInvokeContext.Default,
                            cancellationToken)
                        .ConfigureAwait(false);
                    healthBody = Text(health);
                }
                catch
                {
                    /* best-effort */
                }
            }
        }

        var merged = MergeDeployWait(deployJson, started.ElapsedMilliseconds, polls: 0, waitedForService, healthBody, jobId: null);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = merged }],
            IsError = !deployOk
        };
    }

    async Task<CallToolResult> LifecycleLastAsync(
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ForwardAsync("cdp_lifecycle_last", args, ResolveGapContext(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception) when (CdpBridgeDurableAccess.HasInFlightDeploy() || ShouldUseLocalLifecycleFallback())
        {
            return Ok(CdpBridgeDurableAccess.ReadLifecycleLast(args));
        }
    }

    async Task<CallToolResult> LifecycleSceneAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await ForwardAsync(
                    "cdp_lifecycle_scene",
                    FrozenDictionary<string, JsonElement>.Empty,
                    ResolveGapContext(),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception) when (CdpBridgeDurableAccess.HasInFlightDeploy() || ShouldUseLocalLifecycleFallback())
        {
            return Ok(CdpBridgeDurableAccess.ReadLifecycleScene());
        }
    }

    async Task<CallToolResult> HealthAsync(
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ForwardAsync("cdp_health", args, ResolveGapContext(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception) when (CdpBridgeDurableAccess.HasInFlightDeploy())
        {
            return Ok(BuildDeployGapHealth());
        }
    }

    CdpBridgeInvokeContext ResolveGapContext() =>
        CdpBridgeDurableAccess.HasInFlightDeploy()
            ? CdpBridgeInvokeContext.DeployGap(_timing)
            : CdpBridgeInvokeContext.Default;

    static bool ShouldUseLocalLifecycleFallback() => true;

    async Task<bool> WaitForServiceHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await CdpBridgeTransport.WithRetryAsync(
                    _ensurer,
                    CdpBridgeInvokeContext.ServiceReady(_timing),
                    async ct =>
                    {
                        using var response = await _http.GetAsync("/healthz", ct).ConfigureAwait(false);
                        response.EnsureSuccessStatusCode();
                        return true;
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    async Task<CallToolResult> ForwardAsync(
        string toolName,
        IReadOnlyDictionary<string, JsonElement> args,
        CdpBridgeInvokeContext ctx,
        CancellationToken cancellationToken)
    {
        return await CdpBridgeTransport.WithRetryAsync(
                _ensurer,
                ctx,
                ct => PostInvokeAsync(toolName, args, ct),
                cancellationToken)
            .ConfigureAwait(false);
    }

    async Task<CallToolResult> PostInvokeAsync(
        string toolName,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        var payload = new CdpInvokeRequest
        {
            Tool = toolName,
            Arguments = args.Count == 0
                ? null
                : args.ToDictionary(static p => p.Key, static p => p.Value)
        };
        using var response = await _http.PostAsJsonAsync("/api/v1/cdp/invoke", payload, _jsonOptions, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadFromJsonAsync<CdpInvokeResponse>(_jsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? new CdpInvokeResponse
            {
                Success = false,
                Body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)
            };
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = body.Body }],
            IsError = !body.Success
        };
    }

    static CallToolResult Ok(string body) =>
        new()
        {
            Content = [new TextContentBlock { Text = body }],
            IsError = false
        };

    static CallToolResult Error(string message) =>
        new()
        {
            Content = [new TextContentBlock { Text = $"Error: {message}" }],
            IsError = true
        };

    static string Text(CallToolResult result) =>
        result.Content?.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";

    static CallToolResult AnnotateBridgeWait(string body, long waitedMs, int polls, bool waitedForService) =>
        Ok(MergeDeployWait(body, waitedMs, polls, waitedForService, healthBody: null, jobId: null));

    static string MergeDeployWait(
        string deployJson,
        long waitedMs,
        int polls,
        bool waitedForService,
        string? healthBody,
        string? jobId)
    {
        try
        {
            using var doc = JsonDocument.Parse(deployJson);
            var writer = new MemoryStream();
            using (var w = new Utf8JsonWriter(writer, new JsonWriterOptions { Indented = true }))
            {
                w.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                    prop.WriteTo(w);

                w.WritePropertyName("bridge_wait");
                w.WriteStartObject();
                w.WriteNumber("waited_ms", waitedMs);
                w.WriteNumber("polls", polls);
                w.WriteBoolean("service_ready", waitedForService);
                if (jobId is not null)
                    w.WriteString("job_id", jobId);
                w.WriteString(
                    "hint",
                    "Bridge held CallTool until durable deploy finished — no shell escape or manual lifecycle poll.");
                w.WriteEndObject();

                if (healthBody is not null)
                {
                    w.WritePropertyName("post_deploy_health");
                    using var healthDoc = JsonDocument.Parse(healthBody);
                    healthDoc.RootElement.WriteTo(w);
                }

                w.WriteEndObject();
            }

            return System.Text.Encoding.UTF8.GetString(writer.ToArray());
        }
        catch
        {
            return deployJson;
        }
    }

    string BuildDeployGapHealth()
    {
        var jobId = CdpBridgeDurableAccess.InFlightDeployJobId();
        return JsonSerializer.Serialize(new
        {
            ok = true,
            detail = "bridge_deploy_gap",
            bridge_deploy_gap = true,
            deploy_job_id = jobId,
            service = "CdpService",
            service_url = _settings.BaseUrl.ToString(),
            hint = "CdpService is restarting during durable deploy. Bridge reads lifecycle from disk; " +
                   "cdp_deploy apply/hard/rollout blocks until job completes. No terminal_* escape."
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
