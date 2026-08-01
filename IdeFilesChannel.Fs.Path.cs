#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Cwd / where resolution for IdeFilesChannel (project|external|sticky).</summary>
internal static partial class IdeFilesChannel
{
    static string ResolveCwd(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        out string where)
    {
        var whereRaw = (Opt(args, "where") ?? "").Trim().ToLowerInvariant();
        var pathArg = Opt(args, "path") ?? Opt(args, "root");

        if (whereRaw is "external" || (pathArg is { Length: > 0 } && Path.IsPathRooted(pathArg)))
        {
            where = "external";
            if (pathArg is { Length: > 0 })
            {
                var full = Path.GetFullPath(pathArg);
                if (Directory.Exists(full))
                {
                    SetCwd(full);
                    return full;
                }

                if (File.Exists(full))
                {
                    var parent = Path.GetDirectoryName(full)!;
                    SetCwd(parent);
                    return parent;
                }
            }

            return GetCwd(session);
        }

        if (whereRaw is "project" || whereRaw is "session")
        {
            where = "project";
            if (session.ProjectRoot is { Length: > 0 } pr && Directory.Exists(pr))
            {
                var full = Path.GetFullPath(pr);
                SetCwd(full);
                return full;
            }

            where = "cwd";
            return GetCwd(session);
        }

        // default: sticky cwd, else project, else process
        var sticky = IdeSettingsStore.GetOrNull(CwdKey);
        if (sticky is { Length: > 0 } && Directory.Exists(sticky))
        {
            where = ClassifyWhere(session, sticky);
            return Path.GetFullPath(sticky);
        }

        if (session.ProjectRoot is { Length: > 0 } proj && Directory.Exists(proj))
        {
            where = "project";
            var full = Path.GetFullPath(proj);
            SetCwd(full);
            return full;
        }

        where = "cwd";
        return GetCwd(session);
    }

    static string GetCwd(SessionContext session)
    {
        var sticky = IdeSettingsStore.GetOrNull(CwdKey);
        if (sticky is { Length: > 0 } && Directory.Exists(sticky))
            return Path.GetFullPath(sticky);
        if (session.ProjectRoot is { Length: > 0 } pr && Directory.Exists(pr))
            return Path.GetFullPath(pr);
        return Path.GetFullPath(Environment.CurrentDirectory);
    }

    static void SetCwd(string full) => IdeSettingsStore.Set(CwdKey, Path.GetFullPath(full));

    static string ClassifyWhere(SessionContext session, string path)
    {
        if (session.ProjectRoot is { Length: > 0 } pr)
        {
            try
            {
                var root = Path.GetFullPath(pr);
                var full = Path.GetFullPath(path);
                if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return "project";
            }
            catch
            {
                // fall through
            }
        }

        return "external";
    }
}
