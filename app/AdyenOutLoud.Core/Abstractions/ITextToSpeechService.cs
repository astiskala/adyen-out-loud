using AdyenOutLoud.Models;

namespace AdyenOutLoud.Abstractions;

public interface ITextToSpeechService
{
    Task<SpeechDiagnostic> SpeakAsync(string text, AppLanguage language, CancellationToken cancellationToken);
}
