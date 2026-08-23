param(
    [string]$Version = "2.3.5",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$artifactsRoot = Join-Path $repoRoot "artifacts"
$packageName = "shiori-v$Version-win-x64"
$publishDirectory = Join-Path $artifactsRoot $packageName
$binaryDirectory = Join-Path $publishDirectory "bin"
$archivePath = Join-Path $artifactsRoot "$packageName.zip"
$nativeDirectory = Join-Path $repoRoot "native\shiori-engine"
$nativeLibrary = Join-Path $nativeDirectory "target\release\shiori_engine.dll"

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null
New-Item -ItemType Directory -Force -Path $binaryDirectory | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $publishDirectory "config") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $publishDirectory "logs") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $publishDirectory "data") | Out-Null

cargo build --release --manifest-path (Join-Path $nativeDirectory "Cargo.toml")
if ($LASTEXITCODE -ne 0) {
    throw "Rust release build failed with exit code $LASTEXITCODE."
}

dotnet publish (Join-Path $repoRoot "src\Shiori.Cli\Shiori.Cli.csproj") `
    --configuration Release `
    --runtime win-x64 `
    --output $binaryDirectory `
    --disable-build-servers `
    --self-contained true
if ($LASTEXITCODE -ne 0) {
    throw ".NET publish failed with exit code $LASTEXITCODE."
}

Copy-Item -LiteralPath $nativeLibrary -Destination $binaryDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination $publishDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot "LICENSE") -Destination $publishDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot "CHANGELOG.md") -Destination $publishDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot "docs\release-notes-v$Version.md") `
    -Destination (Join-Path $publishDirectory "RELEASE_NOTES.md")

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}

tar.exe -a -c -f $archivePath -C $publishDirectory .
if ($LASTEXITCODE -ne 0) {
    throw "ZIP packaging failed with exit code $LASTEXITCODE."
}
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = "$archivePath.sha256"
Set-Content -LiteralPath $checksumPath -Value "$hash  $packageName.zip" -Encoding ascii
Write-Output $archivePath
Write-Output $checksumPath

if (-not $SkipInstaller) {
    & (Join-Path $PSScriptRoot "Build-WindowsInstaller.ps1") `
        -Version $Version `
        -PublishDirectory $publishDirectory
}
