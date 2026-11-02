<#
.SYNOPSIS
    一键构建安装包并生成 AppCast。

.DESCRIPTION
    -Version 是本次发布的唯一版本来源：它会被同时注入 setup.iss 的 MyAppVersion
    （由 Inno Setup 写进安装包的版本资源）和 AppCast 生成器的 --file-version，
    所以 appcast 里的 sparkle:version / 安装包版本资源不会再各自漂移。

.PARAMETER Version
    发布版本号，2~4 段纯数字，例如 1.2.0 或 1.2.0.1。

.EXAMPLE
    .\build.ps1 -Version 1.2.0
#>
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Version
)

# 下面所有相对路径（setup.iss / Output / AppCast / ViewModels）都以脚本所在目录为准，
# 这样在任意工作目录下执行 build.ps1 都不会找错文件。
Set-Location -LiteralPath $PSScriptRoot

$projectName  = "FastFluentFilesFolders"
$archList     = @("x64", "arm64")
$innoCompiler = "C:\Program Files\Inno Setup 7\ISCC.exe"

# NetSparkle 相关路径
$appCastTool  = "netsparkle-generate-appcast"   # 全局工具，已在 PATH 中
$keyPath      = "C:\Users\aaron\AppData\Local\netsparkle"
$baseUrl      = "https://github.com/Xhscfdj/FastFluentFilesFolders/releases/latest/download"

# 安装包经过 gh-proxy 加速下载。
# 【重要】代理前缀必须在“生成 AppCast 之前”就写进 -u：生成器是对写好的 appcast.xml
# 直接签名的，生成完再改文件内容（以前的做法）会让签名与文件对不上，客户端报
# "Signature check of appcast failed (Security mode is Strict)"，检查更新永远失败。
$ghProxyPrefix   = "https://v4.gh-proxy.org/"
$downloadBaseUrl = "$ghProxyPrefix$baseUrl"

# ===== 第零步：校验版本号 =====
# 只允许纯数字点分：Inno Setup 的 AppVersion 与 NetSparkle 的版本比较都按数字段解析，
# 带 -beta / +hash 这类后缀会让“有没有新版本”的判断变得不可预期。
$Version = "$Version".Trim()
if ($Version -notmatch '^\d+(\.\d+){1,3}$') {
    Write-Host "[ERROR] -Version 必须是 2~4 段纯数字版本号（如 1.2.0），当前为 '$Version'。" -ForegroundColor Red
    Write-Host "        例：.\build.ps1 -Version 1.2.0" -ForegroundColor Gray
    exit 1
}

Write-Host "Starting build for $projectName $Version..." -ForegroundColor Cyan

# 客户端 NetSparkle 用 Configs.Version 与 appcast 的 sparkle:version 比对，两者不一致时
# “检查更新”会失效（appcast 报 1.2.0、客户端自称 1.1.1 → 要么永远没有更新提示，要么反复提示）。
# build.ps1 不改源码，只提醒。
$configsPath = "./ViewModels/Configs.cs"
if (Test-Path $configsPath) {
    $versionLine = Select-String -Path $configsPath -Pattern 'static readonly string Version\s*=\s*"([^"]+)"' |
        Select-Object -First 1
    if ($versionLine) {
        $codeVersion = $versionLine.Matches[0].Groups[1].Value
        if ($codeVersion -ne $Version) {
            Write-Host "[WARN] ViewModels/Configs.cs 里的 Version 仍是 '$codeVersion'，与本次发布版本 '$Version' 不一致。" -ForegroundColor Yellow
            Write-Host "       客户端拿 Configs.Version 和 appcast 比对，不改成 '$Version' 的话用户收不到这次更新。" -ForegroundColor Yellow
        }
        else {
            Write-Host "Configs.Version 与发布版本一致：$codeVersion" -ForegroundColor Green
        }
    }
}

# ===== 第一步：为每个架构构建安装包 =====
foreach ($arch in $archList) {
    Write-Host "`n===== Building $Version for architecture: $arch =====" -ForegroundColor Yellow

    # 1. 发布 .NET 应用
    Write-Host "Publishing .NET app for $arch..."
    $publishArgs = @(
        "publish", "-c", "Release", "-r", "win-$arch", "--self-contained", "true",
        "-p:PublishTrimmed=false", "-p:PublishSingleFile=false",
	"-p:PlatformTarget=$arch",
        "-p:WindowsPackageType=None", "-o", "./publish_$arch"
    )
    & dotnet $publishArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERROR] dotnet publish failed for $arch" -ForegroundColor Red
        exit 1
    }

    # 2. 编译 Inno Setup 安装包
    # MyAppVersion 由 -Version 注入（setup.iss 里是 #ifndef 兜底定义），
    # Inno Setup 会把它写进安装包的版本资源，AppCast 生成器正是从这里读版本的。
    Write-Host "Compiling Inno Setup installer for $arch (version $Version)..."
    & $innoCompiler "setup.iss" "/DDestinationArch=$arch" "/DMyAppVersion=$Version"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERROR] Inno Setup compilation failed for $arch" -ForegroundColor Red
        exit 1
    }

    # 确认 -Version 真的进了安装包版本资源（AppCast 生成器就是从这里读版本的）。
    # 这里查 ProductVersion 而不是 FileVersion：Inno Setup 写的是 ProductVersion。
    $setupExe = "./Output/$arch/${projectName}_Setup_$arch.exe"
    if (-not (Test-Path $setupExe)) {
        Write-Host "[ERROR] 找不到安装包：$setupExe（检查 setup.iss 的 OutputDir）" -ForegroundColor Red
        exit 1
    }
    $exeVersion = "$((Get-Item $setupExe).VersionInfo.ProductVersion)".Trim()
    if ($exeVersion -ne $Version) {
        Write-Host "[ERROR] $setupExe 的版本资源是 '$exeVersion'，与 -Version '$Version' 不一致" -ForegroundColor Red
        Write-Host "        多半是 setup.iss 里 MyAppVersion 没走 #ifndef 兜底、或被写死了。" -ForegroundColor Gray
        exit 1
    }
    Write-Host "Installer version OK: $exeVersion" -ForegroundColor Green

    Write-Host "Completed for $arch." -ForegroundColor Green
}

