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
$generatedSource = Join-Path $outputDirectory "Shiori.GeneratedFiles.wxs"

function Get-StableFileIdentity {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    $normalizedPath = $RelativePath.Replace("\", "/").ToLowerInvariant()
    $hash = [System.Security.Cryptography.SHA256]::HashData(
        [System.Text.Encoding]::UTF8.GetBytes($normalizedPath))
    $guidBytes = [byte[]]$hash[0..15]
    $guidBytes[7] = ($guidBytes[7] -band 0x0f) -bor 0x50
    $guidBytes[8] = ($guidBytes[8] -band 0x3f) -bor 0x80
    $identifier = [Convert]::ToHexString($hash[0..11])
    return [pscustomobject]@{
        Id = $identifier
        Guid = ([Guid]::new($guidBytes)).ToString()
    }
}

function Write-GeneratedFileSource {
    param(
        [Parameter(Mandatory = $true)][string]$BinaryDirectory,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    $builder = [System.Text.StringBuilder]::new()
    [void]$builder.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
    [void]$builder.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
    [void]$builder.AppendLine('  <Fragment>')
    [void]$builder.AppendLine('    <ComponentGroup Id="BinFiles">')
    foreach ($file in Get-ChildItem -LiteralPath $BinaryDirectory -File -Recurse | Sort-Object FullName) {
        $relativePath = [IO.Path]::GetRelativePath($BinaryDirectory, $file.FullName)
        $identity = Get-StableFileIdentity -RelativePath $relativePath
        $directory = if ([string]::Equals(
            (Split-Path $relativePath -Parent),
            'ja-JP',
            [StringComparison]::OrdinalIgnoreCase)) { 'JAJPFOLDER' } else { 'BINFOLDER' }
        $source = [Security.SecurityElement]::Escape($file.FullName)
        $name = [Security.SecurityElement]::Escape($file.Name)
        [void]$builder.AppendLine("      <Component Id=`"Bin_$($identity.Id)`" Directory=`"$directory`" Guid=`"$($identity.Guid)`">")
        [void]$builder.AppendLine("        <File Id=`"File_$($identity.Id)`" Source=`"$source`" Name=`"$name`" />")
        [void]$builder.AppendLine("        <RegistryValue Root=`"HKCU`" Key=`"Software\Akatsukisoft\Shiori\Components\BinFiles`" Name=`"$($identity.Id)`" Type=`"integer`" Value=`"1`" KeyPath=`"yes`" />")
        [void]$builder.AppendLine('      </Component>')
    }
    [void]$builder.AppendLine('    </ComponentGroup>')
    [void]$builder.AppendLine('  </Fragment>')
    [void]$builder.AppendLine('</Wix>')
    [IO.File]::WriteAllText($Destination, $builder.ToString(), [Text.UTF8Encoding]::new($false))
}

Write-GeneratedFileSource -BinaryDirectory (Join-Path $sourceDirectory "bin") -Destination $generatedSource

$wix = Get-Command "wix.exe" -ErrorAction SilentlyContinue
if ($null -eq $wix) {
    $wix = Get-Command "wix" -ErrorAction SilentlyContinue
}
if ($null -eq $wix) {
    throw "The WiX Toolset CLI was not found. Install it with 'dotnet tool install --global wix' or run Publish-Windows.ps1 with -SkipInstaller."
}

& $wix.Source build `
    -acceptEula wix7 `
    -arch x64 `
    -ext WixToolset.UI.wixext `
    -d "AppVersion=$Version" `
    -d "SourceDirectory=$sourceDirectory" `
    -d "LicenseRtf=$licenseRtf" `
    -out $installerPath `
    $installerSource `
    $generatedSource
if ($LASTEXITCODE -ne 0) {
    throw "WiX build failed with exit code $LASTEXITCODE."
}

# ICE60 is caused by the bundled e_sqlite3.dll not declaring a language, and
# ICE91 describes per-user files under LocalAppDataFolder. Both are expected
# for this explicitly per-user package; all other ICE findings remain fatal.
& $wix.Source msi validate `
    -acceptEula wix7 `
    -sice ICE60 `
    -sice ICE91 `
    $installerPath
if ($LASTEXITCODE -ne 0) {
    throw "WiX validation failed with exit code $LASTEXITCODE."
}

$hash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = "$installerPath.sha256"
Set-Content -LiteralPath $checksumPath -Value "$hash  $(Split-Path $installerPath -Leaf)" -Encoding ascii
Write-Output $installerPath
Write-Output $checksumPath
