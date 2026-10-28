$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_HOME = Join-Path $root 'artifacts/dotnet-home'
$env:NUGET_PACKAGES = Join-Path $root 'artifacts/nuget-packages'
$env:APPDATA = Join-Path $root 'artifacts/build-appdata'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
New-Item -ItemType Directory -Force $env:APPDATA | Out-Null
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (!(Test-Path $dotnet)) { $dotnet = (Get-Command dotnet).Source }
& $dotnet restore "$root/src/Collector.Windows/Collector.Windows.csproj" -r win-x64 --configfile "$root/NuGet.Config" -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed' }
Write-Host 'Dependencies restored to artifacts/nuget-packages.'
