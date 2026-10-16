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

Write-Host "Starting build for $projectName..." -ForegroundColor Cyan

# ===== 第一步：为每个架构构建安装包 =====
foreach ($arch in $archList) {
    Write-Host "`n===== Building for architecture: $arch =====" -ForegroundColor Yellow

    # 1. 发布 .NET 应用
    Write-Host "Publishing .NET app for $arch..."
    $publishArgs = @(
        "publish", "-c", "Release", "-r", "win-$arch", "--self-contained", "true",
        "-p:PublishTrimmed=false", "-p:PublishSingleFile=false",
        "-p:WindowsPackageType=None", "-o", "./publish_$arch"
    )
    & dotnet $publishArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERROR] dotnet publish failed for $arch" -ForegroundColor Red
        exit 1
    }

    # 2. 编译 Inno Setup 安装包
    Write-Host "Compiling Inno Setup installer for $arch..."
    & $innoCompiler "setup.iss" "/DDestinationArch=$arch"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERROR] Inno Setup compilation failed for $arch" -ForegroundColor Red
        exit 1
    }

    Write-Host "Completed for $arch." -ForegroundColor Green
}

# ===== 第二步：分别生成两个 AppCast =====
# 根据 setup.iss，安装包已经位于 Output\x64 和 Output\arm64
Write-Host "`n===== Generating AppCast for x64 =====" -ForegroundColor Cyan
& $appCastTool `
    -a "./AppCast/x64" `
    -b "./Output/x64" `
    -e exe `
    -n $projectName `
    -u $downloadBaseUrl `
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
    --key-path $keyPath
if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] AppCast generation failed for arm64" -ForegroundColor Red
    exit 1
}

# ===== 第三步：签名自检（AppCast 必须与 .signature 匹配，否则客户端检查更新会直接失败）=====
Write-Host "`n===== Verifying AppCast signatures =====" -ForegroundColor Cyan
foreach ($arch in $archList) {
    $appcastPath = "./AppCast/$arch/appcast.xml"
    $signature = (Get-Content "$appcastPath.signature" -Raw).Trim()
    $verifyOutput = & $appCastTool --verify $appcastPath --signature $signature --key-path $keyPath 2>&1
    if (($verifyOutput -join " ") -notmatch "Signature valid") {
        Write-Host "[ERROR] AppCast signature invalid for $arch -- 不要上传这份 appcast" -ForegroundColor Red
        exit 1
    }
    Write-Host "Signature valid: $appcastPath" -ForegroundColor Green
}

# ===== 第四步：重命名为最终发布用的文件名 =====
Write-Host "`n===== Renaming AppCast files for Release upload =====" -ForegroundColor Cyan

Move-Item "./AppCast/x64/appcast.xml"              "./AppCast/x64/appcast-x64.xml"              -Force
Move-Item "./AppCast/x64/appcast.xml.signature"    "./AppCast/x64/appcast-x64.xml.signature"    -Force
Move-Item "./AppCast/arm64/appcast.xml"            "./AppCast/arm64/appcast-arm64.xml"          -Force
Move-Item "./AppCast/arm64/appcast.xml.signature"  "./AppCast/arm64/appcast-arm64.xml.signature" -Force

Write-Host "`n===== All builds completed successfully! =====" -ForegroundColor Cyan
Write-Host "Files ready for GitHub Release:" -ForegroundColor Cyan
Write-Host "  ./Output/x64/FastFluentFilesFolders_Setup_x64.exe" -ForegroundColor Gray
Write-Host "  ./Output/arm64/FastFluentFilesFolders_Setup_arm64.exe" -ForegroundColor Gray
Write-Host "  ./AppCast/x64/appcast-x64.xml (+ .signature)" -ForegroundColor Gray
Write-Host "  ./AppCast/arm64/appcast-arm64.xml (+ .signature)" -ForegroundColor Gray
Write-Host "`nUpload these 6 files to the GitHub Release." -ForegroundColor Yellow
Write-Host "【必须 6 个一起传】appcast 里的 length 和安装包签名都随安装包变化，只补传其中几个会让客户端校验失败。" -ForegroundColor Yellow
Write-Host "上传完成后运行 .\verify-release.ps1 自检线上资产是否与本次构建一致。" -ForegroundColor Yellow