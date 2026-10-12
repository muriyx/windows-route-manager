using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using windows_route_manager.Services.Interfaces;

namespace windows_route_manager.Services.PowerShell;

public static class PowerShellProtocol
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true
    };

    public static string SerializeRequest(PowerShellRequest request) => JsonSerializer.Serialize(request, Options);

    public static string ReadScript()
    {
        using var stream = typeof(PowerShellProtocol).Assembly.GetManifestResourceStream(
            "windows_route_manager.Services.PowerShell.VpnOperations.ps1")
            ?? throw new InvalidOperationException("The embedded VPN script is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static ProcessStartInfo CreateStartInfo(string script)
    {
        var info = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe"),
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-OutputFormat", "Text", "-EncodedCommand",
                     Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
            info.ArgumentList.Add(argument);
        return info;
    }

    public static PowerShellResponse ParseResponse(int exitCode, string stdout, string stderr)
    {
        Envelope? envelope;
        try { envelope = JsonSerializer.Deserialize<Envelope>(stdout, Options); }
        catch (JsonException exception)
        {
            throw new PowerShellException($"Windows PowerShell returned invalid output (exit {exitCode}). " +
                $"{Limit(stderr)} {Limit(stdout)}", inner: exception);
        }

        if (envelope is null || envelope.Success is null)
            throw new PowerShellException("Windows PowerShell returned an incomplete response.");
        if (!envelope.Success.Value)
        {
            var error = envelope.Error;
            throw new PowerShellException(error?.Message ?? $"Windows PowerShell failed (exit {exitCode}). {Limit(stderr)}",
                error?.Category, error?.ErrorId, error?.NativeErrorCode);
        }
        if (exitCode != 0 || !string.IsNullOrWhiteSpace(stderr))
            throw new PowerShellException($"Windows PowerShell failed (exit {exitCode}). {Limit(stderr)}");
        if (envelope.Data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new PowerShellException("Windows PowerShell returned no result data.");
        return new PowerShellResponse(envelope.Data, envelope.Warnings ?? []);
    }

    private static string Limit(string text) => text.Length > 4000 ? text[..4000] + "…" : text;

    private sealed record Envelope(bool? Success, JsonElement Data, ErrorData? Error, string[]? Warnings);
    private sealed record ErrorData(string? Message, string? Category, string? ErrorId, int? NativeErrorCode);
}
