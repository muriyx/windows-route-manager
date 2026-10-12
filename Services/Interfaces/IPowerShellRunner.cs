using System.Text.Json;
using windows_route_manager.Services.PowerShell;

namespace windows_route_manager.Services.Interfaces;

public interface IPowerShellRunner
{
    Task<PowerShellResponse> RunAsync(PowerShellRequest request, CancellationToken cancellationToken);
}

public sealed record PowerShellResponse(JsonElement Data, IReadOnlyList<string> Warnings);
