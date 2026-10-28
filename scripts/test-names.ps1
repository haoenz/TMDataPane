$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$run = Join-Path $root ('artifacts/names-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
& "$root/build/datapane/native/Release/LifecycleHost.exe" "$root/build/datapane/native/Release/DataPane.dll" $run 1 names
if ($LASTEXITCODE -ne 0) { throw "Name host failed: $LASTEXITCODE" }
Write-Host "Name tests passed: $run"
