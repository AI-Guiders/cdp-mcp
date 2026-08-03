#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent test_scene|cdp_test_scene — IdeSessionLifecycle.TestScene without Cursor MCP.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteTestScene(string raw)
    {
        var work = NormalizeTestSceneCompound(raw);
        var path = ExtractKeyedValue(work, "path")
            ?? ExtractKeyedValue(work, "file_path")
            ?? ExtractKeyedValue(work, "file")
            ?? ExtractKeyedValue(work, "solution_path");
        var configuration = ExtractKeyedValue(work, "configuration");
        var maxTests = ExtractKeyedValue(work, "max_tests");

        return new Route(
            Verb.TestScene,
            raw,
            Ok: true,
            Op: "scene",
            Path: path,
            Tool: configuration,
            NewString: maxTests,
            Go: "test_scene");
    }

    static string NormalizeTestSceneCompound(string raw)
    {
        foreach (var prefix in TestScenePrefixes)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "test_scene";
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;
            return "test_scene " + raw[prefix.Length..].TrimStart();
        }

        return raw;
    }

    static readonly string[] TestScenePrefixes =
    [
        "test_scene_desk",
        "test_scene",
        "cdp_test_scene",
        "test_runner"
    ];
}
