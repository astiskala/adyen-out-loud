using AdyenOutLoud.Models;

namespace AdyenOutLoud.Abstractions;

/// <summary>
/// Service for text-to-speech operations.
/// </summary>
public interface ITextToSpeechService
{
    /// <summary>
    /// Speaks the given text using the specified language.
    /// </summary>
    /// <param name="text">The text to speak.</param>
    /// <param name="language">The language/locale to use for speech synthesis.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Diagnostic information about the speech operation.</returns>
    Task<SpeechDiagnostic> SpeakAsync(string text, AppLanguage language, CancellationToken cancellationToken);
}
