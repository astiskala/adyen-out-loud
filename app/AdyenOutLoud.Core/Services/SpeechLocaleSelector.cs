using AdyenOutLoud.Models;

namespace AdyenOutLoud.Services;

/// <summary>
/// Picks the best installed TTS voice for a requested BCP-47 locale and explains the choice.
/// Kept independent of any native TTS API so the preferred/fallback ordering can be unit tested
/// (see docs/testing.md) without a device or simulator.
/// </summary>
public static class SpeechLocaleSelector
{
    /// <summary>
    /// Preference order: exact locale match, then same base language (e.g. "zh-CN" for a requested "zh-SG"),
    /// then the platform's first reported voice. Returns null only when no voices were reported at all.
    /// </summary>
    public static VoiceLocale? SelectBestMatch(string requestedLocale, IReadOnlyList<VoiceLocale> availableLocales)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedLocale);
        ArgumentNullException.ThrowIfNull(availableLocales);
        if (availableLocales.Count == 0) return null;

        var normalizedRequested = Normalize(requestedLocale);
        var exact = availableLocales.FirstOrDefault(voice => Normalize(voice.Id) == normalizedRequested);
        if (exact is not null) return exact;

        var requestedLanguage = BaseLanguage(normalizedRequested);
        var sameLanguage = availableLocales.FirstOrDefault(voice =>
            BaseLanguage(Normalize(voice.Id)) == requestedLanguage ||
            voice.Language.Equals(requestedLanguage, StringComparison.OrdinalIgnoreCase));

        return sameLanguage ?? availableLocales[0];
    }

    /// <summary>Human-readable explanation of what <see cref="SelectBestMatch"/> chose, for the in-app diagnostic panel.</summary>
    public static string Describe(string requestedLocale, VoiceLocale? selected)
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
