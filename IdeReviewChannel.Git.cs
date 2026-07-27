#nullable enable
using System.Diagnostics;

namespace CdpMcp;

internal static partial class IdeReviewChannel
{
    public static IReadOnlyList<FileCard> ListDirtyFiles(string? projectRoot)
    {
        if (projectRoot is not { Length: > 0 } || !Directory.Exists(projectRoot))
            return [];

        string porcelain;
        try
        {
            porcelain = RunGit(projectRoot, "status --porcelain -uall") ?? "";
        }
        catch
        {
            return [];
        }

        var cards = new List<FileCard>();
        foreach (var raw in porcelain.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (cards.Count >= MaxFiles)
                break;
            if (raw.Length < 3)
                continue;
            var status = raw[..2];
            var path = raw.Length > 3 ? raw[3..].Trim() : "";
            if (path.Contains(" -> ", StringComparison.Ordinal))
                path = path[(path.LastIndexOf(" -> ", StringComparison.Ordinal) + 4)..];
            if (path.Length == 0)
                continue;
            var (risk, why) = ScoreRisk(path, status);
            cards.Add(new FileCard(path, status.Trim(), risk, why, "review"));
        }

        return cards
            .OrderByDescending(c => RiskRank(c.Risk))
            .ThenBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    static int RiskRank(string risk) => risk switch
    {
        "secret" => 4,
        "high" => 3,
        "med" => 2,
        _ => 1
    };

    static (string Risk, string Why) ScoreRisk(string path, string status)
    {
        var name = Path.GetFileName(path);
        if (name.Equals(".env", StringComparison.OrdinalIgnoreCase)
            || name.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || name.Contains("credential", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".pem", StringComparison.OrdinalIgnoreCase))
            return ("secret", "Possible secret — exclude from commit");

        if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
            || name.Equals("CdpEnums.cs", StringComparison.OrdinalIgnoreCase)
            || path.Contains("Program.cs", StringComparison.OrdinalIgnoreCase))
            return ("high", "Surface / project contract");

        if (status.Contains('?', StringComparison.Ordinal)
            || status.Contains('A', StringComparison.Ordinal)
            || status.Contains('D', StringComparison.Ordinal)
            || status.Contains('R', StringComparison.Ordinal))
            return ("med", "Add/delete/rename — check intent");

        if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
            return ("med", "Source change — judgment lane");

        return ("low", "Support / config");
    }

    static string? RunGit(string cwd, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        if (p is null) return null;
        var stdout = p.StandardOutput.ReadToEnd();
        _ = p.StandardError.ReadToEnd();
        if (!p.WaitForExit(8000))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
            return null;
        }

        return p.ExitCode == 0 ? stdout : stdout;
    }

}
