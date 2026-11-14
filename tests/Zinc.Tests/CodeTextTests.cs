using Zinc.Settings;

namespace Zinc.Tests;

public class CodeTextTests
{
    [Fact]
    public void SplitsCodeSpansOut()
    {
        Assert.Equal(
            [("A key bound to ", false), ("none", true), (" goes to the program instead.", false)],
            CodeText.Split("A key bound to `none` goes to the program instead."));
        Assert.Equal([("45s", true), (" or ", false), ("2m", true)], CodeText.Split("`45s` or `2m`"));
    }

    [Theory]
    [InlineData("No code here.")]
    [InlineData("Win+` stays as it is.")] // a backtick with no partner
    [InlineData("")]
    public void LeavesTextWithoutPairsAlone(string text) => Assert.Equal(text, CodeText.Plain(text));

    [Fact]
    public void PlainDropsOnlyTheBackticks()
    {
        Assert.Equal("Reads hosts from ~/.ssh/config.", CodeText.Plain("Reads hosts from `~/.ssh/config`."));
        Assert.Equal("an empty pair  stays out", CodeText.Plain("an empty pair `` stays out"));
    }

    [Fact]
    public void TheSchemaGivesEditorsBothForms()
    {
        var setting = SettingsCatalog.All.First(s => s.Description.Contains('`'));
        var schema = ConfigSchema.Build().ToJsonString();
        Assert.Contains(System.Text.Json.JsonSerializer.Serialize(CodeText.Plain(setting.Description)), schema);
        Assert.Contains("\"markdownDescription\"", schema);
    }
}
