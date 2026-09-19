using AdyenOutLoud.Models;

namespace AdyenOutLoud.Abstractions;

/// <summary>Plays bundled announcement clips.</summary>
public interface IAnnouncementPlayer
{
    /// <summary>Plays a clip in the given language.</summary>
    /// <param name="sound">Which clip to play.</param>
    /// <param name="language">The clip's language.</param>
    /// <param name="cancellationToken">Cancels playback.</param>
    /// <returns>A task that completes when playback finishes.</returns>
    Task PlayAsync(AnnouncementSound sound, AppLanguage language, CancellationToken cancellationToken);
}