# ===== 第二步：分别生成两个 AppCast =====
# 根据 setup.iss，安装包已经位于 Output\x64 和 Output\arm64
# 版本号由生成器从安装包的 ProductVersion 读取（上面已校验过它等于 -Version）；
# --file-version 只是兜底：安装包版本资源读不出来时用它，读得出来时以安装包为准。
Write-Host "`n===== Generating AppCast for x64 =====" -ForegroundColor Cyan
& $appCastTool `
    -a "./AppCast/x64" `
    -b "./Output/x64" `
    -e exe `
    -n $projectName `
    -u $downloadBaseUrl `
    --file-version $Version `
    --key-path $keyPath
if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] AppCast generation failed for x64" -ForegroundColor Red
    exit 1
}

Write-Host "`n===== Generating AppCast for arm64 =====" -ForegroundColor Cyan
& $appCastTool `
    -a "./AppCast/arm64" `
    -b "./Output/arm64" `
    -e exe `
    -n $projectName `
    -u $downloadBaseUrl `
    --file-version $Version `
    --key-path $keyPath
if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] AppCast generation failed for arm64" -ForegroundColor Red
    exit 1
}

# ===== 第三步：生成后自检（签名与版本都必须与本次构建对上）=====
# 签名对不上，客户端会直接报 "Signature check of appcast failed (Security mode is Strict)"；
# 版本对不上，用户永远收不到这次更新。两种都必须在构建阶段拦下来，不能等到发布后才发现。
Write-Host "`n===== Verifying AppCast signature and version =====" -ForegroundColor Cyan
foreach ($arch in $archList) {
    $appcastPath = "./AppCast/$arch/appcast.xml"
    $signature = (Get-Content "$appcastPath.signature" -Raw).Trim()
    $verifyOutput = & $appCastTool --verify $appcastPath --signature $signature --key-path $keyPath 2>&1
    if (($verifyOutput -join " ") -notmatch "Signature valid") {
        Write-Host "[ERROR] AppCast signature invalid for $arch -- 不要上传这份 appcast" -ForegroundColor Red
        exit 1
    }
    Write-Host "Signature valid: $appcastPath" -ForegroundColor Green

    $appcastXml = Get-Content $appcastPath -Raw
    if ($appcastXml -notmatch '<sparkle:version>([^<]+)</sparkle:version>') {
        Write-Host "[ERROR] $appcastPath 里找不到 sparkle:version" -ForegroundColor Red
        exit 1
    }
    $appcastVersion = $Matches[1].Trim()
    if ($appcastVersion -ne $Version) {
        Write-Host "[ERROR] $appcastPath 的版本是 '$appcastVersion'，与 -Version '$Version' 不一致" -ForegroundColor Red
        exit 1
    }
    Write-Host "AppCast version OK: $appcastVersion" -ForegroundColor Green
}

# ===== 第四步：重命名为最终发布用的文件名 =====
Write-Host "`n===== Renaming AppCast files for Release upload =====" -ForegroundColor Cyan

Move-Item "./AppCast/x64/appcast.xml"              "./AppCast/x64/appcast-x64.xml"              -Force
Move-Item "./AppCast/x64/appcast.xml.signature"    "./AppCast/x64/appcast-x64.xml.signature"    -Force
Move-Item "./AppCast/arm64/appcast.xml"            "./AppCast/arm64/appcast-arm64.xml"          -Force
Move-Item "./AppCast/arm64/appcast.xml.signature"  "./AppCast/arm64/appcast-arm64.xml.signature" -Force

Write-Host "`n===== All builds completed successfully! ($projectName $Version) =====" -ForegroundColor Cyan
Write-Host "Files ready for GitHub Release:" -ForegroundColor Cyan
Write-Host "  ./Output/x64/FastFluentFilesFolders_Setup_x64.exe" -ForegroundColor Gray
Write-Host "  ./Output/arm64/FastFluentFilesFolders_Setup_arm64.exe" -ForegroundColor Gray
Write-Host "  ./AppCast/x64/appcast-x64.xml (+ .signature)" -ForegroundColor Gray
Write-Host "  ./AppCast/arm64/appcast-arm64.xml (+ .signature)" -ForegroundColor Gray
Write-Host "`nUpload these 6 files to the GitHub Release." -ForegroundColor Yellow
Write-Host "【必须 6 个一起传】appcast 里的 length 和安装包签名都随安装包变化，只补传其中几个会让客户端校验失败。" -ForegroundColor Yellow
Write-Host "上传完成后运行 .\verify-release.ps1 自检线上资产是否与本次构建一致。" -ForegroundColor Yellow
