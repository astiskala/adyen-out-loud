using AdyenOutLoud.Models;

namespace AdyenOutLoud.UITests;

public sealed class ConfigurationUiTests(UiEnvironment environment) : AppTest(environment)
{
    [Fact]
    public async Task FirstLaunchAsksForTheTerminalSerialNumber()
    {
        Assert.Equal("NEEDS ATTENTION", await App.LabelAsync(StatusTitle));
        Assert.Contains("terminal serial number", await App.LabelAsync(StatusDetail), StringComparison.Ordinal);
        Assert.Equal(NoEventYet, await App.LabelAsync(LatestEvent));
        Assert.Equal("English", await App.ValueAsync(LanguagePicker));
        // An empty field reports its placeholder as its value.
        Assert.Equal("324688170", await App.ValueAsync(SerialField));
    }

    [Fact]
    public async Task PairingWithoutASerialNumberExplainsWhatIsMissing()
    {
        await App.TapAsync(PairButton);

        await App.WaitForLabelAsync(ConfigurationStatus, text => text.Contains("terminal serial number", StringComparison.OrdinalIgnoreCase), "a missing-serial message");
        Assert.Equal("NEEDS ATTENTION", await App.LabelAsync(StatusTitle));
    }

    [Fact]
    public async Task CodesThatMatchNoRecentPaymentAreRefused()
    {
        await EnterPairingAsync("555000111", "ZZZ1", "ZZZ2");

        await App.WaitForLabelAsync(ConfigurationStatus, text => text.Contains("don't match", StringComparison.Ordinal), "a codes-don't-match message");
        Assert.Equal("NEEDS ATTENTION", await App.LabelAsync(StatusTitle));
    }

    [Fact]
    public async Task APairingIsRestoredAfterRelaunch()
    {
        await PairAsync("555000111");
        await App.WaitForLabelAsync(StatusTitle, title => title == "LISTENING", "the paired app to connect", TimeSpan.FromSeconds(40));

        await RelaunchAppAsync();

        await App.WaitForLabelAsync(StatusTitle, title => title is "LISTENING" or "CONNECTING", "the relaunched app to start connecting");
        Assert.Equal("555000111", await App.ValueAsync(SerialField));
    }

    public static TheoryData<string> LanguageNames { get; } = [.. AppLanguage.All.Select(language => language.DisplayName)];

    [Theory]
    [MemberData(nameof(LanguageNames))]
    public async Task EachLanguageCanBeChosenAndIsRemembered(string displayName)
    {
        await ChooseLanguageAsync(displayName);
        Assert.Equal(displayName, await App.ValueAsync(LanguagePicker));

        await RelaunchAppAsync();
        await App.FindAsync(LanguagePicker);
        Assert.Equal(displayName, await App.ValueAsync(LanguagePicker));
    }

    [Theory]
    [InlineData("English")]
    [InlineData("Bahasa Melayu")]
    public async Task TestVoicePlaysTheRecordingForTheSelectedLanguage(string displayName)
    {
        await ChooseLanguageAsync(displayName);
        Assert.Equal(DiagnosticPlaceholder, await App.LabelAsync(Diagnostic));

        await App.TapAsync(TestVoiceButton);

        await App.WaitForLabelAsync(
            Diagnostic,
            text => text.Contains(displayName, StringComparison.Ordinal) && text.Contains("test announcement", StringComparison.Ordinal),
            $"a confirmation that the {displayName} test announcement played",
            TimeSpan.FromSeconds(30));
    }

    private async Task ChooseLanguageAsync(string displayName)
    {
        await App.TapAsync(LanguagePicker);
        var wheel = await App.FindByClassChainAsync("**/XCUIElementTypePickerWheel[1]");
        await App.SetValueAsync(wheel, displayName);
        await App.TapElementAsync(await App.FindByClassChainAsync("**/XCUIElementTypeToolbar/XCUIElementTypeButton[1]"));
        await AppiumSession.WaitUntilAsync(async () => await App.ValueAsync(LanguagePicker) == displayName, TimeSpan.FromSeconds(10), $"the picker to show {displayName}");
    }
}
