param(
    [Parameter(Mandatory = $true)]
    [string]$DestinationDirectory
)

$ErrorActionPreference = "Stop"

$ripgrepVersion = "15.2.0"
$archiveName = "ripgrep-$ripgrepVersion-x86_64-pc-windows-msvc.zip"
$archiveHash = "71b2fef860abe467217a538ff31de02f5258807c0129f771846f87bd029aafc5"
$downloadUrl = "https://github.com/BurntSushi/ripgrep/releases/download/$ripgrepVersion/$archiveName"
$cacheDirectory = Join-Path $PSScriptRoot "..\artifacts\third-party"
$archivePath = Join-Path $cacheDirectory $archiveName
$extractDirectory = Join-Path $cacheDirectory "ripgrep-$ripgrepVersion-x86_64-pc-windows-msvc"

New-Item -ItemType Directory -Force -Path $cacheDirectory | Out-Null

if (-not (Test-Path -LiteralPath $archivePath)) {
    Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath
}

$actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $archiveHash) {
    throw "ripgrep archive checksum mismatch. Expected $archiveHash, got $actualHash."
}

if (Test-Path -LiteralPath $extractDirectory) {
    Remove-Item -LiteralPath $extractDirectory -Recurse -Force
}

Expand-Archive -LiteralPath $archivePath -DestinationPath $cacheDirectory
Copy-Item -LiteralPath (Join-Path $extractDirectory "rg.exe") -Destination $DestinationDirectory
Copy-Item -LiteralPath (Join-Path $extractDirectory "LICENSE-MIT") `
    -Destination (Join-Path $DestinationDirectory "ripgrep-LICENSE-MIT.txt")
Copy-Item -LiteralPath (Join-Path $extractDirectory "UNLICENSE") `
    -Destination (Join-Path $DestinationDirectory "ripgrep-UNLICENSE.txt")
