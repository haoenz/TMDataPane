$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$run = Join-Path $root ('artifacts/install-' + [guid]::NewGuid().ToString('N'))
$tm = Join-Path $run ('TrafficMonitor-' + ('x' * 45))
$package = Join-Path $run 'package'
$old = Join-Path $tm 'plugins/UsageBridge'
New-Item -ItemType Directory -Force "$old/licenses", "$old/credentials", "$old/profile", "$package/plugins/UsageBridge", "$package/licenses" | Out-Null
# Minimal PE header for the installer's architecture check; never executed.
$pe = New-Object byte[] 128
$pe[60] = 64
$pe[68] = 0x64
$pe[69] = 0x86
[IO.File]::WriteAllBytes("$tm/TrafficMonitor.exe", $pe)
[IO.File]::WriteAllText("$tm/plugins/UsageBridge.dll", 'old native')
[IO.File]::WriteAllText("$old/UsageBridge.Collector.exe", 'old collector')
[IO.File]::WriteAllText("$old/config.json", '{"enabled":false}')
[IO.File]::WriteAllText("$old/credentials/test.dat", 'test credential fixture')
[IO.File]::WriteAllText("$old/profile/session.txt", 'test session fixture')
$notice = 'microsoft.netcore.app.runtime.win-x64-THIRD-PARTY-NOTICES.TXT'
[IO.File]::WriteAllText("$old/licenses/$notice", 'old license')
[IO.File]::WriteAllText("$package/plugins/UsageBridge.dll", 'new native')
[IO.File]::WriteAllText("$package/plugins/UsageBridge/UsageBridge.Collector.exe", 'new collector')
[IO.File]::WriteAllText("$package/licenses/$notice", 'new license')
function Assert-File([string]$Path, [string]$Expected) {
    $extended = '\\?\' + [IO.Path]::GetFullPath($Path)
    if ([IO.File]::ReadAllText($extended) -cne $Expected) { throw "Unexpected contents: $Path" }
}
& "$PSScriptRoot/install.ps1" -TrafficMonitorDirectory $tm -PackageDirectory $package
$backup = (Get-ChildItem -LiteralPath $tm -Directory -Filter 'UsageBridge-backup-*' | Select-Object -First 1).FullName
$longPath = "$backup/UsageBridge/licenses/$notice"
if ($longPath.Length -le 260) { throw 'Fixture must exercise a path longer than 260 characters' }
Assert-File $longPath 'old license'
Assert-File "$backup/UsageBridge.dll" 'old native'
Assert-File "$backup/UsageBridge/config.json" '{"enabled":false}'
# Repeat to check a completed installation can also be upgraded.
& "$PSScriptRoot/install.ps1" -TrafficMonitorDirectory $tm -PackageDirectory $package
if (@(Get-ChildItem -LiteralPath $tm -Directory -Filter 'UsageBridge-backup-*').Count -ne 2) { throw 'Expected unique backups' }
Assert-File "$tm/plugins/UsageBridge.dll" 'new native'
Assert-File "$old/UsageBridge.Collector.exe" 'new collector'
Assert-File "$old/config.json" '{"enabled":false}'
Assert-File "$old/credentials/test.dat" 'test credential fixture'
Assert-File "$old/profile/session.txt" 'test session fixture'
Assert-File "$old/licenses/$notice" 'new license'
if (Test-Path -LiteralPath "$old/licenses/licenses") { throw 'Unexpected nested licenses directory' }
Write-Host "Installer tests passed (backup path length $($longPath.Length)): $run"
