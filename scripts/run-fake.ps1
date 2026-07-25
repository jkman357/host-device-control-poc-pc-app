$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet run --project src/HostDeviceControl.App/HostDeviceControl.App.csproj
}
finally {
    Pop-Location
}
