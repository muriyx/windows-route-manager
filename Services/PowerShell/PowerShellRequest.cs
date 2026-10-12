using windows_route_manager.Models;

namespace windows_route_manager.Services.PowerShell;

public enum VpnOperation { ListProfiles, GetProfile, AddRoute, RemoveRoute, ChangeMetric, SetSplitTunneling }

public sealed record PowerShellRequest(
    VpnOperation Operation, string? Name = null, bool AllUserConnection = false,
    string? DestinationPrefix = null, uint? Metric = null, uint? ExpectedMetric = null,
    bool? SplitTunneling = null, bool? ExpectedSplitTunneling = null)
{
    public static PowerShellRequest ForProfile(VpnOperation operation, VpnProfileId id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Name);
        if (id.Name.Any(char.IsControl) || !Enum.IsDefined(id.Scope))
            throw new ArgumentException("Invalid VPN profile identity.", nameof(id));
        return new PowerShellRequest(operation, id.Name, id.Scope == VpnScope.AllUsers);
    }
}
