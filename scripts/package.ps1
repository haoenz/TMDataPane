param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$native = "$root/build/native/$Configuration"
foreach ($required in @("$native/UsageBridge.dll", "$root/build/collector/UsageBridge.Collector.exe", "$root/third_party/TrafficMonitor/LICENSE")) {
    if (!(Test-Path -LiteralPath $required)) { throw "Missing build artifact: $required" }
}
# Unique staging directory prevents obsolete DLLs surviving a later package.
$stage = Join-Path $root ('dist/UsageBridge-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force "$stage/plugins/UsageBridge", "$stage/licenses" | Out-Null
Copy-Item "$native/UsageBridge.dll" "$stage/plugins"
Copy-Item "$root/build/collector/*" "$stage/plugins/UsageBridge" -Recurse
Copy-Item "$root/third_party/TrafficMonitor/LICENSE" "$stage/licenses/TrafficMonitor.txt"
Copy-Item "$root/third_party/licenses/AngleSharp.txt" "$stage/licenses/AngleSharp.txt"
Copy-Item "$root/artifacts/nuget-packages/microsoft.web.webview2/1.0.4314.35/LICENSE.txt" "$stage/licenses/WebView2.txt"
foreach ($package in @('microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64')) {
    $packagePath = Get-ChildItem "$root/artifacts/nuget-packages/$package" -Directory | Sort-Object Name -Descending | Select-Object -First 1
    if ($packagePath) { Get-ChildItem $packagePath.FullName -File | Where-Object Name -Match 'LICENSE|NOTICE' | ForEach-Object { Copy-Item $_.FullName (Join-Path "$stage/licenses" ($package + '-' + $_.Name)) } }
}
Copy-Item "$root/README.md" $stage
Copy-Item "$root/docs" "$stage/docs" -Recurse
Copy-Item "$root/examples" "$stage/examples" -Recurse
Copy-Item "$root/scripts/install.ps1" "$stage/install.ps1"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, "$stage.zip")
Write-Host "Package: $stage.zip"
Set-Content "$root/dist/latest-package.txt" $stage -Encoding UTF8
