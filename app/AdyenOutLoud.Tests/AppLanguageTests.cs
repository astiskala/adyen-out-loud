using AdyenOutLoud.Models;

namespace AdyenOutLoud.Tests;

public sealed class AppLanguageTests
{
    [Theory]
    [InlineData("en", "English")]
    [InlineData("ZH", "中文")]
    [InlineData("ms", "Bahasa Melayu")]
    [InlineData("ta", "தமிழ்")]
    public void FromCodeFindsEachSupportedLanguageIgnoringCase(string code, string displayName) =>
        Assert.Equal(displayName, AppLanguage.FromCode(code).DisplayName);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("fr")]
    public void FromCodeDefaultsToEnglish(string? code) => Assert.Same(AppLanguage.English, AppLanguage.FromCode(code));

    [Fact]
    public void AllListsTheFourSupportedLanguages() =>
        Assert.Equal(["en", "zh", "ms", "ta"], AppLanguage.All.Select(language => language.Code));
}
