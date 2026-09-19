namespace AdyenOutLoud.Models;

/// <summary>
/// The pre-recorded announcements the app can play.
/// </summary>
public enum AnnouncementSound
{
    /// <summary>
    /// Spoken when a payment succeeds.
    /// </summary>
    PaymentReceived,

    /// <summary>
    /// Spoken by the in-app test button.
    /// </summary>
    TestAnnouncement,
}
