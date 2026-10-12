using windows_route_manager.Models;

namespace windows_route_manager.Services.Interfaces;

public interface IVpnProfileWriter
{
    Task AddRouteAsync(VpnProfileId id, VpnRoute route, CancellationToken cancellationToken);
    Task RemoveRouteAsync(VpnProfileId id, VpnRoute expectedRoute, CancellationToken cancellationToken);
    Task ChangeMetricAsync(VpnProfileId id, VpnRoute expectedRoute, uint metric, CancellationToken cancellationToken);
    Task SetSplitTunnelingAsync(VpnProfileId id, bool expectedValue, bool enabled, CancellationToken cancellationToken);
}
