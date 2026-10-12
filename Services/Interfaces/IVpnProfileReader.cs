using windows_route_manager.Models;

namespace windows_route_manager.Services.Interfaces;

public interface IVpnProfileReader
{
    Task<VpnProfileList> GetProfilesAsync(CancellationToken cancellationToken);
    Task<VpnProfile> GetProfileAsync(VpnProfileId id, CancellationToken cancellationToken);
}
