using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Models;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Graphics;

namespace AdyenOutLoud.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IRelayConnectionService _relay;
    private readonly IRelayConfigurationService _configuration;
    private readonly ITextToSpeechService _textToSpeech;
    private readonly ILocalizationService _localization;
    private readonly ISettingsService _settings;
    private readonly IPaymentAnnouncementService _announcements;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private bool _initialized;
    private string _relayUrlInput = string.Empty;
    private string _terminalSerialInput = string.Empty;
    private string _configurationStatus = string.Empty;
    private string _statusTitle = "CONNECTING";
    private string _statusDetail = "Preparing the payment listener...";
    private Color _statusColor = Color.FromArgb("#F7B955");
    private string _diagnostic = "Run Test voice to inspect the selected installed voice.";
    private string _latestEvent = "No payment event received yet.";

    public MainViewModel(
        IRelayConnectionService relay,
        IRelayConfigurationService configuration,
        ITextToSpeechService textToSpeech,
        ILocalizationService localization,
        ISettingsService settings,
        IPaymentAnnouncementService announcements)
    {
        _relay = relay;
        _configuration = configuration;
        _textToSpeech = textToSpeech;
        _localization = localization;
        _settings = settings;
        _announcements = announcements;
        relay.StatusChanged += OnStatusChanged;
        relay.Diagnostic += OnDiagnostic;
        announcements.AnnouncementCompleted += OnAnnouncementCompleted;
        TestVoiceCommand = new AsyncCommand(TestVoiceAsync);
        SaveConfigurationCommand = new AsyncCommand(SaveConfigurationAsync);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // CA1822 suggests making this static since it doesn't read instance state, but XAML's compiled
    // {Binding Languages} resolves against the page's instance DataContext and cannot bind to a
    // static member — see MainPage.xaml. Keeping this an instance member is required, not style.
#pragma warning disable CA1822
    public IReadOnlyList<AppLanguage> Languages => AppLanguage.All;
#pragma warning restore CA1822

    public string FooterText { get; } = OperatingSystem.IsIOS()
        ? "On iOS, keep Adyen Out Loud in the foreground while taking payments."
        : "Adyen Out Loud keeps listening for payments while running in the background on this platform.";

    public AppLanguage SelectedLanguage
    {
        get => _settings.SelectedLanguage;
        set
        {
            if (value.Code == _settings.SelectedLanguage.Code) return;
            _settings.SelectedLanguage = value;
            OnPropertyChanged();
        }
    }

    public string RelayUrlInput { get => _relayUrlInput; set => Set(ref _relayUrlInput, value); }
    public string TerminalSerialInput { get => _terminalSerialInput; set => Set(ref _terminalSerialInput, value); }
    public string ConfigurationStatus { get => _configurationStatus; private set => Set(ref _configurationStatus, value); }
    public string StatusTitle { get => _statusTitle; private set => Set(ref _statusTitle, value); }
    public string StatusDetail { get => _statusDetail; private set => Set(ref _statusDetail, value); }
    public Color StatusColor { get => _statusColor; private set => Set(ref _statusColor, value); }
    public string Diagnostic { get => _diagnostic; private set => Set(ref _diagnostic, value); }
    public string LatestEvent { get => _latestEvent; private set => Set(ref _latestEvent, value); }
    public ICommand TestVoiceCommand { get; }
    public ICommand SaveConfigurationCommand { get; }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        await _initializationGate.WaitAsync();
        try
        {
            if (_initialized) return;
            var configuration = await _configuration.GetAsync();
            if (configuration is not null)
            {
                RelayUrlInput = configuration.BaseUrl.AbsoluteUri;
                TerminalSerialInput = configuration.TerminalSerial;
            }
            _initialized = true;
        }
        catch (Exception exception)
        {
            StatusTitle = "NEEDS ATTENTION";
            StatusColor = Color.FromArgb("#FF6961");
            StatusDetail = "Could not read the saved relay configuration.";
            Diagnostic = exception.Message;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private async Task SaveConfigurationAsync()
    {
        if (!Uri.TryCreate(RelayUrlInput.Trim(), UriKind.Absolute, out var baseUrl))
        {
            ConfigurationStatus = "Enter a valid https:// relay URL.";
            return;
        }

        var terminalSerial = TerminalSerialInput.Trim();
        if (terminalSerial.Length == 0)
        {
            ConfigurationStatus = "Enter this device's terminal serial number.";
            return;
        }

        try
        {
            await _relay.StopAsync();
            await _configuration.SaveAsync(baseUrl, terminalSerial);
            ConfigurationStatus = "Saved. Connecting...";
            _relay.Start();
        }
        catch (Exception exception)
        {
            ConfigurationStatus = $"Could not save: {exception.Message}";
        }
    }

    private async Task TestVoiceAsync()
    {
        try
        {
            var language = SelectedLanguage;
            var result = await _textToSpeech.SpeakAsync(
                _localization.CreateTestAnnouncement(language), language, CancellationToken.None);
            Diagnostic = result.Message;
        }
        catch (Exception exception)
        {
            Diagnostic = $"Voice test failed: {exception.Message}";
        }
    }

    private void OnStatusChanged(object? sender, RelayStatus status) => MainThread.BeginInvokeOnMainThread(() =>
    {
        StatusTitle = status.State switch
        {
            RelayConnectionState.Listening => "LISTENING",
            RelayConnectionState.NeedsAttention => "NEEDS ATTENTION",
            _ => "CONNECTING"
        };
        StatusColor = status.State switch
        {
            RelayConnectionState.Listening => Color.FromArgb("#0ABF53"),
            RelayConnectionState.NeedsAttention => Color.FromArgb("#FF6961"),
            _ => Color.FromArgb("#F7B955")
        };
        StatusDetail = status.Detail;
    });

    private void OnDiagnostic(object? sender, string message) =>
        MainThread.BeginInvokeOnMainThread(() => Diagnostic = message);

    private void OnAnnouncementCompleted(object? sender, AnnouncementResult result) => MainThread.BeginInvokeOnMainThread(() =>
    {
        var payment = result.Message;
        LatestEvent = $"Terminal {payment.TerminalId} / {payment.OccurredAt.ToLocalTime():g}\nTransaction {payment.TransactionId} / PSP {payment.PspReference}\nEvent {payment.Id}";
        Diagnostic = result.Speech?.Message ?? result.Detail;
    });

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new(propertyName));

    public void Dispose()
    {
        _relay.StatusChanged -= OnStatusChanged;
        _relay.Diagnostic -= OnDiagnostic;
        _announcements.AnnouncementCompleted -= OnAnnouncementCompleted;
        _initializationGate.Dispose();
    }
}
