using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace CdpMcp;

internal static class CdpServiceToken
{
    internal static string Ensure(CdpServiceSettings settings)
    {
        var path = settings.ResolveTokenPath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Trim();
            if (existing.Length >= 16)
                return existing;
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        File.WriteAllText(path, token);
        return token;
    }

    internal static bool IsAuthorized(HttpContext context, CdpServiceSettings settings)
    {
        var expected = Ensure(settings);
        if (string.IsNullOrEmpty(expected))
            return false;

        if (!context.Request.Headers.TryGetValue("Authorization", out var auth))
            return false;

        var header = auth.ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var provided = header[prefix.Length..].Trim();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(provided));
    }
}
