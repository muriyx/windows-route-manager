using System.Collections.ObjectModel;
using windows_route_manager.Helpers;
using windows_route_manager.Models;
using windows_route_manager.Services.Interfaces;
using windows_route_manager.Services.PowerShell;

namespace windows_route_manager.ViewModels;

public sealed class VpnRoutesViewModel : ObservableObject
{
    private readonly IVpnProfileReader _reader;
    private readonly IVpnProfileWriter _writer;
    private readonly IUserInteraction _interaction;
    private CancellationTokenSource? _operation;
    private VpnProfile? _selectedProfile;
    private VpnRoute? _selectedRoute;
    private bool _isBusy;
    private bool _isWriting;
    private string _filter = "";
    private string _status = "Refresh to discover built-in Windows VPN profiles.";
    private string _warnings = "";
    private bool _splitTunneling;

    public VpnRoutesViewModel(IVpnProfileReader reader, IVpnProfileWriter writer, IUserInteraction interaction)
    {
        _reader = reader;
        _writer = writer;
        _interaction = interaction;
        RefreshCommand = new AsyncCommand(() => RunAsync("Reading VPN profiles…", RefreshCoreAsync), ReportError, () => !IsBusy);
        AddCommand = new AsyncCommand(AddAsync, ReportError, () => CanEdit);
        EditCommand = new AsyncCommand(EditAsync, ReportError, () => CanEdit && SelectedRoute is not null);
        RemoveCommand = new AsyncCommand(RemoveAsync, ReportError, () => CanEdit && SelectedRoute is not null);
        ApplySplitCommand = new AsyncCommand(ApplySplitAsync, ReportError,
            () => CanEdit && SplitTunneling != SelectedProfile!.SplitTunneling);
        CancelCommand = new RelayCommand(CancelRead, () => CanCancel);
    }

    public ObservableCollection<VpnProfile> Profiles { get; } = [];
    public ObservableCollection<VpnRoute> Routes { get; } = [];
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand AddCommand { get; }
    public AsyncCommand EditCommand { get; }
    public AsyncCommand RemoveCommand { get; }
    public AsyncCommand ApplySplitCommand { get; }
    public RelayCommand CancelCommand { get; }

