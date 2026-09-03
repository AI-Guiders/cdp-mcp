using System.Diagnostics;
using System.Text;

namespace Cdp.Deploy;

public sealed record CdpAidPublishResult(int ExitCode, string Stdout, string Stderr, string? OkLine);

public static class CdpAidPublishRunner
{
    public static CdpAidPublishResult Publish(CdpAidPublishRequest request)
    {
        var (fileName, arguments) = BuildCommand(request);
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi)
                         ?? throw new InvalidOperationException("Failed to start aid-publish.");

        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return new CdpAidPublishResult(proc.ExitCode, stdout, stderr, ExtractOkLine(stdout));
    }

    internal static (string FileName, string Arguments) BuildCommand(CdpAidPublishRequest request)
    {
        var (fileName, arguments) = BuildCommandCore(request);
        return (fileName, arguments);
    }

    static (string FileName, string Arguments) BuildCommandCore(CdpAidPublishRequest request)
    {
        var sb = new StringBuilder();
        sb.Append("-Project ");
        sb.Append(Quote(request.ProjectPath));
        sb.Append(" -Target ");
        sb.Append(Quote(request.DeployRoot));
        sb.Append(" -Runtime win-x64 -Configuration Release -SelfContained");
        if (request.KillRunning)
            sb.Append(" -KillRunning");
        if (request.UseNuGet)
            sb.Append(" -UseNuGet");
        if (!string.IsNullOrWhiteSpace(request.PreserveConfigToml))
        {
            sb.Append(" -PreserveConfig ");
            sb.Append(Quote(request.PreserveConfigToml!));
        }

        var args = sb.ToString();
        var aidPublish = FindAidPublishExecutable(request.WorkingDirectory);
        return aidPublish is not null
            ? (aidPublish, args)
            : ("dotnet", $"tool run aid-publish -- {args}");
    }

    static string? FindAidPublishExecutable(string workingDirectory)
    {
        foreach (var candidate in CandidateAidPublishPaths())
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    static IEnumerable<string> CandidateAidPublishPaths()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            yield return Path.Combine(dir.Trim(), "aid-publish.exe");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, ".dotnet", "tools", "aid-publish.exe");
    }

    static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    static string? ExtractOkLine(string stdout)
    {
        foreach (var line in stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Reverse())
        {
            var t = line.Trim();
            if (t.StartsWith("OK:", StringComparison.OrdinalIgnoreCase))
                return t.Length <= 160 ? t : t[..160];
        }

        return null;
    }
}

public sealed record CdpAidPublishRequest(
    string ProjectPath,
    string DeployRoot,
    bool KillRunning,
    bool UseNuGet,
    string? PreserveConfigToml,
    string WorkingDirectory);
