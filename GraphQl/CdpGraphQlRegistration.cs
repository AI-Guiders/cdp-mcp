#nullable enable
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace CdpMcp.GraphQl;

/// <summary>HotChocolate wiring for CdpService (CDP-ADR-0233).</summary>
internal static class CdpGraphQlRegistration
{
    public const string HttpPath = "/api/v1/cdp/graphql";
    public const string SchemaPath = "/api/v1/cdp/graphql/schema";

    public static IServiceCollection AddCdpGraphQl(this IServiceCollection services)
    {
        services
            .AddGraphQLServer()
            .AddQueryType<CdpQueryType>()
            .AddType<AnchorType>()
            .AddType<PackagesReadQueryType>()
            .AddErrorFilter<CdpGraphQlDidYouMeanFilter>()
            .ModifyRequestOptions(o => o.IncludeExceptionDetails = true);

        return services;
    }

    public static WebApplication MapCdpGraphQl(this WebApplication app)
    {
        CdpGraphQlRuntime.Services = app.Services;
        app.MapGraphQL(HttpPath);
        app.MapGraphQLSchema(SchemaPath);
        return app;
    }

    public static async Task WarmExecutorAsync(IServiceProvider services, CancellationToken ct = default)
    {
        CdpGraphQlRuntime.Services = services;
        await CdpGraphQlRuntime.EnsureExecutorAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>In-proc executor for MCP cdp_graphql (same process as CdpService).</summary>
internal static class CdpGraphQlRuntime
{
    static readonly object Gate = new();
    static Task<IRequestExecutor>? _warming;

    public static IServiceProvider? Services { get; set; }
    public static IRequestExecutor? Executor { get; set; }
    public static string? LastWarmError { get; set; }

    public static async Task<IRequestExecutor?> EnsureExecutorAsync(CancellationToken ct = default)
    {
        if (Executor is not null)
            return Executor;

        var services = Services;
        if (services is null)
        {
            LastWarmError = "services_null";
            return null;
        }

        Task<IRequestExecutor> warm;
        lock (Gate)
        {
            if (Executor is not null)
                return Executor;
            _warming ??= WarmCoreAsync(services, ct);
            warm = _warming;
        }

        try
        {
            var exec = await warm.ConfigureAwait(false);
            Executor = exec;
            LastWarmError = null;
            return exec;
        }
        catch (Exception ex)
        {
            LastWarmError = FormatWarmError(ex);
            lock (Gate) { _warming = null; }
            Console.Error.WriteLine("CdpGraphQl warm failed (ADR-0233): " + LastWarmError);
            return null;
        }
    }

    static async Task<IRequestExecutor> WarmCoreAsync(IServiceProvider services, CancellationToken ct)
    {
        var resolver = services.GetRequiredService<IRequestExecutorManager>();
        return await resolver.GetExecutorAsync(cancellationToken: ct).ConfigureAwait(false);
    }

    static string FormatWarmError(Exception ex)
    {
        if (ex is SchemaException se)
        {
            var parts = se.Errors.Select((e, i) =>
                (i + 1) + ". " + e.Message
                + (string.IsNullOrEmpty(e.Code) ? "" : " [" + e.Code + "]"));
            return "SchemaException: " + string.Join(" ", parts);
        }
        return ex.GetType().Name + ": " + ex.Message
            + (ex.InnerException is null ? "" : " | inner: " + ex.InnerException.Message);
    }
}
