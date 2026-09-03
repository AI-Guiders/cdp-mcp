using System.Diagnostics;
using System.Text.Json;

namespace Cdp.Deploy;

public static class CdpDeployPromoter
{
    static readonly string[] PreserveNames = ["cdp-mcp.toml"];

    public static void PromoteTree(string stagedRoot, string liveRoot)
    {
        if (!Directory.Exists(stagedRoot))
            throw new FileNotFoundException("Staged deploy tree missing.", stagedRoot);

        Directory.CreateDirectory(liveRoot);
        var backups = BackupPreserveFiles(liveRoot);

        var robocopy = FindRobocopy();
        if (robocopy is not null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = robocopy,
                Arguments = $"\"{stagedRoot}\" \"{liveRoot}\" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("robocopy failed to start.");
            proc.WaitForExit();
            if (proc.ExitCode >= 8)
                throw new InvalidOperationException($"robocopy promote failed exit={proc.ExitCode} ({stagedRoot} -> {liveRoot})");
        }
        else
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(liveRoot))
            {
                var attr = File.GetAttributes(entry);
                if (attr.HasFlag(FileAttributes.Directory))
                    Directory.Delete(entry, true);
                else
                    File.Delete(entry);
            }

            foreach (var entry in Directory.EnumerateFileSystemEntries(stagedRoot))
            {
                var name = Path.GetFileName(entry);
                var dest = Path.Combine(liveRoot, name);
                if (Directory.Exists(entry))
                    CopyDirectory(entry, dest);
                else
                    File.Copy(entry, dest, true);
            }
        }

        RestorePreserveFiles(liveRoot, backups);
        CdpDeploySeatConfig.NormalizeInstallSeat(liveRoot);
    }

    static Dictionary<string, string> BackupPreserveFiles(string liveRoot)
    {
        var backups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in PreserveNames)
        {
            var path = Path.Combine(liveRoot, name);
            if (File.Exists(path))
                backups[name] = File.ReadAllText(path);
        }

        return backups;
    }

    static void RestorePreserveFiles(string liveRoot, Dictionary<string, string> backups)
    {
        foreach (var (name, content) in backups)
            File.WriteAllText(Path.Combine(liveRoot, name), content);
    }

    static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(source, dest, StringComparison.OrdinalIgnoreCase));

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = file.Replace(source, dest, StringComparison.OrdinalIgnoreCase);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    static string? FindRobocopy()
    {
        var system = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "robocopy.exe");
        return File.Exists(system) ? system : null;
    }
}

internal static class CdpDeployPending
{
    internal sealed record PendingUpdate(
        string Schema,
        string Mode,
        string StagedAtUtc,
        string ServiceRoot,
        string BridgeRoot,
        string? Version,
        string ApplyHint);

    public static void WriteSoft(CdpDeployLayout layout, string serviceStaged, string bridgeStaged, string? version)
    {
        Directory.CreateDirectory(layout.ServiceInstall);
        var pending = new PendingUpdate(
            "cdp_pending_update/v0",
            "soft",
            DateTime.UtcNow.ToString("o"),
            serviceStaged,
            bridgeStaged,
            version,
            "cdp_deploy mode=apply (promote .next, no republish)");
        File.WriteAllText(layout.PendingMarker, JsonSerializer.Serialize(pending, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static PendingUpdate ReadRequired(CdpDeployLayout layout)
    {
        if (!File.Exists(layout.PendingMarker))
            throw new InvalidOperationException($"No pending update at {layout.PendingMarker} — run soft first.");

        return JsonSerializer.Deserialize<PendingUpdate>(File.ReadAllText(layout.PendingMarker), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Invalid pending update JSON.");
    }

    public static void Clear(CdpDeployLayout layout)
    {
        if (File.Exists(layout.PendingMarker))
            File.Delete(layout.PendingMarker);
    }
}
