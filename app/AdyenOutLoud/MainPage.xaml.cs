using AdyenOutLoud.ViewModels;

namespace AdyenOutLoud;

/// <summary>
/// Main page of the application.
/// </summary>
public partial class MainPage : ContentPage
{
    private readonly MainViewModel _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainPage"/> class.
    /// </summary>
    /// <param name="viewModel">The main view model.</param>
    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <inheritdoc />
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InitializeAsync();
    }
}
