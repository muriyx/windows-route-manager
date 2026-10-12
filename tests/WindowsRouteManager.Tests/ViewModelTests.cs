using windows_route_manager.Models;
using windows_route_manager.Services.Interfaces;
using windows_route_manager.Services.PowerShell;
using windows_route_manager.ViewModels;

namespace WindowsRouteManager.Tests;

public sealed class ViewModelTests
{
    [Fact]
    public async Task RefreshPreservesIdentityIncludingScopeAndFilteringClearsSelection()
    {
        var fake = new FakeVpn();
        fake.Profiles.Add(fake.Profiles[0] with { Id = new VpnProfileId("Office", VpnScope.AllUsers) });
        var vm = Create(fake);
        await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedProfile = vm.Profiles[1];
        await vm.RefreshCommand.ExecuteAsync();
        Assert.Equal(VpnScope.AllUsers, vm.SelectedProfile!.Id.Scope);
        vm.SelectedRoute = vm.Routes[0];
        vm.Filter = "192.168";
        Assert.Empty(vm.Routes);
        Assert.Null(vm.SelectedRoute);
        Assert.False(vm.RemoveCommand.CanExecute(null));
    }

    [Fact]
    public async Task DuplicateNormalizedPrefixDoesNotWrite()
    {
        var fake = new FakeVpn();
        var ui = new FakeUi { Edited = Route("10.4.5.6/8") };
        var vm = Create(fake, ui);
        await vm.RefreshCommand.ExecuteAsync();
        await vm.AddCommand.ExecuteAsync();
        Assert.Equal(0, fake.Writes);
        Assert.Contains("already exists", Assert.Single(ui.Errors));
    }

    [Fact]
    public async Task DefaultRouteNeedsExplicitConfirmation()
    {
        var fake = new FakeVpn();
        var ui = new FakeUi { Edited = Route("1.2.3.4/0"), ConfirmResult = false };
        var vm = Create(fake, ui);
        await vm.RefreshCommand.ExecuteAsync();
        await vm.AddCommand.ExecuteAsync();
        Assert.Equal(0, fake.Writes);
        Assert.Contains("ALL IPv4", Assert.Single(ui.Confirmations));
    }

    [Fact]
    public async Task AddRefreshesActualState()
    {
        var fake = new FakeVpn();
        var vm = Create(fake, new FakeUi { Edited = Route("192.168.1.8/24") });
        await vm.RefreshCommand.ExecuteAsync();
        await vm.AddCommand.ExecuteAsync();
        Assert.Equal(1, fake.Writes);
        Assert.Equal(2, fake.Reads);
        Assert.Equal(2, vm.Routes.Count);
        Assert.Contains("re-read", vm.Status);
    }

    [Fact]
    public async Task PartialWriteFailureStillRefreshesAndShowsError()
    {
        var fake = new FakeVpn { WriteFailure = new TimeoutException("Unknown result") };
        var ui = new FakeUi { Edited = Route("192.168.1.0/24") };
        var vm = Create(fake, ui);
        await vm.RefreshCommand.ExecuteAsync();
        await vm.AddCommand.ExecuteAsync();
        Assert.Equal(2, fake.Reads);
        Assert.False(vm.IsBusy);
        Assert.Contains("Unknown result", Assert.Single(ui.Errors));
    }

    [Fact]
    public async Task FailedRefreshDisablesStaleEdits()
    {
        var fake = new FakeVpn();
        var vm = Create(fake);
        await vm.RefreshCommand.ExecuteAsync();
        fake.ReadFailure = new PowerShellException("Access denied", "PermissionDenied");
        await vm.RefreshCommand.ExecuteAsync();
        Assert.Null(vm.SelectedProfile);
        Assert.Empty(vm.Routes);
        Assert.False(vm.AddCommand.CanExecute(null));
    }

    [Fact]
    public async Task DeleteCanBeDeclined()
    {
        var fake = new FakeVpn();
        var vm = Create(fake, new FakeUi { ConfirmResult = false });
        await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedRoute = vm.Routes[0];
        await vm.RemoveCommand.ExecuteAsync();
        Assert.Equal(0, fake.Writes);
    }

    [Fact]
    public async Task SplitToggleDoesNotWriteUntilApplyAndCanBeDeclined()
    {
        var fake = new FakeVpn();
        var vm = Create(fake, new FakeUi { ConfirmResult = false });
        await vm.RefreshCommand.ExecuteAsync();
        vm.SplitTunneling = true;
        Assert.Equal(0, fake.Writes);
        Assert.True(vm.ApplySplitCommand.CanExecute(null));
        await vm.ApplySplitCommand.ExecuteAsync();
        Assert.Equal(0, fake.Writes);
        Assert.False(vm.SplitTunneling);
    }

