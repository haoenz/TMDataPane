param([string]$TrafficMonitorDirectory = 'C:\Users\zhanghaoen\AppData\Local\Microsoft\WinGet\Packages\zhongyang219.TrafficMonitor.Full_Microsoft.Winget.Source_8wekyb3d8bbwe\TrafficMonitor', [string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
function Copy-DirectoryTree([string]$Source, [string]$Destination) {
    # Robocopy supports long WinGet/profile paths. Never mirror: preserve user data.
    & robocopy.exe $Source $Destination /E /COPY:DAT /DCOPY:DAT /IS /IT /XJ /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Host
    $copyExitCode = $LASTEXITCODE
    if ($copyExitCode -ge 8) { throw "Copy failed (robocopy exit $copyExitCode): $Source -> $Destination" }
}
if (!$PackageDirectory) {
    if (Test-Path "$PSScriptRoot/plugins/UsageBridge.dll") { $PackageDirectory = $PSScriptRoot }
    else { $PackageDirectory = (Get-Content "$(Split-Path $PSScriptRoot -Parent)/dist/latest-package.txt" -Raw).Trim() }
}
$TrafficMonitorDirectory = [IO.Path]::GetFullPath($TrafficMonitorDirectory)
$exe = Join-Path $TrafficMonitorDirectory 'TrafficMonitor.exe'
if (!(Test-Path -LiteralPath $exe)) { throw 'TrafficMonitor.exe not found' }
$bytes = [IO.File]::ReadAllBytes($exe)
$offset = [BitConverter]::ToInt32($bytes, 60)
if ([BitConverter]::ToUInt16($bytes, $offset+4) -ne 0x8664) { throw 'This package requires x64 TrafficMonitor' }
if (@(Get-Process TrafficMonitor -ErrorAction SilentlyContinue).Count) { throw 'Please exit TrafficMonitor before installing. The script will not stop it.' }
if (!(Test-Path "$PackageDirectory/plugins/UsageBridge.dll")) { throw 'Package DLL not found' }
if (!(Test-Path "$PackageDirectory/plugins/UsageBridge/UsageBridge.Collector.exe")) { throw 'Package collector not found' }
if (!(Test-Path "$PackageDirectory/licenses")) { throw 'Package licenses not found' }
$plugins = Join-Path $TrafficMonitorDirectory 'plugins'
$backup = Join-Path $TrafficMonitorDirectory ('UsageBridge-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8))
foreach ($name in @('UsageBridge.dll','UsageBridge')) {
    $existing = Join-Path $plugins $name
    if (Test-Path -LiteralPath $existing) {
        New-Item -ItemType Directory -Force $backup | Out-Null
        if (Test-Path -LiteralPath $existing -PathType Container) {
            Copy-DirectoryTree $existing (Join-Path $backup $name)
        } else {
            Copy-Item -LiteralPath $existing -Destination $backup
        }
    }
}
New-Item -ItemType Directory -Force $plugins | Out-Null
Copy-Item "$PackageDirectory/plugins/UsageBridge.dll" $plugins -Force
Copy-DirectoryTree "$PackageDirectory/plugins/UsageBridge" "$plugins/UsageBridge"
Copy-DirectoryTree "$PackageDirectory/licenses" "$plugins/UsageBridge/licenses"
if (Test-Path -LiteralPath $backup) { Write-Host "Backup: $backup" }
Write-Host 'Installed DataPane. Start TrafficMonitor and open DataPane options. Restart TrafficMonitor after adding or removing display items.'
