# Copyright © 2026 Ray Yang. All rights reserved.
# No license is granted. See LICENSE and NOTICE.md.

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    python tools/validate_project.py
    dotnet restore HostDeviceControl.Poc.sln
    dotnet format HostDeviceControl.Poc.sln --verify-no-changes --no-restore
    dotnet build HostDeviceControl.Poc.sln --configuration Release --no-restore
}
finally {
    Pop-Location
}
