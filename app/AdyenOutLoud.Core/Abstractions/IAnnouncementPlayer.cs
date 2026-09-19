using AdyenOutLoud.Models;

namespace AdyenOutLoud.Abstractions;

/// <summary>
/// Plays the pre-recorded announcement clips bundled with the app.
/// </summary>
public interface IAnnouncementPlayer
{
    /// <summary>
    /// Plays a clip in the given language and completes when playback has finished.
    /// </summary>
    /// <param name="sound">Which announcement to play.</param>
    /// <param name="language">The language of the recording to play.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PlayAsync(AnnouncementSound sound, AppLanguage language, CancellationToken cancellationToken);
}
