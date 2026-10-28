$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$destination = Join-Path $root 'third_party/TrafficMonitor'
New-Item -ItemType Directory -Force $destination | Out-Null
# Match the installed host, never silently track master.
$base = 'https://raw.githubusercontent.com/zhongyang219/TrafficMonitor/V1.86'
foreach ($file in @('include/PluginInterface.h', 'LICENSE')) {
    $target = Join-Path $destination (Split-Path $file -Leaf)
    if (!(Test-Path -LiteralPath $target)) {
        $temporary = "$target.download"
        Invoke-WebRequest "$base/$file" -UseBasicParsing -OutFile $temporary
        Move-Item -LiteralPath $temporary -Destination $target -Force
    }
}
$hashes = foreach ($name in @('PluginInterface.h', 'LICENSE')) {
    $path = Join-Path $destination $name
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = [BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($path))).Replace('-', '') }
    finally { $sha.Dispose() }
    [pscustomobject]@{ Path = $name; Hash = $hash }
}
$hashes | ConvertTo-Json | Set-Content (Join-Path $destination 'hashes.json') -Encoding UTF8
