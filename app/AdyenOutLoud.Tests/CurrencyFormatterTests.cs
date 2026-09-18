using AdyenOutLoud.Models;
using AdyenOutLoud.Services;

namespace AdyenOutLoud.Tests;

public sealed class CurrencyFormatterTests
{
    [Theory]
    [InlineData("EUR", 2)]
    [InlineData("JPY", 0)]
    [InlineData("IDR", 0)]
    [InlineData("CLP", 2)]
    [InlineData("ISK", 2)]
    [InlineData("KWD", 3)]
    public void UsesAdyenCurrencyExponent(string currency, int expected) =>
        Assert.Equal(expected, CurrencyFormatter.GetExponent(currency));

    [Fact]
    public void FormatsMinorUnitsWithLanguageCulture() =>
        Assert.Equal("12.50 EUR", CurrencyFormatter.Format(1250, "eur", AppLanguage.English));
}
