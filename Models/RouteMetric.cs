using System.Globalization;

namespace windows_route_manager.Models;

public static class RouteMetric
{
    // VpnClient declares RouteMetric as UInt32; do not apply New-NetRoute's UInt16 limit here.
    public static uint Parse(string? value)
    {
        if (!uint.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var metric))
            throw new ArgumentException("Route metric must be a whole number from 0 to 4294967295.", nameof(value));
        return metric;
    }
}
