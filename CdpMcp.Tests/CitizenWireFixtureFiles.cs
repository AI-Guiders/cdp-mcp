#nullable enable

namespace CdpMcp.Tests;

/// <summary>Load docs/design/citizen-wire-fixtures/* regardless of test cwd / BaseDirectory.</summary>
internal static class CitizenWireFixtureFiles
{
    public static string Read(string name)
    {
        var fromCwd = Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            "docs", "design", "citizen-wire-fixtures", name));
        if (File.Exists(fromCwd))
            return File.ReadAllText(fromCwd);

        var fromProj = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "docs", "design", "citizen-wire-fixtures", name));
        if (File.Exists(fromProj))
            return File.ReadAllText(fromProj);

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "design", "citizen-wire-fixtures", name);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException("citizen wire fixture: " + name);
    }
}
