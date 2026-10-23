using System.Windows;
using windows_route_manager.Models;
using windows_route_manager.Services.Interfaces;
using windows_route_manager.Views;

namespace windows_route_manager.Services;

public sealed class WindowInteraction(Window owner) : IUserInteraction
{
    public AnnotatedRoute? EditRoute(AnnotatedRoute? existing)
    {
        var dialog = new RouteEditorWindow(existing) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }
    public bool Confirm(string title, string message) => MessageBox.Show(owner, message, title,
        MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    public void ShowError(string title, string message) => MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
}
