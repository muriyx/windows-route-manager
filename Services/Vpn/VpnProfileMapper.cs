using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using windows_route_manager.Models;
using windows_route_manager.Services.PowerShell;

namespace windows_route_manager.Services.Vpn;

public static class VpnProfileMapper
{
    public static VpnProfile FromJson(JsonElement value)
    {
        try
        {
            var name = value.GetProperty("Name").GetString();
            if (string.IsNullOrWhiteSpace(name))
                throw new FormatException("A VPN profile has no name.");
            var scope = value.GetProperty("AllUserConnection").GetBoolean() ? VpnScope.AllUsers : VpnScope.User;
            List<VpnRoute> routes = [];
            var hiddenIpv6 = 0;
            foreach (var item in value.GetProperty("Routes").EnumerateArray())
            {
                var destination = item.GetProperty("DestinationPrefix").GetString();
                if (destination?.Contains(':') == true &&
                    IPAddress.TryParse(destination.Split('/')[0], out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    hiddenIpv6++;
                    continue;
                }
                routes.Add(new VpnRoute(Ipv4Prefix.Parse(destination), item.GetProperty("RouteMetric").GetUInt32()));
            }
            return new VpnProfile(new VpnProfileId(name, scope),
                value.GetProperty("ConnectionStatus").GetString() ?? "Unknown",
                value.GetProperty("TunnelType").GetString() ?? "Unknown",
                value.GetProperty("SplitTunneling").GetBoolean(), routes, hiddenIpv6);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException)
        {
            throw new PowerShellException("Windows returned an unsupported VPN profile or route format. " + exception.Message, inner: exception);
        }
    }
}
