$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$run = Join-Path $root ('artifacts/install-' + [guid]::NewGuid().ToString('N'))
$tm = Join-Path $run ('TrafficMonitor-' + ('x' * 55))
$package = Join-Path $run 'package'
# Only the isolated fixture below is installed. Its fake PE is never executed;
# unrelated desktop TrafficMonitor processes must not block this fixture test.
function Get-Process {
    [CmdletBinding()] param([string]$Name)
    if ($Name -ne 'TrafficMonitor') { throw 'Unexpected process query in installer fixture' }
}
$old = Join-Path $tm 'plugins/DataPane'
New-Item -ItemType Directory -Force "$old/licenses", "$old/credentials", "$old/profile", "$package/plugins/DataPane", "$package/licenses" | Out-Null
# Minimal PE header for the installer's architecture check; never executed.
$pe = New-Object byte[] 128
$pe[60] = 64
$pe[68] = 0x64
$pe[69] = 0x86
[IO.File]::WriteAllBytes("$tm/TrafficMonitor.exe", $pe)
[IO.File]::WriteAllText("$tm/plugins/DataPane.dll", 'old native')
[IO.File]::WriteAllText("$old/DataPane.Collector.exe", 'old collector')
[IO.File]::WriteAllText("$old/config.json", '{"enabled":false}')
[IO.File]::WriteAllText("$old/credentials/test.dat", 'test credential fixture')
[IO.File]::WriteAllText("$old/profile/session.txt", 'test session fixture')
$notice = 'microsoft.netcore.app.runtime.win-x64-THIRD-PARTY-NOTICES.TXT'
[IO.File]::WriteAllText("$old/licenses/$notice", 'old license')
[IO.File]::WriteAllText("$package/plugins/DataPane.dll", 'new native')
[IO.File]::WriteAllText("$package/plugins/DataPane/DataPane.Collector.exe", 'new collector')
[IO.File]::WriteAllText("$package/licenses/$notice", 'new license')
function Assert-File([string]$Path, [string]$Expected) {
    $extended = '\\?\' + [IO.Path]::GetFullPath($Path)
    if ([IO.File]::ReadAllText($extended) -cne $Expected) { throw "Unexpected contents: $Path" }
}
& "$PSScriptRoot/install.ps1" -TrafficMonitorDirectory $tm -PackageDirectory $package
$backup = (Get-ChildItem -LiteralPath $tm -Directory -Filter 'DataPane-backup-*' | Select-Object -First 1).FullName
$longPath = "$backup/DataPane/licenses/$notice"
if ($longPath.Length -le 260) { throw 'Fixture must exercise a path longer than 260 characters' }
Assert-File $longPath 'old license'
Assert-File "$backup/DataPane.dll" 'old native'
Assert-File "$backup/DataPane/config.json" '{"enabled":false}'
# Repeat to check a completed installation can also be upgraded.
& "$PSScriptRoot/install.ps1" -TrafficMonitorDirectory $tm -PackageDirectory $package
if (@(Get-ChildItem -LiteralPath $tm -Directory -Filter 'DataPane-backup-*').Count -ne 2) { throw 'Expected unique backups' }
Assert-File "$tm/plugins/DataPane.dll" 'new native'
Assert-File "$old/DataPane.Collector.exe" 'new collector'
Assert-File "$old/config.json" '{"enabled":false}'
Assert-File "$old/credentials/test.dat" 'test credential fixture'
Assert-File "$old/profile/session.txt" 'test session fixture'
Assert-File "$old/licenses/$notice" 'new license'
if (Test-Path -LiteralPath "$old/licenses/licenses") { throw 'Unexpected nested licenses directory' }
Write-Host "Installer tests passed (backup path length $($longPath.Length)): $run"
