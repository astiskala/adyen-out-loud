using AdyenOutLoud.Services;

namespace AdyenOutLoud.Tests;

public sealed class PaymentMethodNamesTests
{
    [Theory]
    [InlineData("visa", "Visa")]
    [InlineData("VISA", "Visa")]
    [InlineData("mc", "Mastercard")]
    [InlineData("applepay", "Apple Pay")]
    [InlineData("cup", "UnionPay")]
    public void KnownAdyenPaymentMethodsMapToCanonicalDisplayNames(string method, string expected) =>
        Assert.Equal(expected, PaymentMethodNames.Get(method));

    [Fact]
    public void UnknownPaymentMethodsAreHumanizedFromTheirRawCode() =>
        Assert.Equal("some new method", PaymentMethodNames.Get("some_new-method"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingPaymentMethodFallsBackToCard(string? method) =>
        Assert.Equal("card", PaymentMethodNames.Get(method));
}
