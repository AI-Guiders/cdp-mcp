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

    public static IServiceCollection AddCdpGraphQl(this IServiceCollection services)
    {
        services
            .AddGraphQLServer()
            .AddQueryType<CdpQueryRoot>()
            .AddType<AnchorType>()
            .AddErrorFilter<CdpGraphQlDidYouMeanFilter>()
            .ModifyRequestOptions(o => o.IncludeExceptionDetails = true)
            .DisableIntrospection(false);

        return services;
    }

    public static WebApplication MapCdpGraphQl(this WebApplication app)
    {
        app.MapGraphQL(HttpPath).WithOptions(new HotChocolate.AspNetCore.GraphQLServerOptions
        {
            Tool = { Enable = true }
        });
        return app;
    }

    public static async Task WarmExecutorAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var resolver = services.GetRequiredService<IRequestExecutorResolver>();
        var executor = await resolver.GetRequestExecutorAsync(cancellationToken: ct).ConfigureAwait(false);
        CdpGraphQlRuntime.Executor = executor;
    }
}

/// <summary>In-proc executor for MCP cdp_graphql (same process as CdpService).</summary>
internal static class CdpGraphQlRuntime
{
    public static IRequestExecutor? Executor { get; set; }
}
