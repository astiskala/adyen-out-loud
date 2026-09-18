namespace AdyenOutLoud.Models;

public sealed record AppLanguage(string Code, string DisplayName, string Locale)
{
    public static readonly AppLanguage English = new("en", "English", "en-SG");
    public static readonly AppLanguage Chinese = new("zh", "中文", "zh-SG");
    public static readonly AppLanguage Malay = new("ms", "Bahasa Melayu", "ms-SG");
    public static readonly AppLanguage Tamil = new("ta", "தமிழ்", "ta-SG");

    public static IReadOnlyList<AppLanguage> All { get; } =
        [English, Chinese, Malay, Tamil];

    public static AppLanguage FromCode(string? code) =>
        All.FirstOrDefault(language => language.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) ?? English;
}
