namespace AdyenOutLoud.Abstractions;

/// <summary>
/// Service for managing background execution state on mobile platforms.
/// </summary>
public interface IBackgroundExecutionService
{
    /// <summary>
    /// Called when the application enters the foreground.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task EnterForegroundAsync();

    /// <summary>
    /// Called when the application enters the background.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task EnterBackgroundAsync();
}
