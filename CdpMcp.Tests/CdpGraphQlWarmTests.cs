#nullable enable
using System.Text;
using CdpMcp.GraphQl;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CdpMcp.Tests;

public class CdpGraphQlWarmTests
{
    [Fact]
    public async Task Schema_builds_and_typename_query_works()
    {
        var services = new ServiceCollection();
        services.AddCdpGraphQl();
        await using var sp = services.BuildServiceProvider();
        var resolver = sp.GetRequiredService<IRequestExecutorManager>();
        IRequestExecutor executor;
        try
        {
            executor = await resolver.GetExecutorAsync();
        }
        catch (SchemaException ex)
        {
            var sb = new StringBuilder();
            foreach (var e in ex.Errors)
            {
                sb.AppendLine(e.Message);
                sb.AppendLine("  code=" + e.Code);
                if (e.Exception is not null)
                    sb.AppendLine("  ex=" + e.Exception.GetType().Name + ": " + e.Exception.Message);
                foreach (var (k, v) in e.Extensions ?? new Dictionary<string, object?>())
                    sb.AppendLine("  ext." + k + "=" + v);
            }
            throw new Xunit.Sdk.XunitException("Schema warm failed:\n" + sb);
        }

        Assert.Equal("Query", executor.Schema.QueryType.Name);
        Assert.Contains(executor.Schema.QueryType.Fields, f => f.Name == "textHits");
        Assert.Contains(executor.Schema.QueryType.Fields, f => f.Name == "peek");
        Assert.Contains(executor.Schema.QueryType.Fields, f => f.Name == "diagnostics");
    }

    [Fact]
    public void LikeToRg_translates_percent_and_underscore()
    {
        Assert.Equal("^.*FindInFiles.*$", LikeToRg.Translate("%FindInFiles%"));
        Assert.Equal("^A.B$", LikeToRg.Translate("A_B"));
    }
}
