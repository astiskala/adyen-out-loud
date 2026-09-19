using System.Text.Json;
using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Models;
using Microsoft.Maui.Storage;

namespace AdyenOutLoud.Services;

/// <summary><see cref="Preferences"/>-backed settings: the language and the IDs of recently announced events.</summary>
public sealed class PreferencesSettingsService : ISettingsService, IDisposable
{
    private const string LangKey = "announcement-language";
    private const string EventsKey = "recent-event-ids-v1";
    private const int Capacity = 40;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<RecentEvent>? _events;

    /// <inheritdoc />
    public AppLanguage SelectedLanguage
    {
        get => AppLanguage.FromCode(Preferences.Default.Get(LangKey, AppLanguage.English.Code));
        set { if (!AppLanguage.All.Any(l => l.Code == value.Code)) throw new ArgumentOutOfRangeException(nameof(value)); Preferences.Default.Set(LangKey, value.Code); }
    }

    /// <inheritdoc />
    public async Task<bool> TryReserveEventIdAsync(string eventId, DateTimeOffset receivedAt, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        await _gate.WaitAsync(ct);
        try
        {
            _events ??= Load();
            if (_events.Any(e => e.Id == eventId)) return false;
            _events = [new(eventId, receivedAt.ToUnixTimeMilliseconds()), .. _events.OrderByDescending(e => e.SeenAt).Take(Capacity - 1)];
            Preferences.Default.Set(EventsKey, JsonSerializer.Serialize(_events, RecentEventJsonContext.Default.ListRecentEvent));
            return true;
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    private static List<RecentEvent> Load()
    {
        try { return JsonSerializer.Deserialize(Preferences.Default.Get(EventsKey, "[]"), RecentEventJsonContext.Default.ListRecentEvent) ?? []; }
        catch (JsonException) { Preferences.Default.Remove(EventsKey); return []; }
    }
}

internal sealed record RecentEvent(string Id, long SeenAt);
[System.Text.Json.Serialization.JsonSerializable(typeof(List<RecentEvent>))]
internal sealed partial class RecentEventJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