    [Fact]
    public async Task BusyReadDisablesCommandsAndSupportsCancellation()
    {
        var fake = new FakeVpn { BlockRead = true };
        var vm = Create(fake);
        var pending = vm.RefreshCommand.ExecuteAsync();
        Assert.True(vm.IsBusy);
        Assert.False(vm.AddCommand.CanExecute(null));
        Assert.False(vm.RefreshCommand.CanExecute(null));
        vm.CancelCommand.Execute(null);
        await pending;
        Assert.False(vm.IsBusy);
        Assert.Contains("canceled", vm.Status);
    }

    [Fact]
    public async Task MetricEditUsesOriginalRouteAndRefreshes()
    {
        var fake = new FakeVpn();
        var vm = Create(fake, new FakeUi { Edited = Route("10.0.0.0/8") with { Metric = 99 } });
        await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedRoute = vm.Routes[0];
        await vm.EditCommand.ExecuteAsync();
        Assert.Equal(99u, Assert.Single(vm.Routes).Metric);
        Assert.Equal(1u, fake.ExpectedRoute!.Metric);
    }

    [Fact]
    public async Task MissingEditedProfileAfterWriteIsNotReportedAsSuccess()
    {
        var fake = new FakeVpn { HideProfilesAfterWrite = true };
        var ui = new FakeUi { Edited = Route("192.168.0.0/16") };
        var vm = Create(fake, ui);
        await vm.RefreshCommand.ExecuteAsync();
        await vm.AddCommand.ExecuteAsync();
        Assert.Null(vm.SelectedProfile);
        Assert.False(vm.CanEdit);
        Assert.Contains("could not be re-read", Assert.Single(ui.Errors));
    }

    internal static VpnRoute Route(string prefix) => new(Ipv4Prefix.Parse(prefix), 1);
    private static VpnRoutesViewModel Create(FakeVpn fake, FakeUi? ui = null) => new(fake, fake, ui ?? new FakeUi());

    internal sealed class FakeUi : IUserInteraction
    {
        public VpnRoute? Edited { get; init; }
        public bool ConfirmResult { get; init; } = true;
        public List<string> Errors { get; } = [];
        public List<string> Confirmations { get; } = [];
        public VpnRoute? EditRoute(VpnRoute? existing) => Edited;
        public bool Confirm(string title, string message) { Confirmations.Add(message); return ConfirmResult; }
        public void ShowError(string title, string message) => Errors.Add(message);
    }

    internal sealed class FakeVpn : IVpnProfileReader, IVpnProfileWriter
    {
        public List<VpnProfile> Profiles { get; } = [new(new VpnProfileId("Office", VpnScope.User), "Disconnected", "Ikev2", false, [Route("10.0.0.0/8")])];
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public Exception? ReadFailure { get; set; }
        public Exception? WriteFailure { get; init; }
        public bool BlockRead { get; init; }
        public bool HideProfilesAfterWrite { get; init; }
        public VpnRoute? ExpectedRoute { get; private set; }
        public async Task<VpnProfileList> GetProfilesAsync(CancellationToken cancellationToken)
        {
            Reads++;
            if (BlockRead) await Task.Delay(Timeout.Infinite, cancellationToken);
            if (ReadFailure is not null) throw ReadFailure;
            return new VpnProfileList(HideProfilesAfterWrite && Writes > 0 ? [] : Profiles.ToArray(), []);
        }
        public Task<VpnProfile> GetProfileAsync(VpnProfileId id, CancellationToken cancellationToken) => Task.FromResult(Profiles.Single(p => p.Id == id));
        public Task AddRouteAsync(VpnProfileId id, VpnRoute route, CancellationToken cancellationToken) =>
            Write(id, p => p with { Routes = [.. p.Routes, route] });
        public Task RemoveRouteAsync(VpnProfileId id, VpnRoute expectedRoute, CancellationToken cancellationToken) =>
            Write(id, p => p with { Routes = p.Routes.Where(r => r != expectedRoute).ToArray() });
        public Task ChangeMetricAsync(VpnProfileId id, VpnRoute expectedRoute, uint metric, CancellationToken cancellationToken)
        {
            ExpectedRoute = expectedRoute;
            return Write(id, p => p with { Routes = p.Routes.Select(r => r == expectedRoute ? r with { Metric = metric } : r).ToArray() });
        }
        public Task SetSplitTunnelingAsync(VpnProfileId id, bool expectedValue, bool enabled, CancellationToken cancellationToken) =>
            Write(id, p => p with { SplitTunneling = enabled });
        private Task Write(VpnProfileId id, Func<VpnProfile, VpnProfile> change)
        {
            Writes++;
            if (WriteFailure is not null) throw WriteFailure;
            var index = Profiles.FindIndex(p => p.Id == id);
            Profiles[index] = change(Profiles[index]);
            return Task.CompletedTask;
        }
    }
}
