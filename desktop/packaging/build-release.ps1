param(
    [string]$Version = '0.2.2',
    [Parameter(Mandatory)][string]$SdkBin,
    [Parameter(Mandatory)][string]$VCRedist,
    [Parameter(Mandatory)][string]$InnoCompiler,
    [Parameter(Mandatory)][string]$CertificateThumbprint,
    [switch]$SkipPublish
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output = Join-Path $root "artifacts/v$Version"
$payload = Join-Path $output 'app'
$hostPayload = Join-Path $output 'host'
$packages = Join-Path $output 'packages'
New-Item -ItemType Directory -Path $packages -Force | Out-Null
function Assert-Exit { if ($LASTEXITCODE -ne 0) { throw "Build tool failed with exit code $LASTEXITCODE" } }
if (!$SkipPublish) {
    dotnet publish (Join-Path $root 'desktop/Equora.App/Equora.App.csproj') -c Release -p:Platform=x64 -r win-x64 --self-contained true -p:WindowsPackageType=None "-p:Version=$Version" -p:DebugType=None -p:DebugSymbols=false "-p:PublishDir=$payload/" -m:1 -nr:false -v:minimal
    Assert-Exit
    dotnet publish (Join-Path $root 'desktop/Equora.NativeHost/Equora.NativeHost.csproj') -c Release -p:Platform=x64 -r win-x64 --self-contained true "-p:Version=$Version" -p:DebugType=None -p:DebugSymbols=false -o $hostPayload -m:1 -nr:false -v:minimal
    Assert-Exit
}
$browserHost = Join-Path $payload 'BrowserHost'
New-Item -ItemType Directory -Path $browserHost -Force | Out-Null
Copy-Item -Path (Join-Path $hostPayload '*') -Destination $browserHost -Recurse -Force
$browserExtension = Join-Path $payload 'BrowserExtension'
New-Item -ItemType Directory -Path $browserExtension -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $root 'extension/Equora.BrowserExtension') -File | Where-Object Extension -in '.js','.html','.json','.png' | Copy-Item -Destination $browserExtension -Force
Get-ChildItem -LiteralPath $VCRedist -Filter '*.dll' | Copy-Item -Destination $payload -Force
New-Item -ItemType Directory -Path (Join-Path $payload 'Docs') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'docs/help.md'),(Join-Path $root 'docs/usage-restrictions.md'),(Join-Path $root 'LICENSE'),(Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination (Join-Path $payload 'Docs') -Force
# MSIX layout requires resources.pri at package root; unpackaged publish emits Equora.App.pri. Keep both.
if (!(Test-Path -LiteralPath (Join-Path $payload 'resources.pri'))) {
    Copy-Item -LiteralPath (Join-Path $payload 'Equora.App.pri') -Destination (Join-Path $payload 'resources.pri')
}
foreach ($file in @('Equora.App.exe','Equora.App.dll','equora_capi.dll','sqlite3.dll','hostfxr.dll','coreclr.dll','Microsoft.UI.Xaml.dll','resources.pri','msvcp140.dll','vcruntime140.dll','BrowserHost/com.equora.nativehost.exe','BrowserHost/coreclr.dll','BrowserExtension/manifest.json')) {
    if (!(Test-Path -LiteralPath (Join-Path $payload $file))) { throw "Missing runtime payload: $file" }
}
[xml]$manifest = Get-Content -LiteralPath (Join-Path $root 'desktop/Equora.App/Package.appxmanifest') -Raw -Encoding UTF8
$manifest.Package.Identity.SetAttribute('Version', "$Version.0")
$manifest.Package.Identity.SetAttribute('ProcessorArchitecture', 'x64')
$manifest.Package.Resources.Resource.SetAttribute('Language', 'zh-CN')
$manifest.Save((Join-Path $payload 'AppxManifest.xml'))
$msix = Join-Path $packages "Equora-v$Version-win-x64.msix"
& (Join-Path $SdkBin 'makeappx.exe') pack /d $payload /p $msix /o
Assert-Exit
& (Join-Path $SdkBin 'signtool.exe') sign /fd SHA256 /sha1 $CertificateThumbprint /s My $msix
Assert-Exit
& $InnoCompiler "/DAppVersion=$Version" "/DPayloadDir=$payload" "/DOutputDir=$packages" (Join-Path $PSScriptRoot 'Equora.iss')
Assert-Exit
$installer = Join-Path $packages "Equora-v$Version-win-x64-Setup.exe"
& (Join-Path $SdkBin 'signtool.exe') sign /fd SHA256 /sha1 $CertificateThumbprint /s My $installer
Assert-Exit
Export-Certificate -Cert "Cert:\CurrentUser\My\$CertificateThumbprint" -FilePath (Join-Path $packages "Equora-v$Version.cer") | Out-Null
Compress-Archive -Path (Join-Path $root 'extension/Equora.BrowserExtension') -DestinationPath (Join-Path $packages "Equora-v$Version-BrowserExtension.zip") -Force
$lines = Get-ChildItem -LiteralPath $packages -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object Name | ForEach-Object { "{0}  {1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
[IO.File]::WriteAllLines((Join-Path $packages 'SHA256SUMS.txt'), $lines)
Write-Output "Packages: $packages"
