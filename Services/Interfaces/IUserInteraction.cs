using windows_route_manager.Models;

namespace windows_route_manager.Services.Interfaces;

public interface IUserInteraction
{
    AnnotatedRoute? EditRoute(AnnotatedRoute? existing);
    bool Confirm(string title, string message);
    void ShowError(string title, string message);
}
