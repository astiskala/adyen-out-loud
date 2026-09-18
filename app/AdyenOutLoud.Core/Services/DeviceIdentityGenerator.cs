using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace AdyenOutLoud.Services;

public static class DeviceIdentityGenerator
{
    public static string Create()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool IsValid([NotNullWhen(true)] string? token) => token is { Length: 43 } &&
        token.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
