#Requires -Version 5.1
<#
.SYNOPSIS
    LRS MSIX packaging script - build 3 platforms, create .msix, sign, bundle
.PARAMETER Version
    版本号（格式 x.y.z.w），不指定则从 Package.appxmanifest 读取并更新
.PARAMETER Configuration
    编译配置，默认 Release
.PARAMETER OutputDir
    输出目录，默认脚本所在目录下的 LRS\AppPackages
.PARAMETER CertificatePath
    用于签名的 .pfx 证书文件路径（可选）
.PARAMETER CertificatePassword
    证书密码（建议使用环境变量或 SecureString）
#>
param(
    [string]$Version,
    [string]$Configuration = "Release",
    [string]$OutputDir = "$PSScriptRoot\LRS\AppPackages",
    [string]$CertificatePath,
    [string]$CertificatePassword
)

$ErrorActionPreference = "Stop"
$ProjectDir = "$PSScriptRoot\LRS"
$Csproj     = "$ProjectDir\LRS.csproj"
$Manifest   = "$ProjectDir\Package.appxmanifest"

# ---- Read version from Package.appxmanifest if not specified ----
if (-not $Version) {
    [xml]$appx = Get-Content $Manifest
    $Version = $appx.Package.Identity.Version
} else {
    Write-Host "  Updating Package.appxmanifest version to $Version..." -ForegroundColor Gray
    [xml]$appx = Get-Content $Manifest
    $appx.Package.Identity.Version = $Version
    $appx.Save($Manifest)
}
$VersionDir = "LRS_$($Version)_Test"
$BundleName = "LRS_$($Version)_x86_x64_arm64.msixbundle"

Write-Host "== LRS Packaging Script ==" -ForegroundColor Cyan
Write-Host "Version: $Version | Config: $Configuration | Output: $OutputDir\$VersionDir" -ForegroundColor Gray

# ---- Check SDK ----
$sdkVersion = (dotnet --version) 2>$null
if ($LASTEXITCODE -ne 0) { throw "dotnet not available" }
$major = [int]($sdkVersion -replace '\..*')
if ($major -ne 8) {
    Write-Warning "Current SDK: $sdkVersion (.NET 8 required). Trying global.json..."
    if (Test-Path "$PSScriptRoot\global.json") {
        Write-Host "  global.json found" -ForegroundColor Green
    } else {
        throw "Please install .NET 8 SDK or add global.json to project root"
    }
}

# ---- Tool paths (from NuGet cache) ----
$BuildToolsRoot = "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools"
$BuildToolsVer  = (Get-ChildItem $BuildToolsRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1).Name
if (-not $BuildToolsVer) { throw "Microsoft.Windows.SDK.BuildTools NuGet package not found" }
$BuildToolsBin  = "$BuildToolsRoot\$BuildToolsVer\bin\10.0.28000.0\x64"
$MakeAppx       = "$BuildToolsBin\MakeAppx.exe"
$SignTool       = "$BuildToolsBin\signtool.exe"
if (-not (Test-Path $MakeAppx)) { throw "MakeAppx.exe not found: $MakeAppx" }
if (-not (Test-Path $SignTool)) { throw "signtool.exe not found: $SignTool" }

# ---- Platform map ----
$Platforms = @(
    @{ Name="x64";   RID="win-x64"   },
    @{ Name="x86";   RID="win-x86"   },
    @{ Name="ARM64"; RID="win-arm64" }
)

# ---- Helper function for signing ----
function Sign-File {
    param([string]$FilePath)
    if ($CertificatePath -and (Test-Path $CertificatePath)) {
        Write-Host "  Using certificate: $CertificatePath" -ForegroundColor Gray
        $signArgs = "/fd SHA256 /f `"$CertificatePath`" /p $CertificatePassword /tr http://timestamp.digicert.com /td SHA256 `"$FilePath`""
    } else {
        Write-Host "  Using default certificate (auto-select from store)" -ForegroundColor Gray
        $signArgs = "/fd SHA256 /a /tr http://timestamp.digicert.com /td SHA256 `"$FilePath`""
    }
    & $SignTool sign $signArgs 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Sign failed: $FilePath" }
}

# ================================================================
# 1. dotnet publish (3 platforms)
# ================================================================
Write-Host "`n[1/4] dotnet publish..." -ForegroundColor Yellow
foreach ($p in $Platforms) {
    Write-Host "  Building $($p.Name)..." -NoNewline
    dotnet publish $Csproj -c $Configuration -p:Platform=$($p.Name) --nologo 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $($p.Name)" }
    Write-Host " done" -ForegroundColor Green
}

# ================================================================
# 2. MakeAppx create .msix
# ================================================================
Write-Host "`n[2/4] Creating .msix packages..." -ForegroundColor Yellow
$MsixFiles = @()
foreach ($p in $Platforms) {
    $bin  = "$ProjectDir\bin\$($p.Name)\$Configuration\net8.0-windows10.0.19041.0\$($p.RID)"
    $msix = "$bin\LRS_$($Version)_$($p.Name).msix"

    if (-not (Test-Path "$bin\AppxManifest.xml")) { throw "AppxManifest.xml not found in: $bin" }

    Write-Host "  Packing $($p.Name)..." -NoNewline
    & $MakeAppx pack /d $bin /p $msix /o /nfv 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "MakeAppx pack failed: $($p.Name)" }
    $sizeMB = [math]::Round((Get-Item $msix).Length / 1MB, 1)
    Write-Host " done ($sizeMB MB)" -ForegroundColor Green

    $MsixFiles += $msix
}

# ================================================================
# 3. Sign .msix
# ================================================================
Write-Host "`n[3/4] Signing .msix packages..." -ForegroundColor Yellow
foreach ($msix in $MsixFiles) {
    $name = Split-Path $msix -Leaf
    Write-Host "  Signing $name..." -NoNewline
    Sign-File $msix
    Write-Host " done" -ForegroundColor Green
}

# ================================================================
# 4. Bundle + output to AppPackages
# ================================================================
Write-Host "`n[4/4] Creating bundle..." -ForegroundColor Yellow

$OutDir   = "$OutputDir\$VersionDir"
$WorkDir  = "$OutputDir\bundle_work"

New-Item -ItemType Directory -Path $OutDir  -Force | Out-Null
New-Item -ItemType Directory -Path $WorkDir -Force | Out-Null

foreach ($msix in $MsixFiles) {
    Copy-Item $msix $WorkDir -Force
}

$BundlePath = "$OutDir\$BundleName"
& $MakeAppx bundle /d $WorkDir /p $BundlePath /o 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { throw "MakeAppx bundle failed" }

Write-Host "  Signing bundle..." -NoNewline
Sign-File $BundlePath
Write-Host " done" -ForegroundColor Green

Remove-Item -Recurse $WorkDir -Force

# ---- Report ----
Write-Host "`n===== Package Complete =====" -ForegroundColor Cyan
Write-Host "Output: $OutDir\" -ForegroundColor White
Get-ChildItem $OutDir | ForEach-Object {
    $size = if ($_.PSIsContainer) { "" } else { " ($([math]::Round($_.Length/1MB,1)) MB)" }
    Write-Host "  $($_.Name)$size"
}