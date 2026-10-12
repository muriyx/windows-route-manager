namespace windows_route_manager.Services.PowerShell;

public sealed class PowerShellException : Exception
{
    public PowerShellException(string message, string? category = null, string? errorId = null,
        int? nativeErrorCode = null, Exception? inner = null) : base(message, inner)
    {
        Category = category;
        ErrorId = errorId;
        NativeErrorCode = nativeErrorCode;
    }

    public string? Category { get; }
    public string? ErrorId { get; }
    public int? NativeErrorCode { get; }
    public bool IsAccessDenied => Category is "PermissionDenied" or "SecurityError" ||
        NativeErrorCode is 5 or 1314 || ErrorId?.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase) == true;

    public string UserMessage => IsAccessDenied
        ? "Windows denied access. For an All Users profile, use 'Open as administrator' and retry explicitly. " +
          "Elevation under another account shows that account's User profiles.\n\n" + Message
        : Message;
}
