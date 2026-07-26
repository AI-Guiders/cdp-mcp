using System.Diagnostics;
using System.Text;

namespace CdpMcp;

/// <summary>
/// PlantUML → PNG via <c>java -jar plantuml.jar -tpng -pipe</c>.
/// Jar: env <c>PLANTUML_JAR</c>, then CDP plugin quarantine, then well-known paths.
/// </summary>
internal static class PlantUmlRender
{
    public const int TimeoutMs = 45_000;

    public sealed record Result(bool Ok, byte[]? Png, string? Error, string? Jar);

    public static bool LooksLikePlantUml(string body, string? path, string? fence)
    {
        if (fence is "plantuml" or "puml")
            return true;
        var ext = path is null ? "" : Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".puml" or ".plantuml")
            return true;
        var t = body.AsSpan().TrimStart();
        return t.StartsWith("@startuml", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("@startmindmap", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("@startsalt", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("@startgantt", StringComparison.OrdinalIgnoreCase);
    }

    public static Result RenderPng(string source, CancellationToken cancellationToken)
    {
        var jar = ResolveJar();
        if (jar is null)
            return new Result(false, null, "plantuml_jar_not_found (set PLANTUML_JAR)", null);

        var java = ResolveJava();
        if (java is null)
            return new Result(false, null, "java_not_found", jar);

        var body = EnsureStartUml(source);
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = java,
                Arguments = $"-jar \"{jar}\" -tpng -pipe",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardInputEncoding = Encoding.UTF8,
            };

            using var proc = new Process { StartInfo = psi };
            if (!proc.Start())
                return new Result(false, null, "plantuml_start_failed", jar);

            // Write stdin on background; read stdout fully.
            var writeTask = Task.Run(async () =>
            {
                await proc.StandardInput.WriteAsync(body.AsMemory(), cancellationToken).ConfigureAwait(false);
                await proc.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
                proc.StandardInput.Close();
            }, cancellationToken);

            using var ms = new MemoryStream();
            var copyTask = proc.StandardOutput.BaseStream.CopyToAsync(ms, cancellationToken);
            var errTask = proc.StandardError.ReadToEndAsync(cancellationToken);

            if (!proc.WaitForExit(TimeoutMs))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return new Result(false, null, "plantuml_timeout", jar);
            }

            writeTask.GetAwaiter().GetResult();
            copyTask.GetAwaiter().GetResult();
            var err = errTask.GetAwaiter().GetResult();

            var png = ms.ToArray();
            if (proc.ExitCode != 0 || png.Length < 8 || png[0] != 0x89 || png[1] != (byte)'P')
            {
                var msg = string.IsNullOrWhiteSpace(err) ? $"exit={proc.ExitCode} bytes={png.Length}" : err.Trim();
                if (msg.Length > 400) msg = msg[..400] + "…";
                return new Result(false, null, msg, jar);
            }

            if (png.Length > ToolMediaOutbox.MaxBytesPerImage)
                return new Result(false, null, $"png_too_large ({png.Length})", jar);

            return new Result(true, png, null, jar);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new Result(false, null, ex.Message, jar);
        }
    }

    static string EnsureStartUml(string source)
    {
        var t = source.Trim();
        if (t.Contains("@start", StringComparison.OrdinalIgnoreCase))
            return t.EndsWith('\n') ? t : t + "\n";
        return "@startuml\n" + t + "\n@enduml\n";
    }

    static string? ResolveJava()
    {
        var env = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (env is { Length: > 0 })
        {
            var cand = Path.Combine(env, "bin", OperatingSystem.IsWindows() ? "java.exe" : "java");
            if (File.Exists(cand)) return cand;
        }

        foreach (var name in new[] { "java.exe", "java" })
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = OperatingSystem.IsWindows() ? "where.exe" : "which",
                    Arguments = OperatingSystem.IsWindows() ? "java" : "java",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p is null) break;
                var line = p.StandardOutput.ReadLine();
                p.WaitForExit(3000);
                if (line is { Length: > 0 } && File.Exists(line.Trim()))
                    return line.Trim();
            }
            catch
            {
                /* fall through */
            }

            _ = name;
        }

        // Dogfood machines
        foreach (var cand in new[]
                 {
                     @"C:\Program Files\Eclipse Adoptium\jdk-21.0.11.10-hotspot\bin\java.exe",
                     @"C:\Program Files\Java\jdk-21\bin\java.exe"
                 })
        {
            if (File.Exists(cand)) return cand;
        }

        return null;
    }

    static string? ResolveJar()
    {
        var env = Environment.GetEnvironmentVariable("PLANTUML_JAR");
        if (env is { Length: > 0 } && File.Exists(env))
            return env;

        var quarantined = CdpPluginQuarantine.ResolvePlantUmlJar();
        if (quarantined is { Length: > 0 })
            return quarantined;

        foreach (var cand in new[]
                 {
                     @"D:\plantuml-mit-1.2025.0.jar",
                     @"D:\PlantUML\plantuml-1.2024.5.jar",
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                         @".cursor\extensions\jebbs.plantuml-2.18.1\plantuml.jar")
                 })
        {
            if (File.Exists(cand))
                return cand;
        }

        return null;
    }
}
