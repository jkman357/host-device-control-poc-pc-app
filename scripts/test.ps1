$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet run `
        --project tests/HostDeviceControl.Protocol.Tests/HostDeviceControl.Protocol.Tests.csproj `
        -c Release
}
finally {
    Pop-Location
}
