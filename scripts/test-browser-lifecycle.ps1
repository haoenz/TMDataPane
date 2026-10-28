$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$run = Join-Path $root ('artifacts/browser-lifecycle-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force "$run/UsageBridge", "$run/config/UsageBridge" | Out-Null
Copy-Item "$root/build/native/Release/UsageBridge.dll", "$root/build/native/Release/LifecycleHost.exe" $run
Copy-Item "$root/build/collector/*" "$run/UsageBridge" -Recurse
# No authentication and no site dependency: navigation may fail, but its process tree must still be cleaned up.
Set-Content "$run/config/UsageBridge/config.json" '{"version":2,"enabled":true,"sources":[{"id":"browser","kind":"browser_dom","url":"http://127.0.0.1:9/","timeoutSeconds":5,"fields":[{"name":"value","path":"#balance"}]}],"metrics":[{"id":"browser","source":"browser"}],"views":[{"slot":1,"metric":"browser","label":"Browser"}]}' -Encoding UTF8
Set-Content "$run/config/UsageBridge/collection.enabled" '1'
foreach ($kill in @($false,$true)) {
    $seconds = if ($kill) { 60 } else { 20 }
    $log = "$run/host-$seconds.log"
    $arguments = '"{0}" "{1}" {2} auto' -f "$run/UsageBridge.dll", "$run/config", $seconds
    $testProcess = Start-Process "$run/LifecycleHost.exe" -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput $log
    $null = $testProcess.Handle
    try {
        $browserSeen = $false
        for ($i=0; $i -lt 60; $i++) {
            if ((Test-Path $log) -and (Select-String $log -Pattern '^CHILD \d+ msedgewebview2.exe')) { $browserSeen=$true; break }
            Start-Sleep -Milliseconds 250
        }
        if (!$browserSeen) { throw 'No browser descendant observed; cannot claim browser cleanup passed' }
        if ($kill) { Stop-Process -Id $testProcess.Id -Force }
        elseif (!$testProcess.WaitForExit(30000)) { throw 'Host timed out' }
        elseif ($testProcess.ExitCode -ne 0) { throw 'Host failed' }
        $pids = @(Get-Content $log | ForEach-Object { if ($_ -match '^CHILD (\d+) ') { [int]$Matches[1] } })
        for ($i=0; $i -lt 40; $i++) {
            $remaining = @($pids | ForEach-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
            if (!$remaining.Count) { break }
            Start-Sleep -Milliseconds 250
        }
        if ($remaining.Count) { throw "Descendant processes remain: $($remaining.Id -join ', ')" }
    } finally { if (!$testProcess.HasExited) { Stop-Process -Id $testProcess.Id -Force } }
}
Write-Host "Browser process tree cleanup passed: $run"
