namespace AdyenOutLoud.Services;

public static class PaymentMethodNames
{
    private static readonly IReadOnlyDictionary<string, string> CanonicalNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["amex"] = "American Express",
            ["applepay"] = "Apple Pay",
            ["bcmc"] = "Bancontact",
            ["cartebancaire"] = "Cartes Bancaires",
            ["cash"] = "cash",
            ["cup"] = "UnionPay",
            ["diners"] = "Diners Club",
            ["discover"] = "Discover",
            ["eftpos_australia"] = "eftpos",
            ["electron"] = "Visa Electron",
            ["girocard"] = "girocard",
            ["googlepay"] = "Google Pay",
            ["interac"] = "Interac",
            ["jcb"] = "JCB",
            ["maestro"] = "Maestro",
            ["mc"] = "Mastercard",
            ["mastercard"] = "Mastercard",
            ["mobilepay"] = "MobilePay",
            ["paypal"] = "PayPal",
            ["twint"] = "TWINT",
            ["visa"] = "Visa",
            ["visadebit"] = "Visa Debit",
            ["vpay"] = "V Pay"
        };

    public static string Get(string? method) =>
        string.IsNullOrWhiteSpace(method)
            ? "card"
            : CanonicalNames.GetValueOrDefault(method.Trim(), Humanize(method));

    private static string Humanize(string value) => value.Trim().Replace('_', ' ').Replace('-', ' ');
}
