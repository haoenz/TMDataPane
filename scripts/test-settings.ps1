$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$run = Join-Path $root ('artifacts/settings-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $run | Out-Null
$testProcess = Start-Process -FilePath "$root/build/datapane/collector/DataPane.Collector.exe" -ArgumentList ('--settings-test "{0}"' -f $run) -WindowStyle Hidden -PassThru
$null = $testProcess.Handle
if (!$testProcess.WaitForExit(30000)) { Stop-Process -Id $testProcess.Id -Force; throw 'Settings test timed out' }
if ($testProcess.ExitCode -ne 0) { Get-Content "$run/settings-error.txt" -ErrorAction SilentlyContinue; throw 'Settings test failed' }
Get-Content "$run/settings-results.json"
Write-Host "Settings tests passed: $run"
