using System.Diagnostics;
using System.Security.Principal;

namespace windows_route_manager.Services;

public static class PrivilegeService
{
    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
    public static void OpenAsAdministrator()
    {
        var path = Environment.ProcessPath ?? throw new InvalidOperationException("The application executable could not be located.");
        if (!System.IO.Path.GetFileName(path).Equals("WindowsRouteManager.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Start WindowsRouteManager.exe directly to use elevation.");
        // No pending operation or profile is passed to the elevated process. The user must select and retry.
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" })?.Dispose();
    }
}
