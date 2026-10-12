using System.Windows;
using windows_route_manager.Models;
using windows_route_manager.ViewModels;

namespace windows_route_manager.Views;

public partial class RouteEditorWindow : Window
{
    private readonly RouteEditorViewModel _viewModel;
    public RouteEditorWindow(VpnRoute? existing)
    {
        InitializeComponent();
        _viewModel = new RouteEditorViewModel(existing);
        DataContext = _viewModel;
        Loaded += (_, _) => { if (_viewModel.IsNew) DestinationBox.Focus(); else MetricBox.Focus(); };
    }
    public VpnRoute? Result { get; private set; }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Result = _viewModel.Validate();
        if (Result is not null) DialogResult = true;
    }
}
