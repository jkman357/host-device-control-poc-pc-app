$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet restore HostDeviceControl.Poc.sln
    dotnet build HostDeviceControl.Poc.sln -c Release --no-restore
}
finally {
    Pop-Location
}
