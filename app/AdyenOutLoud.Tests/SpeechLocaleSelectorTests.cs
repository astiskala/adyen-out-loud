using AdyenOutLoud.Models;
using AdyenOutLoud.Services;

namespace AdyenOutLoud.Tests;

public sealed class SpeechLocaleSelectorTests
{
    private static readonly VoiceLocale EnSg = new("en-SG", "en", "English (Singapore)");
    private static readonly VoiceLocale EnUs = new("en-US", "en", "English (United States)");
    private static readonly VoiceLocale ZhCn = new("zh-CN", "zh", "Chinese (China)");
    private static readonly VoiceLocale ZhSg = new("zh-SG", "zh", "Chinese (Singapore)");
    private static readonly VoiceLocale MsMy = new("ms-MY", "ms", "Malay (Malaysia)");
    private static readonly VoiceLocale TaIn = new("ta-IN", "ta", "Tamil (India)");
    private static readonly VoiceLocale FrFr = new("fr-FR", "fr", "French (France)");

    [Theory]
    [InlineData("en-SG")]
    [InlineData("zh-SG")]
    [InlineData("ms-SG")]
    [InlineData("ta-SG")]
    public void PrefersAnExactLocaleMatchWhenInstalled(string requested)
    {
        var installed = new[] { EnSg, ZhSg, new VoiceLocale("ms-SG", "ms", "Malay (Singapore)"), new VoiceLocale("ta-SG", "ta", "Tamil (Singapore)") };
        var selected = SpeechLocaleSelector.SelectBestMatch(requested, installed);
        Assert.Equal(requested, selected?.Id);
    }

    [Fact]
    public void FallsBackToTheSameBaseLanguageWhenTheRegionalVoiceIsMissing()
    {
        var installed = new[] { EnUs, ZhCn, MsMy, TaIn };

        Assert.Equal("en-US", SpeechLocaleSelector.SelectBestMatch("en-SG", installed)?.Id);
        Assert.Equal("zh-CN", SpeechLocaleSelector.SelectBestMatch("zh-SG", installed)?.Id);
        Assert.Equal("ms-MY", SpeechLocaleSelector.SelectBestMatch("ms-SG", installed)?.Id);
        Assert.Equal("ta-IN", SpeechLocaleSelector.SelectBestMatch("ta-SG", installed)?.Id);
    }

    [Fact]
    public void FallsBackToTheFirstReportedVoiceWhenNoLanguageMatchExists()
    {
        var installed = new[] { FrFr };
        Assert.Equal("fr-FR", SpeechLocaleSelector.SelectBestMatch("ta-SG", installed)?.Id);
    }

    [Fact]
    public void ReturnsNullWhenThePlatformReportsNoVoicesAtAll()
    {
        Assert.Null(SpeechLocaleSelector.SelectBestMatch("en-SG", []));
    }

    [Fact]
    public void MatchIsCaseAndSeparatorInsensitive()
    {
        var installed = new[] { new VoiceLocale("zh_SG", "zh", "Chinese (Singapore)") };
        Assert.Equal("zh_SG", SpeechLocaleSelector.SelectBestMatch("ZH-sg", installed)?.Id);
    }

    [Fact]
    public void DescribeReportsAnExactMatchDifferentlyFromAFallback()
    {
        Assert.Equal("Using installed voice zh-SG.", SpeechLocaleSelector.Describe("zh-SG", ZhSg));
        Assert.Equal("Voice zh-SG is unavailable; using Chinese (China) (zh-CN).", SpeechLocaleSelector.Describe("zh-SG", ZhCn));
        Assert.Equal("No installed voices were reported; using the system default for zh-SG.", SpeechLocaleSelector.Describe("zh-SG", null));
    }
}
