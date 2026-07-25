# Copyright © 2026 Ray Yang. All rights reserved.
# No license is granted. See LICENSE and NOTICE.md.

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet run --project src/HostDeviceControl.App/HostDeviceControl.App.csproj
}
finally {
    Pop-Location
}
