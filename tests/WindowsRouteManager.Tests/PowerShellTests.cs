using System.Text;
using System.Text.Json;
using windows_route_manager.Models;
using windows_route_manager.Services.Interfaces;
using windows_route_manager.Services.PowerShell;
using windows_route_manager.Services.Vpn;

namespace WindowsRouteManager.Tests;

public sealed class PowerShellTests
{
    [Fact]
    public void InputIsJsonDataAndNeverPartOfCommandLine()
    {
        const string name = "Office '; $(throw 'injected') & \"VPN\" / Минск";
        var request = PowerShellRequest.ForProfile(VpnOperation.AddRoute, new VpnProfileId(name, VpnScope.AllUsers));
        using var json = JsonDocument.Parse(PowerShellProtocol.SerializeRequest(request));
        Assert.Equal(name, json.RootElement.GetProperty("Name").GetString());
        Assert.True(json.RootElement.GetProperty("AllUserConnection").GetBoolean());
        var script = PowerShellProtocol.ReadScript();
        var info = PowerShellProtocol.CreateStartInfo(script);
        Assert.EndsWith("WindowsPowerShell\\v1.0\\powershell.exe", info.FileName);
        Assert.Contains("-NoProfile", info.ArgumentList);
        Assert.Contains("-NonInteractive", info.ArgumentList);
        Assert.DoesNotContain("-ExecutionPolicy", info.ArgumentList);
        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
        Assert.Equal(script, Encoding.Unicode.GetString(Convert.FromBase64String(info.ArgumentList.Last())));
        Assert.DoesNotContain(name, string.Join(" ", info.ArgumentList));
        Assert.True(string.Join(" ", info.ArgumentList).Length < 30000);
    }

    [Theory]
    [InlineData("PermissionDenied", 0)]
    [InlineData("NotSpecified", 5)]
    [InlineData("SecurityError", 1314)]
    public void StructuredAccessErrorsHaveActionableGuidance(string category, int code)
    {
        var json = JsonSerializer.Serialize(new { Success = false, Error = new { Message = "Denied", Category = category, ErrorId = "NativeFailure", NativeErrorCode = code } });
        var exception = Assert.Throws<PowerShellException>(() => PowerShellProtocol.ParseResponse(1, json, ""));
        Assert.True(exception.IsAccessDenied);
        Assert.Contains("administrator", exception.UserMessage);
    }

    [Theory]
    [InlineData(0, "not JSON", "")]
    [InlineData(0, "{}", "")]
    [InlineData(0, "null", "")]
    [InlineData(0, "{\"Success\":true}", "")]
    [InlineData(1, "{\"Success\":true,\"Data\":[]}", "")]
    [InlineData(0, "{\"Success\":true,\"Data\":[]}", "Native error stream")]
    public void BrokenOutputCannotLookLikeSuccess(int code, string output, string error) =>
        Assert.Throws<PowerShellException>(() => PowerShellProtocol.ParseResponse(code, output, error));

    [Fact]
    public void EmptyArrayAndWarningsArePreserved()
    {
        var response = PowerShellProtocol.ParseResponse(0, "{\"Success\":true,\"Data\":[],\"Warnings\":[\"All Users unavailable\"]}", "");
        Assert.Empty(response.Data.EnumerateArray());
        Assert.Single(response.Warnings);
    }

    [Fact]
    public void MapperNormalizesIpv4AndPreservesIpv6ByHidingIt()
    {
        using var json = JsonDocument.Parse("""
            {"Name":"Office","AllUserConnection":true,"ConnectionStatus":"Connected","TunnelType":"Ikev2",
             "SplitTunneling":true,"Routes":[{"DestinationPrefix":"10.2.3.4/8","RouteMetric":9},
             {"DestinationPrefix":"2001:db8::/32","RouteMetric":2}]}
            """);
        var profile = VpnProfileMapper.FromJson(json.RootElement);
        Assert.Equal(VpnScope.AllUsers, profile.Id.Scope);
        Assert.True(profile.IsConnected);
        Assert.Equal(1, profile.HiddenIpv6RouteCount);
        Assert.Equal(new VpnRoute(Ipv4Prefix.Parse("10.0.0.0/8"), 9), Assert.Single(profile.Routes));
    }

    [Fact]
    public void InvalidMappingFailsVisibly()
    {
        using var json = JsonDocument.Parse("{}");
        Assert.Throws<PowerShellException>(() => VpnProfileMapper.FromJson(json.RootElement));
    }

    [Fact]
    public async Task WriterKeepsScopeAndExpectedStateInEveryRequest()
    {
        var runner = new FakeRunner();
        var service = new VpnProfileService(runner);
        var id = new VpnProfileId("Same name", VpnScope.AllUsers);
        var route = new VpnRoute(Ipv4Prefix.Parse("10.2.4.8/16"), 42);
        await service.AddRouteAsync(id, route, default);
        await service.RemoveRouteAsync(id, route, default);
        await service.ChangeMetricAsync(id, route, 7, default);
        await service.SetSplitTunnelingAsync(id, false, true, default);
        Assert.All(runner.Requests, request => { Assert.True(request.AllUserConnection); Assert.Equal(id.Name, request.Name); });
        Assert.Equal("10.2.0.0/16", runner.Requests[0].DestinationPrefix);
        Assert.Equal(42u, runner.Requests[1].ExpectedMetric);
        Assert.Equal(7u, runner.Requests[2].Metric);
        Assert.Equal(42u, runner.Requests[2].ExpectedMetric);
        Assert.False(runner.Requests[3].ExpectedSplitTunneling);
        Assert.True(runner.Requests[3].SplitTunneling);
    }

    [Fact]
    public async Task WriterSurfacesWarnings()
    {
        var service = new VpnProfileService(new FakeRunner { Warnings = ["Reconnect required by provider"] });
        await Assert.ThrowsAsync<PowerShellException>(() => service.AddRouteAsync(new VpnProfileId("Test", VpnScope.User),
            new VpnRoute(Ipv4Prefix.Parse("1.2.3.4"), 1), default));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("bad\0name")]
    public void InvalidIdentityNeverReachesRunner(string name) => Assert.Throws<ArgumentException>(() =>
        PowerShellRequest.ForProfile(VpnOperation.AddRoute, new VpnProfileId(name, VpnScope.User)));

    private sealed class FakeRunner : IPowerShellRunner
    {
        public List<PowerShellRequest> Requests { get; } = [];
        public string[] Warnings { get; init; } = [];
        public Task<PowerShellResponse> RunAsync(PowerShellRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new PowerShellResponse(JsonSerializer.SerializeToElement(new { Verified = true }), Warnings));
        }
    }
}
