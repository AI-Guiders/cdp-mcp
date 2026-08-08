#nullable enable
using Xunit;

namespace CdpMcp.Tests;

/// <summary>Persona examples must stay placeholders — never real habitat type/path names or KB content magnets.</summary>
public sealed class CitizenPersonaNoCodePoisonTests
{
    static readonly string[] CodePoisons =
    [
        "CitizenRouteHost",
        "CitizenIntentRouter",
        "IdeFindChannel",
        "GlassIntercom",
        "CascadeIDE",
        "CdpMcp.csproj",
        "CdpMcp.Tests",
    ];

    /// <summary>Lived: Wire laundry made Sierra paste-read Integrity Core / SHOWCASE instead of dig-named card.</summary>
    static readonly string[] KbContentPoisons =
    [
        "META/integrity-core",
        "integrity-core.md",
        "SHOWCASE.md",
        "definition_id=debug-radius",
        "pack_id=epistemic-scene",
        "pack_id=agent-operations-cdp",
        "process_id=bug-radius-shrink",
    ];

    [Fact]
    public void Dialog_and_Wire_prompts_have_no_real_code_path_poisons()
    {
        var dialog = CitizenPersona.DialogSystemPrompt;
        var wire = CitizenPersona.WireSystemPrompt;

        foreach (var poison in CodePoisons)
        {
            Assert.DoesNotContain(poison, dialog, StringComparison.Ordinal);
            Assert.DoesNotContain(poison, wire, StringComparison.Ordinal);
        }

        Assert.Contains("rel/file.cs", dialog, StringComparison.Ordinal);
        Assert.Contains("placeholders", dialog, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dialog_and_Wire_prompts_have_no_kb_content_magnets()
    {
        var dialog = CitizenPersona.DialogSystemPrompt;
        var wire = CitizenPersona.WireSystemPrompt;

        foreach (var poison in KbContentPoisons)
        {
            Assert.DoesNotContain(poison, dialog, StringComparison.Ordinal);
            Assert.DoesNotContain(poison, wire, StringComparison.Ordinal);
        }

        Assert.Contains("file_path=rel/note.md", wire, StringComparison.Ordinal);
        Assert.Contains("placeholders ≠ leaf", wire, StringComparison.Ordinal);
    }
}