    public VpnProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!SetProperty(ref _selectedProfile, value)) return;
            SplitTunneling = value?.SplitTunneling ?? false;
            UpdateRoutes();
            OnPropertyChanged(nameof(ProfileDetails));
            OnPropertyChanged(nameof(ProfileNotice));
            UpdateCommands();
        }
    }
    public VpnRoute? SelectedRoute
    {
        get => _selectedRoute;
        set { if (SetProperty(ref _selectedRoute, value)) UpdateCommands(); }
    }
    public string Filter
    {
        get => _filter;
        set { if (SetProperty(ref _filter, value)) UpdateRoutes(); }
    }
    public bool SplitTunneling
    {
        get => _splitTunneling;
        set { if (SetProperty(ref _splitTunneling, value)) ApplySplitCommand.NotifyCanExecuteChanged(); }
    }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public bool IsWriting { get => _isWriting; private set => SetProperty(ref _isWriting, value); }
    public bool IsIdle => !IsBusy;
    public bool CanEdit => !IsBusy && SelectedProfile is not null;
    public bool CanCancel => IsBusy && !IsWriting;
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Warnings { get => _warnings; private set => SetProperty(ref _warnings, value); }
    public string RouteCount => $"{Routes.Count} shown / {SelectedProfile?.Routes.Count ?? 0} saved IPv4 routes";
    public string EmptyMessage => SelectedProfile is null
        ? "No profile selected. Refresh to find VPN profiles created in Windows Settings."
        : SelectedProfile.Routes.Count == 0 ? "No saved IPv4 routes. Use Add route to create one." : "No routes match the filter.";
    public string ProfileDetails => SelectedProfile is { } profile
        ? $"{profile.Id.ScopeLabel}  •  {profile.ConnectionStatus}  •  {profile.TunnelType}" : "Select a built-in Windows VPN profile";
    public string ProfileNotice => SelectedProfile is { } profile
        ? (profile.IsConnected ? "Connected: reconnect the VPN in Windows to apply profile changes. " : "Windows applies these saved routes when the VPN connects. ") +
          (profile.Id.Scope == VpnScope.AllUsers ? "Editing an All Users profile may require administrator rights. " : "") +
          (profile.HiddenIpv6RouteCount > 0 ? $"{profile.HiddenIpv6RouteCount} IPv6 route(s) are hidden and preserved." : "") : "";

    private void UpdateRoutes()
    {
        SelectedRoute = null;
        Routes.Clear();
        foreach (var route in SelectedProfile?.Routes ?? [])
            if (route.DestinationPrefix.Contains(Filter.Trim(), StringComparison.OrdinalIgnoreCase)) Routes.Add(route);
        OnPropertyChanged(nameof(RouteCount));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        var selectedId = SelectedProfile?.Id;
        var result = await _reader.GetProfilesAsync(cancellationToken);
        SelectedProfile = null;
        Profiles.Clear();
        foreach (var profile in result.Profiles) Profiles.Add(profile);
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == selectedId) ?? Profiles.FirstOrDefault();
        Warnings = string.Join("\n", result.Warnings);
        Status = $"Read {Profiles.Count} profile(s) at {DateTime.Now:t}. {RouteCount}.";
    }

    private async Task AddAsync()
    {
        var profile = SelectedProfile!;
        var route = _interaction.EditRoute(null);
        if (route is null) return;
        if (profile.Routes.Any(r => r.Prefix == route.Prefix))
        {
            _interaction.ShowError("Duplicate route", $"{route.DestinationPrefix} already exists in {profile.DisplayName}. Edit its metric instead.");
            return;
        }
        if (!ConfirmDefault(route, "Add")) return;
        await RunAsync("Saving VPN route…", token => _writer.AddRouteAsync(profile.Id, route, token), true);
    }

    private async Task EditAsync()
    {
        var profile = SelectedProfile!;
        var original = SelectedRoute!;
        var edited = _interaction.EditRoute(original);
        if (edited is null || edited.Metric == original.Metric) return;
        if (edited.Prefix != original.Prefix)
        {
            _interaction.ShowError("Invalid edit", "Only the metric can be edited. Add a new route to change the destination.");
            return;
        }
        if (!ConfirmDefault(original, "Change metric for")) return;
        await RunAsync("Updating route metric…", token => _writer.ChangeMetricAsync(profile.Id, original, edited.Metric, token), true);
    }

    private async Task RemoveAsync()
    {
        var profile = SelectedProfile!;
        var route = SelectedRoute!;
        var warning = route.Prefix.IsDefault ? "\n\nThis is a DEFAULT ROUTE (0.0.0.0/0). Removing it can change Internet access and traffic routing." : "";
        if (!_interaction.Confirm("Remove VPN route", $"Remove {route.DestinationPrefix} from {profile.DisplayName}?{warning}")) return;
        await RunAsync("Removing VPN route…", token => _writer.RemoveRouteAsync(profile.Id, route, token), true);
    }

    private async Task ApplySplitAsync()
    {
        var profile = SelectedProfile!;
        var enabled = SplitTunneling;
        var effect = enabled
            ? "Enable split tunneling? Traffic outside the VPN routes can use your regular network. Review your organization's policy."
            : "Disable split tunneling? Windows may send default traffic through this VPN, changing Internet access.";
        if (!_interaction.Confirm("Change split tunneling", $"{profile.DisplayName}\n\n{effect}\n\nReconnect an active VPN to apply the change."))
        {
            SplitTunneling = profile.SplitTunneling;
            return;
        }
        await RunAsync("Saving split tunneling…", token => _writer.SetSplitTunnelingAsync(profile.Id, profile.SplitTunneling, enabled, token), true);
    }

    private bool ConfirmDefault(VpnRoute route, string action) => !route.Prefix.IsDefault ||
        _interaction.Confirm("Default route warning", $"{action} 0.0.0.0/0? This route matches ALL IPv4 destinations and can change Internet access or override split tunneling.");

    private async Task RunAsync(string message, Func<CancellationToken, Task> action, bool writing = false)
    {
        if (IsBusy) return;
        var targetId = SelectedProfile?.Id;
        using var operation = new CancellationTokenSource();
        _operation = operation;
        IsBusy = true;
        IsWriting = writing;
        Status = message;
        Warnings = "";
        UpdateCommands();
        Exception? failure = null;
        try { await action(operation.Token); }
        catch (Exception exception) { failure = exception; }
        if (writing)
        {
            // Always re-read after a write attempt, including a timeout or a partial failure.
            try
            {
                await RefreshCoreAsync(CancellationToken.None);
                if (!Profiles.Any(p => p.Id == targetId))
                    throw new PowerShellException("The edited profile could not be re-read. It may have been deleted or its scope is unavailable.");
                if (failure is null) Status = "Saved and re-read from Windows. Reconnect an active VPN to apply changes.";
            }
            catch (Exception exception)
            {
                ClearSnapshot();
                failure = new PowerShellException((failure is null ? "The write completed, but refresh failed." : Describe(failure)) +
                    "\nThe current state is unknown. Refresh before retrying.\n" + Describe(exception));
            }
        }
        else if (failure is not null) ClearSnapshot();
        IsBusy = false;
        IsWriting = false;
        _operation = null;
        UpdateCommands();
        if (failure is OperationCanceledException) Status = "Read canceled. Refresh to load profiles.";
        else if (failure is not null)
        {
            Status = writing ? "The change could not be fully confirmed. Review the current profile and error details." : "Could not read VPN profiles.";
            _interaction.ShowError(writing ? "VPN change failed" : "VPN refresh failed", Describe(failure));
        }
    }

    private void ClearSnapshot() { SelectedProfile = null; Profiles.Clear(); }
    public void CancelRead() { if (!IsWriting) _operation?.Cancel(); }
    private static string Describe(Exception exception) => exception is PowerShellException powershell ? powershell.UserMessage : exception.Message;
    private void ReportError(Exception exception) => _interaction.ShowError("Unexpected error", Describe(exception));

    private void UpdateCommands()
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanCancel));
        RefreshCommand.NotifyCanExecuteChanged();
        AddCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
        ApplySplitCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }
}
