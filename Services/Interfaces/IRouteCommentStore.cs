using windows_route_manager.Models;

namespace windows_route_manager.Services.Interfaces;

public interface IRouteCommentStore
{
    Task<IReadOnlyList<RouteComment>> LoadAsync(CancellationToken cancellationToken);
    Task SetAsync(VpnProfileId profile, Ipv4Prefix prefix, string comment, CancellationToken cancellationToken);
}
