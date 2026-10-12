namespace windows_route_manager.Models;

public enum VpnScope { User, AllUsers }

public sealed record VpnProfileId(string Name, VpnScope Scope)
{
    public string ScopeLabel => Scope == VpnScope.AllUsers ? "All Users" : "User";
    public override string ToString() => $"{Name} ({ScopeLabel})";
}

public sealed record VpnRoute(Ipv4Prefix Prefix, uint Metric)
{
    public string DestinationPrefix => Prefix.ToString();
}

public sealed record VpnProfile(
    VpnProfileId Id, string ConnectionStatus, string TunnelType,
    bool SplitTunneling, IReadOnlyList<VpnRoute> Routes, int HiddenIpv6RouteCount = 0)
{
    public string DisplayName => Id.ToString();
    public bool IsConnected => ConnectionStatus.Equals("Connected", StringComparison.OrdinalIgnoreCase);
}

public sealed record VpnProfileList(IReadOnlyList<VpnProfile> Profiles, IReadOnlyList<string> Warnings);
