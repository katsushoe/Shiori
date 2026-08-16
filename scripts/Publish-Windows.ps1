param(
    [string]$Version = "1.1.0"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$artifactsRoot = Join-Path $repoRoot "artifacts"
$packageName = "shiori-v$Version-win-x64"
$publishDirectory = Join-Path $artifactsRoot $packageName
$archivePath = Join-Path $artifactsRoot "$packageName.zip"
$nativeDirectory = Join-Path $repoRoot "native\shiori-engine"
$nativeLibrary = Join-Path $nativeDirectory "target\release\shiori_engine.dll"

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null

cargo build --release --manifest-path (Join-Path $nativeDirectory "Cargo.toml")
if ($LASTEXITCODE -ne 0) {
    throw "Rust release build failed with exit code $LASTEXITCODE."
}

dotnet publish (Join-Path $repoRoot "src\Shiori.Cli\Shiori.Cli.csproj") `
    --configuration Release `
    --output $publishDirectory `
    --self-contained false `
    --no-restore
if ($LASTEXITCODE -ne 0) {
    throw ".NET publish failed with exit code $LASTEXITCODE."
}

Copy-Item -LiteralPath $nativeLibrary -Destination $publishDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination $publishDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot "LICENSE") -Destination $publishDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot "CHANGELOG.md") -Destination $publishDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot "docs\release-notes-v$Version.md") `
    -Destination (Join-Path $publishDirectory "RELEASE_NOTES.md")

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}

Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $archivePath
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = "$archivePath.sha256"
Set-Content -LiteralPath $checksumPath -Value "$hash  $packageName.zip" -Encoding ascii
Write-Output $archivePath
Write-Output $checksumPath
