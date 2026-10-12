$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$WarningPreference = 'SilentlyContinue'
$script:capturedWarnings = @()
$PSDefaultParameterValues['*:WarningVariable'] = '+script:capturedWarnings'
[Console]::InputEncoding = New-Object System.Text.UTF8Encoding($false)
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)

function Test-BuiltInProfile($vpn) {
    return -not ($vpn.PSObject.TypeNames -match 'ThirdPartyVpnConnection')
}

function Convert-Profile($vpn, [bool]$allUsers) {
    if ($null -eq $vpn.PSObject.Properties['Routes']) {
        throw 'Windows did not expose saved VPN routes. This profile cannot be edited safely.'
    }
    $routes = @($vpn.Routes | ForEach-Object {
        if ($null -ne $_) {
            @{ DestinationPrefix = [string]$_.DestinationPrefix; RouteMetric = [uint32]$_.RouteMetric }
        }
    })
    return @{
        Name = [string]$vpn.Name
        AllUserConnection = $allUsers
        ConnectionStatus = [string]$vpn.ConnectionStatus
        TunnelType = [string]$vpn.TunnelType
        SplitTunneling = [bool]$vpn.SplitTunneling
        Routes = $routes
    }
}

function Get-SelectedProfile($request) {
    $profiles = @(Get-VpnConnection -Name ([string]$request.Name) -AllUserConnection:([bool]$request.AllUserConnection) -ErrorAction Stop)
    if ($profiles.Count -ne 1 -or $profiles[0].Name -ine $request.Name) {
        throw 'The selected VPN profile no longer exists. Refresh the profile list.'
    }
    if (-not (Test-BuiltInProfile $profiles[0])) { throw 'Only built-in Windows VPN profiles are supported.' }
    return $profiles[0]
}

function Get-MatchingRoutes($vpn, [string]$prefix) {
    if ($null -eq $vpn.PSObject.Properties['Routes']) { throw 'Windows did not expose saved VPN routes.' }
    return @($vpn.Routes | Where-Object { $_.DestinationPrefix -eq $prefix })
}

