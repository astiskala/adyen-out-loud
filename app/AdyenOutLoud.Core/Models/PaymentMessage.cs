namespace AdyenOutLoud.Models;

/// <summary>
/// Represents a successful payment message received from the relay.
/// </summary>
/// <param name="Id">Unique event identifier (e.g., "payment:pspReference").</param>
/// <param name="Type">Message type, always "payment_succeeded".</param>
/// <param name="OccurredAt">Timestamp when the payment occurred.</param>
/// <param name="TerminalId">Full terminal POIID (e.g., "V400m-324688170").</param>
/// <param name="TransactionId">Adyen transaction identifier.</param>
/// <param name="PspReference">Adyen PSP reference for the transaction.</param>
public sealed record PaymentMessage(
    string Id,
    string Type,
    DateTimeOffset OccurredAt,
    string TerminalId,
    string TransactionId,
    string PspReference);

/// <summary>
/// Result of a payment announcement attempt.
/// </summary>
/// <param name="EventId">The event identifier from the payment message.</param>
/// <param name="WasDuplicate">True if this event was already announced (duplicate).</param>
/// <param name="WasPlayed">True if the announcement clip was played.</param>
/// <param name="Detail">Human-readable detail about the result.</param>
/// <param name="Message">The original payment message.</param>
public sealed record AnnouncementResult(
    string EventId,
    bool WasDuplicate,
    bool WasPlayed,
    string Detail,
    PaymentMessage Message);

/// <summary>
/// Represents the current state of the relay connection.
/// </summary>
public enum RelayConnectionState
{
    /// <summary>
    /// The connection is being established or re-established.
    /// </summary>
    Connecting,

    /// <summary>
    /// The connection is active and listening for payments.
    /// </summary>
    Listening,

    /// <summary>
    /// The connection needs user attention (configuration missing, error, etc.).
    /// </summary>
    NeedsAttention
}

/// <summary>
/// Status information for the relay connection.
/// </summary>
/// <param name="State">The current connection state.</param>
/// <param name="Detail">Human-readable detail about the current state.</param>
public sealed record RelayStatus(RelayConnectionState State, string Detail);
