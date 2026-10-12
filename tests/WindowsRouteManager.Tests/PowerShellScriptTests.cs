using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using windows_route_manager.Models;
using windows_route_manager.Services.Interfaces;
using windows_route_manager.Services.PowerShell;

namespace WindowsRouteManager.Tests;

// The production script runs ONLY after its VpnClient import is removed and all four
// VPN commands are replaced with in-memory functions. Module autoloading is disabled.
public sealed class PowerShellScriptTests
{
    [Theory]
    [InlineData(VpnOperation.AddRoute, "192.168.1.0/24", 20u, null)]
    [InlineData(VpnOperation.RemoveRoute, "10.0.0.0/8", null, 5u)]
    [InlineData(VpnOperation.ChangeMetric, "10.0.0.0/8", 20u, 5u)]
    public async Task MutationsVerifyFakeState(VpnOperation operation, string prefix, uint? metric, uint? expected)
    {
        var result = await RunFakeAsync(new PowerShellRequest(operation, "VPN ' $(throw) Минск", true, prefix, metric, expected));
        Assert.True(result.Data.GetProperty("Verified").GetBoolean());
    }

    [Fact]
    public async Task SplitSettingIsSavedAndVerified()
    {
        var result = await RunFakeAsync(new PowerShellRequest(VpnOperation.SetSplitTunneling, "Office", SplitTunneling: true, ExpectedSplitTunneling: false));
        Assert.True(result.Data.GetProperty("Verified").GetBoolean());
    }

    [Fact]
    public async Task BothScopesAndRouteArraysSurviveSerialization()
    {
        var result = await RunFakeAsync(new PowerShellRequest(VpnOperation.ListProfiles));
        var profiles = result.Data.EnumerateArray().ToArray();
        Assert.Equal(2, profiles.Length);
        Assert.False(profiles[0].GetProperty("AllUserConnection").GetBoolean());
        Assert.True(profiles[1].GetProperty("AllUserConnection").GetBoolean());
        Assert.Single(profiles[0].GetProperty("Routes").EnumerateArray());
    }

    [Fact]
    public async Task MissingScopeDoesNotHideOtherScope()
    {
        var result = await RunFakeAsync(new PowerShellRequest(VpnOperation.ListProfiles), "AllUsersDenied");
        Assert.Single(result.Data.EnumerateArray());
        Assert.Contains("All Users", Assert.Single(result.Warnings));
    }

    [Fact]
    public async Task NoProfilesIsAnEmptyArray()
    {
        var result = await RunFakeAsync(new PowerShellRequest(VpnOperation.ListProfiles), "Empty");
        Assert.Empty(result.Data.EnumerateArray());
    }

    [Fact]
    public async Task WarningsAreCapturedWithoutCorruptingJson()
    {
        var result = await RunFakeAsync(new PowerShellRequest(VpnOperation.GetProfile, "Office"), "Warning");
        Assert.Contains("Fake provider warning", result.Warnings);
    }

    [Fact]
    public async Task BothScopeFailuresPreserveAccessDeniedCategory()
    {
        var error = await Assert.ThrowsAsync<PowerShellException>(() => RunFakeAsync(new PowerShellRequest(VpnOperation.ListProfiles), "Denied"));
        Assert.True(error.IsAccessDenied);
        Assert.Contains("All Users", error.Message);
        Assert.Contains("User", error.Message);
    }

    [Theory]
    [InlineData(VpnOperation.AddRoute, 5u, null, "Normal", "already exists")]
    [InlineData(VpnOperation.RemoveRoute, null, 8u, "Normal", "changed")]
    [InlineData(VpnOperation.ChangeMetric, 99u, 5u, "Normal", "original metric was restored")]
    [InlineData(VpnOperation.ChangeMetric, 99u, 5u, "RecoveryFails", "Recovery also failed")]
    [InlineData(VpnOperation.ChangeMetric, 20u, 5u, "IgnoreWrite", "did not confirm")]
    public async Task ConflictsAndPartialFailuresAreVisible(VpnOperation operation, uint? metric, uint? expected, string scenario, string message)
    {
        var exception = await Assert.ThrowsAsync<PowerShellException>(() => RunFakeAsync(
            new PowerShellRequest(operation, "Office", false, "10.0.0.0/8", metric, expected), scenario));
        Assert.Contains(message, exception.Message);
    }

    [Fact]
    public async Task ReadReturnsExactNameIncludingUnicodeAndMetacharacters()
    {
        const string name = "Office ' ; $(throw) Минск";
        var result = await RunFakeAsync(PowerShellRequest.ForProfile(VpnOperation.GetProfile, new VpnProfileId(name, VpnScope.User)));
        Assert.Equal(name, result.Data.GetProperty("Name").GetString());
    }

