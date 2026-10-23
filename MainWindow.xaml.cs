using System.ComponentModel;
using System.IO;
using System.Windows;
using windows_route_manager.Services;
using windows_route_manager.Services.PowerShell;
using windows_route_manager.Services.Vpn;
using windows_route_manager.ViewModels;

namespace windows_route_manager;

public partial class MainWindow : Window
{
    private readonly VpnRoutesViewModel _viewModel;

    public MainWindow() : this(null) { }

    public MainWindow(VpnRoutesViewModel? viewModel)
    {
        InitializeComponent();
        if (viewModel is null)
        {
            var service = new VpnProfileService(new PowerShellRunner());
            var comments = new JsonRouteCommentStore(Path.Combine(AppContext.BaseDirectory, "route-comments.json"));
            viewModel = new VpnRoutesViewModel(service, service, new WindowInteraction(this), comments);
        }
        _viewModel = viewModel;
        DataContext = _viewModel;
        var elevated = PrivilegeService.IsAdministrator();
        PrivilegeLabel.Text = elevated ? "Administrator" : "Standard permissions";
        ElevateButton.Visibility = elevated ? Visibility.Collapsed : Visibility.Visible;
        Loaded += async (_, _) => await _viewModel.RefreshCommand.ExecuteAsync();
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_viewModel.IsWriting)
        {
            e.Cancel = true;
            MessageBox.Show(this, "Please wait for saving and any VPN verification to finish before closing.", "Operation in progress", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _viewModel.CancelRead();
    }

    private void Elevate_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Open a separate administrator window through Windows UAC? No operation is retried automatically.\n\n" +
            "If you enter another account's credentials, its User VPN profiles will be shown. Select the correct All Users profile in the new window.",
            "Open as administrator", MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        try { PrivilegeService.OpenAsAdministrator(); }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223) { /* UAC was canceled by the user. */ }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Could not open administrator window", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
