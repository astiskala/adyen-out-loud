namespace AdyenOutLoud.Models;

public sealed record PaymentAmount(string Currency, long ValueMinor);

public sealed record PaymentMessage(
    string Id,
    string Type,
    DateTimeOffset OccurredAt,
    string TerminalId,
    string TransactionId,
    string PspReference,
    string? PaymentMethod,
    PaymentAmount? Amount);

public sealed record SpeechDiagnostic(string RequestedLocale, string? SelectedLocale, string Message);

public sealed record AnnouncementResult(
    string EventId,
    bool ShouldAcknowledge,
    bool WasDuplicate,
    bool WasSpoken,
    string Detail,
    PaymentMessage Message,
    SpeechDiagnostic? Speech);

public enum RelayConnectionState
{
    Connecting,
    Listening,
    NeedsAttention
}

public sealed record RelayStatus(RelayConnectionState State, string Detail);
