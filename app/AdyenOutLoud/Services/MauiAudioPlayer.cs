using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Models;
using Microsoft.Maui.Storage;
using Plugin.Maui.Audio;

namespace AdyenOutLoud.Services;

/// <summary>
/// Plays the pre-recorded announcement clips bundled as app assets (<c>Resources/Raw/{Sound}-{LANG}.mp3</c>).
/// </summary>
public sealed class MauiAudioPlayer(IAudioManager audioManager) : IAnnouncementPlayer, IDisposable
{
    private readonly SemaphoreSlim _playbackGate = new(1, 1);

    /// <inheritdoc />
    public async Task PlayAsync(AnnouncementSound sound, AppLanguage language, CancellationToken cancellationToken)
    {
        await _playbackGate.WaitAsync(cancellationToken);
        try
        {
            await using var clip = await FileSystem.OpenAppPackageFileAsync($"{sound}-{language.Code.ToUpperInvariant()}.mp3");
            using var player = audioManager.CreatePlayer(clip);
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            player.PlaybackEnded += (_, _) => finished.TrySetResult();
            using var cancellation = cancellationToken.Register(() =>
            {
                player.Stop();
                finished.TrySetCanceled(cancellationToken);
            });
            player.Play();
            await finished.Task;
        }
        finally
        {
            _playbackGate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _playbackGate.Dispose();
}
