using System.Globalization;
using windows_route_manager.Helpers;
using windows_route_manager.Models;

namespace windows_route_manager.ViewModels;

public sealed class RouteEditorViewModel : ObservableObject
{
    private string _destination;
    private string _metric;
    private string _error = "";

    public RouteEditorViewModel(VpnRoute? existing)
    {
        IsNew = existing is null;
        _destination = existing?.DestinationPrefix ?? "";
        _metric = existing?.Metric.ToString(CultureInfo.InvariantCulture) ?? "1";
    }

    public bool IsNew { get; }
    public string Title => IsNew ? "Add VPN route" : "Edit route metric";
    public string Destination
    {
        get => _destination;
        set
        {
            if (SetProperty(ref _destination, value))
                OnPropertyChanged(nameof(NormalizedPrefix));
        }
    }
    public string Metric { get => _metric; set => SetProperty(ref _metric, value); }
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    public string NormalizedPrefix => Ipv4Prefix.TryParse(Destination, out var prefix)
        ? $"Will save: {prefix}" : "Enter IPv4 or CIDR; a single address becomes /32.";

    public VpnRoute? Validate()
    {
        try
        {
            var route = new VpnRoute(Ipv4Prefix.Parse(Destination), RouteMetric.Parse(Metric));
            Error = "";
            return route;
        }
        catch (ArgumentException exception)
        {
            Error = exception.Message;
            return null;
        }
    }
}