    private static async Task<PowerShellResponse> RunFakeAsync(PowerShellRequest request, string scenario = "Normal")
    {
        const string import = "Import-Module (Join-Path $PSHOME 'Modules\\VpnClient\\VpnClient.psd1') -ErrorAction Stop";
        var production = PowerShellProtocol.ReadScript();
        Assert.Contains(import, production);
        production = production.Replace(import, "# VpnClient is deliberately unavailable in this test.", StringComparison.Ordinal);
        Assert.DoesNotContain("Import-Module", production);
        var script = FakeCommands + "\n" + production;
        // Keep the command line short; the first stdin line contains trusted test source,
        // and the remaining stdin contains the normal JSON request used by production.
        const string bootstrap = "$ProgressPreference = 'SilentlyContinue'; [Console]::InputEncoding = [Text.UTF8Encoding]::new($false); " +
            "$source = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String([Console]::In.ReadLine())); & ([scriptblock]::Create($source))";
        using var process = new Process { StartInfo = PowerShellProtocol.CreateStartInfo(bootstrap) };
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            var json = JsonNode.Parse(PowerShellProtocol.SerializeRequest(request))!;
            json["Scenario"] = scenario;
            await process.StandardInput.WriteLineAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(script)));
            await process.StandardInput.WriteAsync(json.ToJsonString());
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            return PowerShellProtocol.ParseResponse(process.ExitCode, await output, await error);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
            await Task.WhenAll(output, error);
        }
    }

    private const string FakeCommands = """
        $ProgressPreference = 'SilentlyContinue'
        Import-Module Microsoft.PowerShell.Utility
        Import-Module Microsoft.PowerShell.Management
        $PSModuleAutoLoadingPreference = 'None'
        $script:routes = @([pscustomobject]@{DestinationPrefix='10.0.0.0/8'; RouteMetric=[uint32]5})
        $script:split = $false
        function Get-VpnConnection {
            [CmdletBinding()] param([string[]]$Name, [switch]$AllUserConnection)
            if ($request.Scenario -eq 'Empty') { return }
            if ($request.Scenario -eq 'Denied') { Write-Error 'Fake access denied' -Category PermissionDenied -ErrorId AccessDenied -ErrorAction Stop }
            if ($request.Scenario -eq 'Warning') { Write-Warning 'Fake provider warning' }
            if ($request.Scenario -eq 'AllUsersDenied' -and $AllUserConnection) { throw 'Fake access denied' }
            $n = if ($Name) { $Name[0] } else { 'Office' }
            [pscustomobject]@{Name=$n; AllUserConnection=[bool]$AllUserConnection; Routes=$script:routes;
                SplitTunneling=$script:split; ConnectionStatus='Disconnected'; TunnelType='Ikev2'}
        }
        function Assert-FakeTarget($name, $scope) {
            if ($name -cne $request.Name -or [bool]$scope -ne [bool]$request.AllUserConnection) { throw 'Wrong fake target' }
        }
        function Add-VpnConnectionRoute {
            [CmdletBinding(SupportsShouldProcess)] param([string]$ConnectionName, [switch]$AllUserConnection, [string]$DestinationPrefix, [uint32]$RouteMetric)
            Assert-FakeTarget $ConnectionName $AllUserConnection
            if ($RouteMetric -eq 99 -or $request.Scenario -eq 'RecoveryFails') { throw 'Fake write failure' }
            if ($request.Scenario -eq 'IgnoreWrite') { return }
            $script:routes += [pscustomobject]@{DestinationPrefix=$DestinationPrefix; RouteMetric=$RouteMetric}
        }
        function Remove-VpnConnectionRoute {
            [CmdletBinding(SupportsShouldProcess)] param([string]$ConnectionName, [switch]$AllUserConnection, [string]$DestinationPrefix)
            Assert-FakeTarget $ConnectionName $AllUserConnection
            $script:routes = @($script:routes | Where-Object { $_.DestinationPrefix -ne $DestinationPrefix })
        }
        function Set-VpnConnection {
            [CmdletBinding(SupportsShouldProcess)] param([string]$Name, [switch]$AllUserConnection, [bool]$SplitTunneling, [switch]$Force)
            Assert-FakeTarget $Name $AllUserConnection
            $script:split = $SplitTunneling
        }
        foreach ($command in @('Get-VpnConnection','Add-VpnConnectionRoute','Remove-VpnConnectionRoute','Set-VpnConnection')) {
            if ((Get-Command $command).CommandType -ne 'Function') { throw 'Fake command is missing' }
        }
        """;
}
