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
$installerScript = Join-Path $repoRoot "installer\Shiori.iss"
$compiler = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue

if ($null -eq $compiler) {
    $defaultCompiler = Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"
    if (Test-Path -LiteralPath $defaultCompiler) {
        $compilerPath = $defaultCompiler
    }
    else {
        $userCompiler = Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"
        if (Test-Path -LiteralPath $userCompiler) {
            $compilerPath = $userCompiler
        }
        else {
            throw "Inno Setup 6 was not found. Install it from https://jrsoftware.org/isinfo.php or run Publish-Windows.ps1 with -SkipInstaller."
        }
    }
}
else {
    $compilerPath = $compiler.Source
}

& $compilerPath `
    "/DAppVersion=$Version" `
    "/DSourceDirectory=$sourceDirectory" `
    "/DOutputDirectory=$outputDirectory" `
    $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

$installerPath = Join-Path $outputDirectory "shiori-v$Version-win-x64-setup.exe"
$hash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = "$installerPath.sha256"
Set-Content -LiteralPath $checksumPath -Value "$hash  $(Split-Path $installerPath -Leaf)" -Encoding ascii
Write-Output $installerPath
Write-Output $checksumPath
