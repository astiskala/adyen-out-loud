namespace AdyenOutLoud.Models;

public sealed record InstanceIdentity(string Token, Uri WebhookUrl, Uri WebSocketUrl);
