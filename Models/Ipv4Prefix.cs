using System.Globalization;

namespace windows_route_manager.Models;

public sealed record Ipv4Prefix
{
    private Ipv4Prefix(uint network, int prefixLength)
    {
        Network = network;
        PrefixLength = prefixLength;
    }

    public uint Network { get; }
    public int PrefixLength { get; }
    public bool IsDefault => PrefixLength == 0;

    public static Ipv4Prefix Parse(string? value)
    {
        if (!TryParse(value, out var prefix))
            throw new ArgumentException("Enter an IPv4 address or CIDR prefix, for example 192.168.1.0/24.", nameof(value));
        return prefix!;
    }

    public static bool TryParse(string? value, out Ipv4Prefix? prefix)
    {
        prefix = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Trim().Split('/');
        if (parts.Length > 2)
            return false;
        var prefixLength = 32;
        if (parts.Length == 2 && (!IsDecimal(parts[1]) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out prefixLength) || prefixLength > 32))
            return false;

        var octets = parts[0].Split('.');
        if (octets.Length != 4)
            return false;
        uint address = 0;
        foreach (var octet in octets)
        {
            // Reject alternate IPAddress syntaxes (hex, shortened addresses and ambiguous leading zeros).
            if (!IsDecimal(octet) || octet.Length > 3 || (octet.Length > 1 && octet[0] == '0') ||
                !byte.TryParse(octet, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                return false;
            address = (address << 8) | number;
        }

        var mask = prefixLength == 0 ? 0u : uint.MaxValue << (32 - prefixLength);
        prefix = new Ipv4Prefix(address & mask, prefixLength);
        return true;
    }

    private static bool IsDecimal(string value) => value.Length > 0 && value.All(c => c is >= '0' and <= '9');

    public override string ToString() =>
        $"{Network >> 24}.{(Network >> 16) & 255}.{(Network >> 8) & 255}.{Network & 255}/{PrefixLength}";
}
