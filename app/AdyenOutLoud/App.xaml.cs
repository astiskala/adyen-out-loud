namespace AdyenOutLoud;

/// <summary>
/// Application entry point.
/// </summary>
public partial class App : Application
{
    private readonly Func<MainPage> _createMainPage;
    private readonly Services.AppLifecycleCoordinator _lifecycle;

    /// <summary>
    /// Initializes a new instance of the <see cref="App"/> class.
    /// </summary>
    /// <param name="createMainPage">
    /// Creates the main page. It is a factory, not the page itself, because the page's XAML resolves
    /// <c>{StaticResource}</c> keys defined in <c>App.xaml</c>; injecting the built page would construct it
    /// before <see cref="InitializeComponent"/> below has loaded those resources.
    /// </param>
    /// <param name="lifecycle">The application lifecycle coordinator.</param>
    public App(Func<MainPage> createMainPage, Services.AppLifecycleCoordinator lifecycle)
    {
        InitializeComponent();
        _createMainPage = createMainPage;
        _lifecycle = lifecycle;
    }

    /// <inheritdoc />
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_createMainPage());
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
