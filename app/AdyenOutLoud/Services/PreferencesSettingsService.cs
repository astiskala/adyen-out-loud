using System.Text.Json;
using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Models;
using Microsoft.Maui.Storage;

namespace AdyenOutLoud.Services;

public sealed class PreferencesSettingsService : ISettingsService, IDisposable
{
    private const string LanguageKey = "announcement-language";
    private const string RecentEventsKey = "recent-event-ids-v1";
    // Keep the JSON value below the Windows Preferences per-value size limit.
    private const int EventCapacity = 40;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<RecentEvent>? _events;

    public AppLanguage SelectedLanguage
    {
        get => AppLanguage.FromCode(Preferences.Default.Get(LanguageKey, AppLanguage.English.Code));
        set
        {
            if (!AppLanguage.All.Any(language => language.Code == value.Code))
                throw new ArgumentOutOfRangeException(nameof(value));
            Preferences.Default.Set(LanguageKey, value.Code);
        }
    }

    public async Task<bool> TryReserveEventIdAsync(string eventId, DateTimeOffset receivedAt, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _events ??= Load();
            if (_events.Any(item => item.Id.Equals(eventId, StringComparison.Ordinal))) return false;

            var candidate = new[] { new RecentEvent(eventId, receivedAt.ToUnixTimeMilliseconds()) }
                .Concat(_events.OrderByDescending(item => item.SeenAt).Take(EventCapacity - 1))
                .ToList();
            Preferences.Default.Set(RecentEventsKey, JsonSerializer.Serialize(candidate, RecentEventJsonContext.Default.ListRecentEvent));
            _events = candidate;
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static List<RecentEvent> Load()
    {
        try
        {
            var json = Preferences.Default.Get(RecentEventsKey, "[]");
            return JsonSerializer.Deserialize(json, RecentEventJsonContext.Default.ListRecentEvent) ?? [];
        }
        catch (JsonException)
        {
            Preferences.Default.Remove(RecentEventsKey);
            return [];
        }
    }

    public void Dispose() => _gate.Dispose();
}

/// <summary>Persisted shape of a recently-seen relay event ID, kept outside the class so the source generator below can see it.</summary>
internal sealed record RecentEvent(string Id, long SeenAt);

// Source-generated (reflection-free) JSON (de)serialization keeps this service trim- and NativeAOT-friendly.
[System.Text.Json.Serialization.JsonSerializable(typeof(List<RecentEvent>))]
internal sealed partial class RecentEventJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
