using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Models;
using Microsoft.Maui.Media;

namespace AdyenOutLoud.Services;

public sealed class MauiSpeechService : ITextToSpeechService, IDisposable
{
    private readonly SemaphoreSlim _speechGate = new(1, 1);

    public async Task<SpeechDiagnostic> SpeakAsync(string text, AppLanguage language, CancellationToken cancellationToken)
    {
        await _speechGate.WaitAsync(cancellationToken);
        try
        {
            var platformLocales = (await TextToSpeech.Default.GetLocalesAsync()).ToArray();
            var voices = platformLocales
                .Select(locale => new VoiceLocale(locale.Id, locale.Language, locale.Name))
                .ToArray();

            var selected = SpeechLocaleSelector.SelectBestMatch(language.Locale, voices);
            var diagnostic = SpeechLocaleSelector.Describe(language.Locale, selected);
            var platformLocale = selected is null ? null : platformLocales.FirstOrDefault(locale => locale.Id == selected.Id);

            var options = new SpeechOptions { Locale = platformLocale, Pitch = 1.0f, Volume = 1.0f, Rate = 1.0f };
            await TextToSpeech.Default.SpeakAsync(text, options, cancellationToken);
            return new(language.Locale, selected?.Id, diagnostic);
        }
        finally
        {
            _speechGate.Release();
        }
    }

    public void Dispose() => _speechGate.Dispose();
}
