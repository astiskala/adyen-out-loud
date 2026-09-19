using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Models;
using Microsoft.Maui.Media;

namespace AdyenOutLoud.Services;

/// <summary>
/// MAUI implementation of text-to-speech using the platform's native TTS engine.
/// </summary>
public sealed class MauiSpeechService : ITextToSpeechService, IDisposable
{
    private readonly SemaphoreSlim _speechGate = new(1, 1);

    /// <inheritdoc />
    public async Task<SpeechDiagnostic> SpeakAsync(string text, AppLanguage language, CancellationToken cancellationToken)
    {
        await _speechGate.WaitAsync(cancellationToken);
        try
        {
            var platformLocales = (await TextToSpeech.Default.GetLocalesAsync()).ToArray();
            var voices = platformLocales
                .Select(locale => new VoiceLocale(locale.Id, locale.Language, locale.Name))
                .ToArray();

            var selected = SelectBestMatch(language.Locale, voices);
            var diagnostic = Describe(language.Locale, selected);
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

    /// <inheritdoc />
    public void Dispose() => _speechGate.Dispose();

    private static VoiceLocale? SelectBestMatch(string requestedLocale, VoiceLocale[] availableLocales)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedLocale);
        ArgumentNullException.ThrowIfNull(availableLocales);
        if (availableLocales.Length == 0) return null;

        var normalizedRequested = Normalize(requestedLocale);
        var exact = availableLocales.FirstOrDefault(voice => Normalize(voice.Id) == normalizedRequested);
        if (exact is not null) return exact;

        var requestedLanguage = BaseLanguage(normalizedRequested);
        var sameLanguage = availableLocales.FirstOrDefault(voice =>
            BaseLanguage(Normalize(voice.Id)) == requestedLanguage ||
            voice.Language.Equals(requestedLanguage, StringComparison.OrdinalIgnoreCase));

        return sameLanguage ?? availableLocales[0];
    }

    private static string Describe(string requestedLocale, VoiceLocale? selected)
    {
        if (selected is null) return $"No installed voices were reported; using the system default for {requestedLocale}.";
        return Normalize(selected.Id) == Normalize(requestedLocale)
            ? $"Using installed voice {selected.Id}."
            : $"Voice {requestedLocale} is unavailable; using {selected.Name} ({selected.Id}).";
    }

    private static string Normalize(string locale) => locale.Replace('_', '-').ToLowerInvariant();

    private static string BaseLanguage(string normalizedLocale)
    {
        var separator = normalizedLocale.IndexOf('-');
        return separator < 0 ? normalizedLocale : normalizedLocale[..separator];
    }
}
