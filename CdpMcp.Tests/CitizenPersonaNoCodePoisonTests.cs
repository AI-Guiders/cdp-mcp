#nullable enable
using Xunit;

namespace CdpMcp.Tests;

/// <summary>Persona examples must stay placeholders — never real habitat type/path names.</summary>
public sealed class CitizenPersonaNoCodePoisonTests
{
    static readonly string[] Poisons =
    [
        "CitizenRouteHost",
        "CitizenIntentRouter",
        "IdeFindChannel",
        "GlassIntercom",
        "CascadeIDE",
        "CdpMcp.csproj",
        "CdpMcp.Tests",
    ];

    [Fact]
    public void Dialog_and_Wire_prompts_have_no_real_code_path_poisons()
    {
        var dialog = CitizenPersona.DialogSystemPrompt;
        var wire = CitizenPersona.WireSystemPrompt;

        foreach (var poison in Poisons)
        {
            Assert.DoesNotContain(poison, dialog, StringComparison.Ordinal);
            Assert.DoesNotContain(poison, wire, StringComparison.Ordinal);
        }

        Assert.Contains("rel/file.cs", dialog, StringComparison.Ordinal);
        Assert.Contains("placeholders", dialog, StringComparison.OrdinalIgnoreCase);
    }
}
