param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$sourceDirectory = (Resolve-Path $PublishDirectory).Path
$outputDirectory = Join-Path $repoRoot "artifacts"
$installerSource = Join-Path $repoRoot "installer\Shiori.wxs"
$licenseRtf = Join-Path $repoRoot "installer\License.rtf"
$installerPath = Join-Path $outputDirectory "shiori-v$Version-win-x64-setup.msi"

$wix = Get-Command "wix.exe" -ErrorAction SilentlyContinue
if ($null -eq $wix) {
    $wix = Get-Command "wix" -ErrorAction SilentlyContinue
}
if ($null -eq $wix) {
    throw "The WiX Toolset CLI was not found. Install it with 'dotnet tool install --global wix' or run Publish-Windows.ps1 with -SkipInstaller."
}

& $wix.Source build `
    -arch x64 `
    -ext WixToolset.UI.wixext `
    -d "AppVersion=$Version" `
    -d "SourceDirectory=$sourceDirectory" `
    -d "LicenseRtf=$licenseRtf" `
    -out $installerPath `
    $installerSource
if ($LASTEXITCODE -ne 0) {
    throw "WiX build failed with exit code $LASTEXITCODE."
}

$hash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = "$installerPath.sha256"
Set-Content -LiteralPath $checksumPath -Value "$hash  $(Split-Path $installerPath -Leaf)" -Encoding ascii
Write-Output $installerPath
Write-Output $checksumPath
