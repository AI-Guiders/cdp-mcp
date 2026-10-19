#nullable enable
using Xunit;

namespace CdpMcp.Tests;

/// <summary>
/// CDP-ADR-0031: умный выход (feature=no_adr why=…) обязан предъявить источник.
/// Намерение без предъявленного пути или ADR-id — театр, а не разбор.
/// </summary>
public sealed class ExploreCorrNoAdrTests
{
    [Fact]
    public void Why_with_adr_id_counts_as_source()
    {
        var root = Path.Combine(Path.GetTempPath(), "no-adr-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);

        Assert.True(ExploreCorrNoAdr.NamesSource(root, "CDP-ADR-0031 не покрывает этот локус"));
    }

    [Fact]
    public void Why_with_existing_path_counts_as_source()
    {
        var root = Path.Combine(Path.GetTempPath(), "no-adr-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "README.md"), "x");

        Assert.True(ExploreCorrNoAdr.NamesSource(root, "смотрел README.md, ничего про время"));
    }

    [Fact]
    public void Why_with_intention_only_is_refused()
    {
        var root = Path.Combine(Path.GetTempPath(), "no-adr-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);

        Assert.False(ExploreCorrNoAdr.NamesSource(root, "к вопросу не относится, я подумал"));
    }
}
