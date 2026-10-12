using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using windows_route_manager.Services.Interfaces;

namespace windows_route_manager.Services.PowerShell;

public sealed class PowerShellRunner(TimeSpan? timeout = null) : IPowerShellRunner
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(45);
    private readonly string _script = PowerShellProtocol.ReadScript();

    internal PowerShellRunner(TimeSpan timeout, string testScript) : this(timeout) => _script = testScript;

    public Task<PowerShellResponse> RunAsync(PowerShellRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => RunProcessAsync(request, cancellationToken), cancellationToken);

    private async Task<PowerShellResponse> RunProcessAsync(PowerShellRequest request, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        using var process = new Process { StartInfo = PowerShellProtocol.CreateStartInfo(_script) };
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            process.Start();
        }
        catch (Win32Exception exception)
        {
            throw new PowerShellException("Windows PowerShell 5.1 could not start. Check that Windows PowerShell and the VpnClient module are available.",
                nativeErrorCode: exception.NativeErrorCode, inner: exception);
        }

        // Drain both streams concurrently so a full stderr pipe cannot deadlock the process.
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.StandardInput.WriteAsync(PowerShellProtocol.SerializeRequest(request).AsMemory(), deadline.Token).ConfigureAwait(false);
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            return PowerShellProtocol.ParseResponse(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Windows PowerShell timed out. A write may already have completed; refresh the profile before retrying.");
        }
        catch (IOException exception)
        {
            throw new PowerShellException("Communication with Windows PowerShell failed. Refresh before retrying any write.", inner: exception);
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { /* The process exited between the check and Kill. */ }
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
            await Task.WhenAll(output, error).ConfigureAwait(false);
        }
    }
}
