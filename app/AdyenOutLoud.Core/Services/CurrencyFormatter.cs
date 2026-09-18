using System.Globalization;
using AdyenOutLoud.Models;

namespace AdyenOutLoud.Services;

public static class CurrencyFormatter
{
    private static readonly HashSet<string> ZeroDecimalCurrencies =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CVE", "DJF", "GNF", "IDR", "JPY", "KMF", "KRW", "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF"
        };

    private static readonly HashSet<string> ThreeDecimalCurrencies =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "BHD", "IQD", "JOD", "KWD", "LYD", "OMR", "TND"
        };

    public static int GetExponent(string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        if (ZeroDecimalCurrencies.Contains(currency))
        {
            return 0;
        }

        return ThreeDecimalCurrencies.Contains(currency) ? 3 : 2;
    }

    public static string Format(long minorUnits, string currency, AppLanguage language)
    {
        var normalizedCurrency = currency.ToUpperInvariant();
        var exponent = GetExponent(normalizedCurrency);
        var amount = minorUnits / (decimal)Math.Pow(10, exponent);
        var culture = CultureInfo.GetCultureInfo(language.Locale);
        return $"{amount.ToString($"N{exponent}", culture)} {normalizedCurrency}";
    }
}
