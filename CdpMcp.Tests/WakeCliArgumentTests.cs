using Xunit;

namespace CdpMcp.Tests;

/// <summary>
/// cmd.exe режет команду на переводе строки: многострочное тело charge доходило только первой
/// строкой — course pointer, строка TM, amnesia stub и ownership postfix терялись, получатель
/// видел голую фразу и заново искал контекст. Аргументы CLI собираем однострочными,
/// содержимое сохраняем целиком.
/// </summary>
public class WakeCliArgumentTests
{
    [Fact]
    public void Run_arguments_keep_whole_body_on_one_line()
    {
        var body = "первая строка\nвторая строка\n\nтретья секция";

        var args = CideWakeChannels.Opencode.BuildRunArguments("ses_x", body);

        Assert.DoesNotContain('\n', args);
        Assert.DoesNotContain('\r', args);
        Assert.Contains("первая строка", args);
        Assert.Contains("вторая строка", args);
        Assert.Contains("третья секция", args);
        Assert.Contains("run -s ses_x", args);
    }

    [Fact]
    public void Collapsed_body_keeps_sections_separable()
    {
        Assert.Equal("a | b", CideWakeChannels.Opencode.CollapseForCliArg("a\nb"));
        Assert.Equal("a | b", CideWakeChannels.Opencode.CollapseForCliArg("a\r\nb"));
    }

    [Fact]
    public void Collapsed_body_is_capped_for_cmd_line_limit()
    {
        var long_ = new string('x', CideWakeChannels.Opencode.CliArgMaxChars + 500);

        var collapsed = CideWakeChannels.Opencode.CollapseForCliArg(long_);

        Assert.True(collapsed.Length <= CideWakeChannels.Opencode.CliArgMaxChars + 32, $"len={collapsed.Length}");
        Assert.EndsWith("…[truncated wake charge]", collapsed, StringComparison.Ordinal);
    }
}
