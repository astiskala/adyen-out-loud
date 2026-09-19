namespace AdyenOutLoud;

/// <summary>
/// Application entry point.
/// </summary>
public partial class App : Application
{
    private readonly MainPage _mainPage;
    private readonly Services.AppLifecycleCoordinator _lifecycle;

    /// <summary>
    /// Initializes a new instance of the <see cref="App"/> class.
    /// </summary>
    /// <param name="mainPage">The main page of the application.</param>
    /// <param name="lifecycle">The application lifecycle coordinator.</param>
    public App(MainPage mainPage, Services.AppLifecycleCoordinator lifecycle)
    {
        InitializeComponent();
        _mainPage = mainPage;
        _lifecycle = lifecycle;
    }

    /// <inheritdoc />
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_mainPage);
        window.Activated += OnActivated;
        window.Stopped += OnStopped;
        window.Resumed += OnResumed;
        window.Destroying += OnDestroying;
        return window;
    }

    private void OnActivated(object? sender, EventArgs eventArgs) => _lifecycle.EnterForeground();

    private void OnResumed(object? sender, EventArgs eventArgs) => _lifecycle.EnterForeground();

    private async void OnStopped(object? sender, EventArgs eventArgs) => await _lifecycle.LeaveForegroundAsync();

    private async void OnDestroying(object? sender, EventArgs eventArgs)
    {
        if (sender is Window window)
        {
            window.Activated -= OnActivated;
            window.Stopped -= OnStopped;
            window.Resumed -= OnResumed;
            window.Destroying -= OnDestroying;
        }

        await _lifecycle.LeaveForegroundAsync();
    }
}
