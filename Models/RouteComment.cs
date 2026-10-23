namespace windows_route_manager.Models;

public sealed record RouteComment(VpnProfileId Profile, Ipv4Prefix Prefix, string Comment);
