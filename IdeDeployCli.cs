#nullable enable
using System.Text.Json;
using Cdp.Core;
using Cdp.Deploy;

namespace CdpMcp;

internal static class IdeDeployCli
{
    public static int Run(string payloadPath)
    {
        var json = File.ReadAllText(payloadPath);
        var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
                   ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var session = new SessionContext
        {
            ProjectRoot = CdpDeploySource.TryResolve(AppContext.BaseDirectory)?.RepoRoot
                          ?? CdpDeploySource.TryResolve(IdeDeploy.ResolveSelfInstallRoot())?.RepoRoot
        };
        var result = IdeDeploy.Run(session, args);
        Console.WriteLine(result);        try
        {
            using var doc = JsonDocument.Parse(result);
            if (doc.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
                return 0;
        }
        catch
        {
            /* fall through */
        }

        return 1;
    }
}
