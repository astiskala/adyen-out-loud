namespace AdyenOutLoud.TestSupport;

/// <summary>
/// Adyen terminal Display webhook bodies, derived from the same fixtures the Worker's own tests use
/// so both suites describe the same real-world payloads.
/// </summary>
internal static class Webhooks
{
    private const string FixturePspReference = "NC6HT9CRT65ZGN82";
    private const string FixturePoiId = "V400m-324688170";

    public static string Approved(string pspReference, string terminalSerial = "324688170") =>
        File.ReadAllText(RepositoryPaths.Fixture("display-tender-final-approved.json"))
            .Replace(FixturePspReference, pspReference, StringComparison.Ordinal)
            .Replace(FixturePoiId, $"V400m-{terminalSerial}", StringComparison.Ordinal);

    public static string Declined(string terminalSerial = "324688170") =>
        File.ReadAllText(RepositoryPaths.Fixture("display-tender-final-declined.json"))
            .Replace(FixturePoiId, $"V400m-{terminalSerial}", StringComparison.Ordinal);

    public static string NextPsp() => $"E2E{Guid.NewGuid():N}"[..16].ToUpperInvariant();
}
