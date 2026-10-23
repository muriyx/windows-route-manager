namespace windows_route_manager.Models;

public sealed record AnnotatedRoute(VpnRoute Route, string Comment = "")
{
    public Ipv4Prefix Prefix => Route.Prefix;
    public string DestinationPrefix => Route.DestinationPrefix;
    public uint Metric => Route.Metric;
}
