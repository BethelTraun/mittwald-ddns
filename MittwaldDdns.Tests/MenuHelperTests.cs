using MittwaldDdns.Cli.Interactive;

namespace MittwaldDdns.Tests;

public sealed class MenuHelperTests
{
    [Fact]
    public void MenuOption_KeepsValueIndependentFromDisplayLabel()
    {
        var option = new MenuOption<MainMenuAction>(MainMenuAction.Save, "Persist configuration");

        Assert.Equal(MainMenuAction.Save, option.Value);
        Assert.Equal("Persist configuration", option.ToString());
    }

    [Theory]
    [InlineData("30s", 30)]
    [InlineData("5m", 300)]
    [InlineData("1h", 3600)]
    public void ShortIntervals_ParseAsPositiveTimespans(string input, int seconds)
    {
        Assert.True(PromptValidators.TryParseShortTimeSpan(input, out var value));
        Assert.Equal(TimeSpan.FromSeconds(seconds), value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0m")]
    [InlineData("-1h")]
    [InlineData("1d")]
    public void InvalidShortIntervals_AreRejected(string input)
    {
        Assert.False(PromptValidators.TryParseShortTimeSpan(input, out _));
    }

    [Fact]
    public void ApiVersionLabels_DoNotDefineValues()
    {
        Assert.Equal("API v1", PromptLabels.ApiVersionLabel(ApiVersionChoice.V1));
        Assert.Equal("API v2", PromptLabels.ApiVersionLabel(ApiVersionChoice.V2));
        Assert.Equal(ApiVersionChoice.V1, Enum.Parse<ApiVersionChoice>("V1"));
    }
}
