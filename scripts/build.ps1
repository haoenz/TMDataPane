param([ValidateSet('Debug','Release')][string]$Configuration = 'Release', [switch]$SelfContained)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_HOME = Join-Path $root 'artifacts/dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:NUGET_PACKAGES = Join-Path $root 'artifacts/nuget-packages'
$env:APPDATA = Join-Path $root 'artifacts/build-appdata'
New-Item -ItemType Directory -Force $env:APPDATA | Out-Null
$env:TEMP = Join-Path $root 'artifacts/temp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force $env:TEMP | Out-Null
if (!(Get-Command dotnet -ErrorAction SilentlyContinue) -and (Test-Path 'C:\Program Files\dotnet\dotnet.exe')) {
    $env:PATH = 'C:\Program Files\dotnet;' + $env:PATH
}
if (!(Get-Command cmake -ErrorAction SilentlyContinue)) {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
        $bundled = Join-Path $vs 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin'
        if (Test-Path "$bundled/cmake.exe") { $env:PATH = "$bundled;" + $env:PATH }
    }
}
foreach ($tool in @('cmake','dotnet')) {
    if (!(Get-Command $tool -ErrorAction SilentlyContinue)) { throw "Missing $tool. See docs/environment.md" }
}
& "$PSScriptRoot/bootstrap.ps1"
& cmake -S $root -B "$root/build/native" -G 'Visual Studio 17 2022' -A x64
if ($LASTEXITCODE -ne 0) { throw 'CMake configure failed' }
& cmake --build "$root/build/native" --config $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Native build failed' }
& dotnet publish "$root/src/Collector.Windows/Collector.Windows.csproj" -c $Configuration -r win-x64 --self-contained $SelfContained.IsPresent.ToString().ToLowerInvariant() --configfile "$root/NuGet.Config" -p:NuGetAudit=false -o "$root/build/collector"
if ($LASTEXITCODE -ne 0) { throw 'Collector publish failed' }
Write-Host 'Build complete. Run scripts/package.ps1 to assemble the package.'
