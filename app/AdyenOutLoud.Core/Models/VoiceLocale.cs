namespace AdyenOutLoud.Models;

/// <summary>A TTS voice reported by the platform, reduced to the fields the selection algorithm needs.</summary>
public sealed record VoiceLocale(string Id, string Language, string Name);
