using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Models;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Graphics;

namespace AdyenOutLoud.ViewModels;

/// <summary>
/// Main view model for the application.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IRelayConnectionService _relay;
    private readonly IRelayConfigurationService _configuration;
    private readonly ITextToSpeechService _textToSpeech;
    private readonly ILocalizationService _localization;
    private readonly ISettingsService _settings;
    private bool _isTestingVoice;
    private bool _isSavingConfig;
    private bool _initialized;
    private string _relayUrlInput = string.Empty;
    private string _terminalSerialInput = string.Empty;
    private string _configurationStatus = string.Empty;
    private string _statusTitle = "CONNECTING";
    private string _statusDetail = "Preparing the payment listener...";
    private Color _statusColor = Color.FromArgb("#F7B955");
    private string _diagnostic = "Run Test voice to inspect the selected installed voice.";
    private string _latestEvent = "No payment event received yet.";

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModel"/> class.
    /// </summary>
    /// <param name="relay">The relay connection service.</param>
    /// <param name="configuration">The relay configuration service.</param>
    /// <param name="textToSpeech">The text-to-speech service.</param>
    /// <param name="localization">The localization service.</param>
    /// <param name="settings">The settings service.</param>
    public MainViewModel(
        IRelayConnectionService relay,
        IRelayConfigurationService configuration,
        ITextToSpeechService textToSpeech,
        ILocalizationService localization,
        ISettingsService settings)
    {
        _relay = relay;
        _configuration = configuration;
        _textToSpeech = textToSpeech;
        _localization = localization;
        _settings = settings;
        relay.StatusChanged += OnStatusChanged;
        relay.Diagnostic += OnDiagnostic;
        relay.AnnouncementCompleted += OnAnnouncementCompleted;
        TestVoiceCommand = new Command(async () => await TestVoiceAsync());
        SaveConfigurationCommand = new Command(async () => await SaveConfigurationAsync());
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    // CA1822 suggests making this static since it doesn't read instance state, but XAML's compiled
    // {Binding Languages} resolves against the page's instance DataContext and cannot bind to a
    // static member — see MainPage.xaml. Keeping this an instance member is required, not style.
#pragma warning disable CA1822
    /// <summary>
    /// Gets all supported application languages.
    /// </summary>
    public IReadOnlyList<AppLanguage> Languages => AppLanguage.All;
#pragma warning restore CA1822

    /// <summary>
    /// Gets platform-specific footer text.
    /// </summary>
    public string FooterText { get; } = OperatingSystem.IsIOS()
        ? "On iOS, keep Adyen Out Loud in the foreground while taking payments."
        : "Adyen Out Loud keeps listening for payments while running in the background on this platform.";

    /// <summary>
    /// Gets or sets the selected announcement language.
    /// </summary>
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

    /// <summary>
    /// Gets or sets the relay URL input.
    /// </summary>
    public string RelayUrlInput { get => _relayUrlInput; set => Set(ref _relayUrlInput, value); }

    /// <summary>
    /// Gets or sets the terminal serial number input.
    /// </summary>
    public string TerminalSerialInput { get => _terminalSerialInput; set => Set(ref _terminalSerialInput, value); }

    /// <summary>
    /// Gets or sets the configuration status message.
    /// </summary>
    public string ConfigurationStatus { get => _configurationStatus; private set => Set(ref _configurationStatus, value); }

    /// <summary>
    /// Gets or sets the connection status title.
    /// </summary>
    public string StatusTitle { get => _statusTitle; private set => Set(ref _statusTitle, value); }

    /// <summary>
    /// Gets or sets the connection status detail.
    /// </summary>
    public string StatusDetail { get => _statusDetail; private set => Set(ref _statusDetail, value); }

    /// <summary>
    /// Gets or sets the connection status color.
    /// </summary>
    public Color StatusColor { get => _statusColor; private set => Set(ref _statusColor, value); }

    /// <summary>
    /// Gets or sets the diagnostic message.
    /// </summary>
    public string Diagnostic { get => _diagnostic; private set => Set(ref _diagnostic, value); }

    /// <summary>
    /// Gets or sets the latest payment event details.
    /// </summary>
    public string LatestEvent { get => _latestEvent; private set => Set(ref _latestEvent, value); }

    /// <summary>
    /// Gets the command to test the voice.
    /// </summary>
    public ICommand TestVoiceCommand { get; }

    /// <summary>
    /// Gets the command to save the configuration.
    /// </summary>
    public ICommand SaveConfigurationCommand { get; }

    /// <summary>
    /// Initializes the view model by loading saved configuration.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        if (_initialized) return;
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
    }

    private async Task SaveConfigurationAsync()
    {
        if (_isSavingConfig) return;
        _isSavingConfig = true;
        ((Command)SaveConfigurationCommand).ChangeCanExecute();
        try
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

            await _relay.StopAsync();
            await _configuration.SaveAsync(baseUrl, terminalSerial);
            ConfigurationStatus = "Saved. Connecting...";
            _relay.Start();
        }
        catch (Exception exception)
        {
            ConfigurationStatus = $"Could not save: {exception.Message}";
        }
        finally
        {
            _isSavingConfig = false;
            ((Command)SaveConfigurationCommand).ChangeCanExecute();
        }
    }

    private async Task TestVoiceAsync()
    {
        if (_isTestingVoice) return;
        _isTestingVoice = true;
        ((Command)TestVoiceCommand).ChangeCanExecute();
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
        finally
        {
            _isTestingVoice = false;
            ((Command)TestVoiceCommand).ChangeCanExecute();
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

    /// <inheritdoc />
    public void Dispose()
    {
        _relay.StatusChanged -= OnStatusChanged;
        _relay.Diagnostic -= OnDiagnostic;
        _relay.AnnouncementCompleted -= OnAnnouncementCompleted;
    }
}
