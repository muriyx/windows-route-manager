using windows_route_manager.Services.PowerShell;

namespace WindowsRouteManager.Tests;

// All subprocesses here execute inert test scripts. No VpnClient commands or imports.
public sealed class PowerShellRunnerTests
{
    // Allow cold PowerShell startup on shared CI runners in tests that do not verify deadlines.
    private static readonly TimeSpan ScriptCompletionTimeout = TimeSpan.FromSeconds(45);

    [Fact]
    public async Task DeadlineTerminatesAnUnresponsiveProcess()
    {
        var runner = new PowerShellRunner(TimeSpan.FromMilliseconds(400), "[Threading.Thread]::Sleep(30000)");
        var error = await Assert.ThrowsAsync<TimeoutException>(() => runner.RunAsync(new PowerShellRequest(VpnOperation.ListProfiles), default));
        Assert.Contains("timed out", error.Message);
    }

    [Fact]
    public async Task CancellationTerminatesAnUnresponsiveProcess()
    {
        var runner = new PowerShellRunner(TimeSpan.FromSeconds(20), "[Threading.Thread]::Sleep(30000)");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(new PowerShellRequest(VpnOperation.ListProfiles), cancellation.Token));
    }

    [Fact]
    public async Task RunnerSendsUnicodeJsonAndClosesStdin()
    {
        const string script = """
            $ProgressPreference = 'SilentlyContinue'
            [Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
            [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
            $request = [Console]::In.ReadToEnd() | ConvertFrom-Json
            [Console]::Out.Write((@{ Success=$true; Data=@{Name=$request.Name}; Warnings=@() } | ConvertTo-Json -Compress))
            """;
        var runner = new PowerShellRunner(ScriptCompletionTimeout, script);
        const string name = "Тест ' ; $(throw)";
        var result = await runner.RunAsync(new PowerShellRequest(VpnOperation.GetProfile, name), default);
        Assert.Equal(name, result.Data.GetProperty("Name").GetString());
    }

    [Fact]
    public async Task StderrFailureIsNotHiddenByZeroExitCode()
    {
        const string script = """
            [Console]::Error.Write('Inert test failure')
            [Console]::Out.Write('{"Success":true,"Data":[],"Warnings":[]}')
            """;
        var runner = new PowerShellRunner(ScriptCompletionTimeout, script);
        var error = await Assert.ThrowsAsync<PowerShellException>(() => runner.RunAsync(new PowerShellRequest(VpnOperation.ListProfiles), default));
        Assert.Contains("Inert test failure", error.Message);
    }
}
