using windows_route_manager.Models;
using windows_route_manager.Services.Interfaces;
using windows_route_manager.Services.PowerShell;

namespace windows_route_manager.Services.Vpn;

public sealed class VpnProfileService(IPowerShellRunner runner) : IVpnProfileReader, IVpnProfileWriter
{
    public async Task<VpnProfileList> GetProfilesAsync(CancellationToken cancellationToken)
    {
        var response = await runner.RunAsync(new PowerShellRequest(VpnOperation.ListProfiles), cancellationToken).ConfigureAwait(false);
        return new VpnProfileList(response.Data.EnumerateArray().Select(VpnProfileMapper.FromJson)
            .OrderBy(p => p.Id.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Id.Scope).ToArray(), response.Warnings);
    }

    public async Task<VpnProfile> GetProfileAsync(VpnProfileId id, CancellationToken cancellationToken)
    {
        var response = await runner.RunAsync(PowerShellRequest.ForProfile(VpnOperation.GetProfile, id), cancellationToken).ConfigureAwait(false);
        var profile = VpnProfileMapper.FromJson(response.Data);
        if (profile.Id.Scope != id.Scope || !profile.Id.Name.Equals(id.Name, StringComparison.OrdinalIgnoreCase))
            throw new PowerShellException("Windows returned a different VPN profile. Refresh and select the profile again.");
        if (response.Warnings.Count > 0)
            throw new PowerShellException("Windows reported a warning while reading the profile: " + string.Join("\n", response.Warnings));
        return profile;
    }

    public Task AddRouteAsync(VpnProfileId id, VpnRoute route, CancellationToken cancellationToken) =>
        WriteAsync(PowerShellRequest.ForProfile(VpnOperation.AddRoute, id) with
        { DestinationPrefix = route.DestinationPrefix, Metric = route.Metric }, cancellationToken);

    public Task RemoveRouteAsync(VpnProfileId id, VpnRoute expectedRoute, CancellationToken cancellationToken) =>
        WriteAsync(PowerShellRequest.ForProfile(VpnOperation.RemoveRoute, id) with
        { DestinationPrefix = expectedRoute.DestinationPrefix, ExpectedMetric = expectedRoute.Metric }, cancellationToken);

    public Task ChangeMetricAsync(VpnProfileId id, VpnRoute expectedRoute, uint metric, CancellationToken cancellationToken) =>
        WriteAsync(PowerShellRequest.ForProfile(VpnOperation.ChangeMetric, id) with
        { DestinationPrefix = expectedRoute.DestinationPrefix, Metric = metric, ExpectedMetric = expectedRoute.Metric }, cancellationToken);

    public Task SetSplitTunnelingAsync(VpnProfileId id, bool expectedValue, bool enabled, CancellationToken cancellationToken) =>
        WriteAsync(PowerShellRequest.ForProfile(VpnOperation.SetSplitTunneling, id) with
        { SplitTunneling = enabled, ExpectedSplitTunneling = expectedValue }, cancellationToken);

    private async Task WriteAsync(PowerShellRequest request, CancellationToken cancellationToken)
    {
        var response = await runner.RunAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.Warnings.Count > 0)
            throw new PowerShellException("The write completed with Windows warnings. Verify the refreshed profile.\n" + string.Join("\n", response.Warnings));
    }
}
