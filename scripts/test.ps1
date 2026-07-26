# Copyright © 2026 Ray Yang. All rights reserved.
# No license is granted. See LICENSE and NOTICE.md.

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    python tools/validate_project.py
    dotnet restore HostDeviceControl.Poc.sln
    dotnet build HostDeviceControl.Poc.sln --configuration Release --no-restore
    dotnet run --project tests/HostDeviceControl.Protocol.Tests/HostDeviceControl.Protocol.Tests.csproj --configuration Release --no-build
    dotnet run --project tests/HostDeviceControl.Transport.Serial.Tests/HostDeviceControl.Transport.Serial.Tests.csproj --configuration Release --no-build
}
finally {
    Pop-Location
}