try {
    # Only data is read from stdin. It is never evaluated as PowerShell source.
    $request = [Console]::In.ReadToEnd() | ConvertFrom-Json
    Import-Module (Join-Path $PSHOME 'Modules\VpnClient\VpnClient.psd1') -ErrorAction Stop
    $data = $null
    if ($request.Operation -eq 'ListProfiles') {
        $items = @()
        $successfulScopes = 0
        $scopeFailures = @()
        foreach ($allUsers in @($false, $true)) {
            try {
                $profiles = @(Get-VpnConnection -AllUserConnection:$allUsers -ErrorAction Stop)
                $successfulScopes++
                foreach ($vpn in $profiles) {
                    if (Test-BuiltInProfile $vpn) {
                        $items += (Convert-Profile $vpn $allUsers)
                    } else {
                        $script:capturedWarnings += "Skipped third-party VPN profile: $($vpn.Name)."
                    }
                }
            } catch {
                $scopeFailures += $_
                $scope = if ($allUsers) { 'All Users' } else { 'User' }
                $script:capturedWarnings += "Could not read $scope profiles: $($_.Exception.Message)"
            }
        }
        if ($successfulScopes -eq 0) { throw $scopeFailures[0] }
        $data = @($items)
    } else {
        if ([string]::IsNullOrWhiteSpace($request.Name)) { throw 'A VPN profile name is required.' }
        $vpn = Get-SelectedProfile $request
        if ($request.Operation -eq 'GetProfile') {
            $data = Convert-Profile $vpn ([bool]$request.AllUserConnection)
        } elseif ($request.Operation -eq 'SetSplitTunneling') {
            if ($null -eq $request.SplitTunneling -or $null -eq $request.ExpectedSplitTunneling) { throw 'Split tunneling values are required.' }
            if ([bool]$vpn.SplitTunneling -ne [bool]$request.ExpectedSplitTunneling) {
                throw 'Split tunneling changed outside this application. Refresh before retrying.'
            }
            Set-VpnConnection -Name ([string]$request.Name) -AllUserConnection:([bool]$request.AllUserConnection) `
                -SplitTunneling ([bool]$request.SplitTunneling) -Force -Confirm:$false -ErrorAction Stop | Out-Null
            $actual = Get-SelectedProfile $request
            if ([bool]$actual.SplitTunneling -ne [bool]$request.SplitTunneling) { throw 'Windows did not retain the requested split tunneling setting.' }
            $data = @{ Verified = $true }
        } elseif ($request.Operation -in @('AddRoute', 'RemoveRoute', 'ChangeMetric')) {
            $prefix = [string]$request.DestinationPrefix
            if ([string]::IsNullOrWhiteSpace($prefix)) { throw 'A destination prefix is required.' }
            $routeParameters = @{
                ConnectionName = [string]$request.Name
                AllUserConnection = [bool]$request.AllUserConnection
                DestinationPrefix = $prefix
                ErrorAction = 'Stop'
                Confirm = $false
            }
            $matches = @(Get-MatchingRoutes $vpn $prefix)
            if ($request.Operation -eq 'AddRoute') {
                if ($null -eq $request.Metric) { throw 'A route metric is required.' }
                if ($matches.Count -ne 0) { throw 'This destination already exists in the VPN profile. Refresh before editing it.' }
                Add-VpnConnectionRoute @routeParameters -RouteMetric ([uint32]$request.Metric) | Out-Null
            } else {
                if ($null -eq $request.ExpectedMetric -or $matches.Count -ne 1 -or [uint32]$matches[0].RouteMetric -ne [uint32]$request.ExpectedMetric) {
                    throw 'The saved route changed or no longer exists. Refresh before retrying.'
                }
                if ($request.Operation -eq 'ChangeMetric' -and $null -eq $request.Metric) { throw 'A route metric is required.' }
                Remove-VpnConnectionRoute @routeParameters | Out-Null
                if ($request.Operation -eq 'ChangeMetric') {
                    try {
                        Add-VpnConnectionRoute @routeParameters -RouteMetric ([uint32]$request.Metric) | Out-Null
                    } catch {
                        $changeFailure = $_.Exception.Message
                        # Replacement is not atomic. Restore only if no route now occupies this prefix.
                        try {
                            $current = @(Get-MatchingRoutes (Get-SelectedProfile $request) $prefix)
                            if ($current.Count -ne 0) { throw 'The prefix is already occupied; it was not overwritten.' }
                            Add-VpnConnectionRoute @routeParameters -RouteMetric ([uint32]$request.ExpectedMetric) | Out-Null
                            $restored = @(Get-MatchingRoutes (Get-SelectedProfile $request) $prefix)
                            if ($restored.Count -ne 1 -or [uint32]$restored[0].RouteMetric -ne [uint32]$request.ExpectedMetric) {
                                throw 'Windows did not confirm the restored metric.'
                            }
                        } catch {
                            throw "Metric update failed: $changeFailure Recovery also failed: $($_.Exception.Message) The route may be missing or changed. Refresh and inspect it."
                        }
                        throw "Metric update failed: $changeFailure The original metric was restored."
                    }
                }
            }
            $actual = @(Get-MatchingRoutes (Get-SelectedProfile $request) $prefix)
            if ($request.Operation -eq 'RemoveRoute') {
                if ($actual.Count -ne 0) { throw 'Windows still reports the route after removal. Refresh before retrying.' }
            } elseif ($actual.Count -ne 1 -or [uint32]$actual[0].RouteMetric -ne [uint32]$request.Metric) {
                throw 'Windows did not confirm the requested route metric. Refresh before retrying.'
            }
            $data = @{ Verified = $true }
        } else {
            throw 'Unsupported VPN operation.'
        }
    }
    $warnings = @($script:capturedWarnings | ForEach-Object { [string]$_ })
    [Console]::Out.Write((@{ Success = $true; Data = $data; Warnings = $warnings } | ConvertTo-Json -Depth 8 -Compress))
    exit 0
} catch {
    $nativeCode = $null
    if ($null -ne $_.Exception.PSObject.Properties['NativeErrorCode']) { $nativeCode = [int]$_.Exception.NativeErrorCode }
    $errorData = @{
        Message = $_.Exception.Message
        Category = [string]$_.CategoryInfo.Category
        ErrorId = [string]$_.FullyQualifiedErrorId
        NativeErrorCode = $nativeCode
    }
    if ($request.Operation -eq 'ListProfiles' -and $script:capturedWarnings.Count -gt 0) {
        $errorData.Message = $script:capturedWarnings -join "`n"
    }
    [Console]::Out.Write((@{ Success = $false; Error = $errorData } | ConvertTo-Json -Depth 5 -Compress))
    exit 1
}
