$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$run = Join-Path $root ('artifacts/web-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $run | Out-Null
foreach ($pass in 1..2) {
    $testProcess = Start-Process -FilePath "$root/build/datapane/collector/DataPane.Collector.exe" -ArgumentList ('--web-test "{0}"' -f $run) -WindowStyle Hidden -PassThru
    $null = $testProcess.Handle
    if (!$testProcess.WaitForExit(60000)) { Stop-Process -Id $testProcess.Id -Force; throw 'Web test timed out' }
    if ($testProcess.ExitCode -ne 0) { Get-Content "$run/web-error.txt" -ErrorAction SilentlyContinue; throw 'Web test failed' }
    Get-Content "$run/web-results.json"
}
Write-Host "Web tests passed (including a second process reusing the profile): $run"
