# Windows Route Manager

**Windows IP & VPN Route Manager**

A small, open-source Windows desktop utility for editing saved routes in built-in Windows VPN profiles.

The app uses native Windows VPN routing, so your routes work even when the app is closed.

> [!NOTE]
> Third-party VPN applications such as OpenVPN or WireGuard are outside this version's scope. Managed profiles can be protected by organizational policy.

## Requirements

- Windows 10+ (x64).

## Reliability and limitations

- Only IPv4 routes are currently supported for editing.
- Existing IPv6 routes are counted but hidden and preserved.
- Changing a metric removes the exact expected route and adds it again. If adding fails, the app attempts to restore the old metric only if the prefix is still vacant, then verifies recovery. This sequence is **not atomic**; process termination, timeouts or concurrent external edits can leave a partial result. Refresh and inspect the error before retrying.
- Before mutation, the script checks the saved metric or split setting against the last read value. This detects stale data but cannot lock out other editors.


## Contributing and license

Contributions are welcome. Please do not attach VPN credentials or confidential profile details to issues.

Licensed under [MIT](LICENSE).
