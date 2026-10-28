$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_HOME = "$root/artifacts/dotnet-home"
$env:APPDATA = "$root/artifacts/build-appdata"
$env:NUGET_PACKAGES = "$root/artifacts/nuget-packages"
$env:TEMP = "$root/artifacts/temp"
$env:TMP = $env:TEMP
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
& $dotnet restore "$root/tests/Core.Tests/Core.Tests.csproj" --configfile "$root/NuGet.Config" -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Test restore failed' }
& $dotnet run --project "$root/tests/Core.Tests/Core.Tests.csproj" -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed' }
