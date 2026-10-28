$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$native = "$root/build/native/Release"
$run = Join-Path $root ('artifacts/lifecycle-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force "$run/UsageBridge", "$run/config/UsageBridge" | Out-Null
Copy-Item "$native/UsageBridge.dll", "$native/LifecycleHost.exe" $run
Copy-Item "$root/build/collector/*" "$run/UsageBridge" -Recurse
$collectorExe = [IO.Path]::GetFullPath("$run/UsageBridge/UsageBridge.Collector.exe")
function Get-OwnedCollector {
    @(Get-Process -Name 'UsageBridge.Collector' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $collectorExe })
}
function Start-TestHost([int]$Seconds) {
    $arguments = '"{0}" "{1}" {2} auto' -f "$run/UsageBridge.dll", "$run/config", $Seconds
    $testProcess = Start-Process -FilePath "$run/LifecycleHost.exe" -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput "$run/host-$Seconds.log"
    $null = $testProcess.Handle # Keep the handle so Windows PowerShell preserves ExitCode.
    return $testProcess
}
function Assert-NoCollector {
    for ($i = 0; $i -lt 20; $i++) {
        if ((Get-OwnedCollector).Count -eq 0) { return }
        Start-Sleep -Milliseconds 250
    }
    throw 'Collector survived host termination/unload'
}
# No sources: no collector should start.
$hostProcess = Start-TestHost 3
try {
    Start-Sleep -Seconds 1
    if ((Get-OwnedCollector).Count -ne 0) { throw 'Collector started without sources' }
    if (!$hostProcess.WaitForExit(10000)) { throw 'Host timed out' }
    if ($hostProcess.ExitCode -ne 0) { throw 'Test host failed' }
} finally { if (!$hostProcess.HasExited) { Stop-Process -Id $hostProcess.Id } }
Set-Content "$run/config/UsageBridge/config.json" '{"version":2,"enabled":true,"sources":[{"id":"demo","kind":"demo"}],"metrics":[{"id":"demo","source":"demo","input":"value"}],"views":[{"slot":1,"metric":"demo","label":"DEMO","format":"0.0","unit":"%"}]}' -Encoding UTF8
Set-Content "$run/config/UsageBridge/collection.enabled" '1'
$names = New-Object byte[] 2052
[BitConverter]::GetBytes([uint32]0x314E4255).CopyTo($names, 0)
[Text.Encoding]::Unicode.GetBytes('DEMO').CopyTo($names, 4)
[IO.File]::WriteAllBytes("$run/config/UsageBridge/display-names.bin", $names)
foreach ($kill in @($false, $true)) {
    $hostProcess = Start-TestHost $(if ($kill) { 60 } else { 15 })
    try {
        $found = $false
        for ($i = 0; $i -lt 40; $i++) {
            $children = Get-OwnedCollector
            if ($children.Count -eq 1) { $found = $true; break }
            if ($children.Count -gt 1) { throw 'Duplicate collector' }
            Start-Sleep -Milliseconds 250
        }
        if (!$found) { throw 'Collector did not start' }
        if ($kill) { Stop-Process -Id $hostProcess.Id -Force }
        elseif (!$hostProcess.WaitForExit(20000)) { throw 'Host unload timed out' }
        elseif ($hostProcess.ExitCode -ne 0) { throw 'Test host failed' }
        Assert-NoCollector
    } finally { if (!$hostProcess.HasExited) { Stop-Process -Id $hostProcess.Id -Force } }
}
if (!(Select-String -Path "$run/host-15.log" -SimpleMatch '42.0%')) { throw 'Snapshot never reached host' }
Write-Host "Lifecycle tests passed. Logs: $run"
