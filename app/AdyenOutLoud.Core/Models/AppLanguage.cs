namespace AdyenOutLoud.Models;

/// <summary>
/// Represents a supported application language with its locale information.
/// </summary>
/// <param name="Code">The ISO 639-1 language code (e.g., "en", "zh").</param>
/// <param name="DisplayName">The human-readable display name in the language itself.</param>
public sealed record AppLanguage(string Code, string DisplayName)
{
    /// <summary>
    /// English (Singapore).
    /// </summary>
    public static readonly AppLanguage English = new("en", "English");

    /// <summary>
    /// Chinese (Singapore).
    /// </summary>
    public static readonly AppLanguage Chinese = new("zh", "中文");

    /// <summary>
    /// Malay (Singapore).
    /// </summary>
    public static readonly AppLanguage Malay = new("ms", "Bahasa Melayu");

    /// <summary>
    /// Tamil (Singapore).
    /// </summary>
    public static readonly AppLanguage Tamil = new("ta", "தமிழ்");

    /// <summary>
    /// Gets all supported languages.
    /// </summary>
    public static IReadOnlyList<AppLanguage> All { get; } =
        [English, Chinese, Malay, Tamil];

    /// <summary>
    /// Gets the language matching the given code, defaulting to English.
    /// </summary>
    /// <param name="code">The ISO 639-1 language code (case-insensitive).</param>
    /// <returns>The matching <see cref="AppLanguage"/>, or <see cref="English"/> if not found.</returns>
    public static AppLanguage FromCode(string? code) =>
        All.FirstOrDefault(language => language.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) ?? English;
}
