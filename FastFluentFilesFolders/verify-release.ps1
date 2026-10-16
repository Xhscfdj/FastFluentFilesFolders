#Requires -Version 5.1
<#
    发布自检：把 GitHub 上“最新发布”里的 AppCast / 安装包和本地构建结果逐项比对。

    用法（在 FastFluentFilesFolders 目录下，上传完 release 资产之后执行）：
        pwsh -File .\verify-release.ps1
        powershell -ExecutionPolicy Bypass -File .\verify-release.ps1

    每个架构检查 4 件事：
      1. 线上 appcast 与本地 AppCast\{arch}\appcast-{arch}.xml 是否逐字节一致
      2. 线上 appcast 的 .signature 能否通过 Ed25519 校验（签名对不上 = 客户端“检查更新失败”）
      3. 线上安装包大小是否与本地 Output\{arch} 里的一致
      4. 线上 appcast 里 enclosure 的 length 是否等于线上安装包的真实大小

    任一项不通过都会列出“需要重新上传”的文件清单，并以退出码 1 结束。

    为什么要自检：appcast 一旦重新生成（重新 build 之后安装包大小/签名都会变），
    就必须把 appcast + .signature + 安装包 一起传上去；只补传其中一个，
    客户端就会报 "Signature check of appcast failed" 或者下载后校验失败。
#>

$projectName  = "FastFluentFilesFolders"
$archList     = @("x64", "arm64")
$keyPath      = "C:\Users\aaron\AppData\Local\netsparkle"
$appCastTool  = "netsparkle-generate-appcast"
$repo         = "Xhscfdj/FastFluentFilesFolders"
$downloadBase = "https://github.com/$repo/releases/latest/download"

try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch { }

$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("lrs-verify-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tmp -Force | Out-Null

function Get-RemoteFile {
    param([string]$Url, [string]$Dest)
    try {
        Invoke-WebRequest -Uri $Url -OutFile $Dest -UseBasicParsing -ErrorAction Stop | Out-Null
        return $true
    } catch {
        return $false
    }
}

function Get-LengthAttribute {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return "" }
    return [regex]::Match((Get-Content $Path -Raw), 'length="(\d+)"').Groups[1].Value
}

Write-Host "`n===== LRS 发布自检 =====" -ForegroundColor Cyan

try {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/latest" `
        -UseBasicParsing -Headers @{ "User-Agent" = "lrs-verify" }
} catch {
    Write-Host "[ERROR] 无法读取 GitHub Release: $($_.Exception.Message)" -ForegroundColor Red
    exit 2
}

Write-Host "线上最新发布: $($release.tag_name)   (published $($release.published_at))" -ForegroundColor Gray

$failed = $false
$uploadList = New-Object System.Collections.Generic.List[string]

foreach ($arch in $archList) {
    Write-Host "`n----- $arch -----" -ForegroundColor Yellow

    $localAppcast = "./AppCast/$arch/appcast-$arch.xml"
    $localSigFile = "$localAppcast.signature"
    $localExe     = "./Output/$arch/${projectName}_Setup_$arch.exe"
    $remoteXml    = Join-Path $tmp "appcast-$arch.xml"
    $remoteSig    = "$remoteXml.signature"

    $exeName = "${projectName}_Setup_$arch.exe"

    # ---- 1) 线上 appcast 内容是否与本地一致 ----
    if (-not (Get-RemoteFile "$downloadBase/appcast-$arch.xml" $remoteXml)) {
        Write-Host "[FAIL] 线上找不到 appcast-$arch.xml" -ForegroundColor Red
        $failed = $true
    } elseif (-not (Test-Path $localAppcast)) {
        Write-Host "[WARN] 本地没有 $localAppcast，跳过内容比对" -ForegroundColor DarkYellow
    } elseif ((Get-FileHash $remoteXml).Hash -ne (Get-FileHash $localAppcast).Hash) {
        Write-Host ("[FAIL] 线上 appcast 与本地不一致（线上 length=" + (Get-LengthAttribute $remoteXml) +
                    "，本地 length=" + (Get-LengthAttribute $localAppcast) + "）") -ForegroundColor Red
        $uploadList.Add("appcast-$arch.xml")
        $failed = $true
    } else {
        Write-Host "[OK]   线上 appcast 与本地逐字节一致" -ForegroundColor Green
    }

    # ---- 2) 线上 appcast 的签名是否有效 ----
    if (Test-Path $remoteXml) {
        if (-not (Get-RemoteFile "$downloadBase/appcast-$arch.xml.signature" $remoteSig)) {
            Write-Host "[FAIL] 线上找不到 appcast-$arch.xml.signature" -ForegroundColor Red
            $uploadList.Add("appcast-$arch.xml.signature")
            $failed = $true
        } else {
            $sigText = (Get-Content $remoteSig -Raw).Trim()
            $verifyOutput = & $appCastTool --verify $remoteXml --signature $sigText --key-path $keyPath 2>&1
            if (($verifyOutput -join " ") -match "Signature valid") {
                Write-Host "[OK]   线上 appcast 签名有效" -ForegroundColor Green
            } else {
                Write-Host "[FAIL] 线上 appcast 签名无效 —— 客户端会报 'Signature check of appcast failed'，检查更新必然失败" -ForegroundColor Red
                $uploadList.Add("appcast-$arch.xml.signature")
                $failed = $true
            }
        }
    }

    # ---- 3) 线上安装包大小 vs 本地 ----
    $asset = $release.assets | Where-Object { $_.name -eq $exeName } | Select-Object -First 1
    if ($null -eq $asset) {
        Write-Host "[FAIL] 线上找不到 $exeName" -ForegroundColor Red
        $uploadList.Add($exeName)
        $failed = $true
    } elseif (Test-Path $localExe) {
        $localSize = (Get-Item $localExe).Length
        if ($asset.size -ne $localSize) {
            Write-Host "[FAIL] 线上安装包与本地不是同一次构建（线上 $($asset.size) 字节 / 本地 $localSize 字节）" -ForegroundColor Red
            $uploadList.Add($exeName)
            $failed = $true
        } else {
            Write-Host "[OK]   线上安装包大小与本地一致（$localSize 字节）" -ForegroundColor Green
        }
    } else {
        Write-Host "[WARN] 本地没有 $localExe，跳过大小比对" -ForegroundColor DarkYellow
    }

    # ---- 4) 线上 appcast 里的 length 是否等于线上安装包实际大小 ----
    if ((Test-Path $remoteXml) -and $null -ne $asset) {
        $remoteLength = Get-LengthAttribute $remoteXml
        if ($remoteLength -ne "$($asset.size)") {
            Write-Host "[FAIL] 线上 appcast 记的 length=$remoteLength，线上安装包实际 $($asset.size) 字节 —— 两者不是同一份" -ForegroundColor Red
            if (-not $uploadList.Contains("appcast-$arch.xml")) { $uploadList.Add("appcast-$arch.xml") }
            if (-not $uploadList.Contains($exeName)) { $uploadList.Add($exeName) }
            $failed = $true
        } else {
            Write-Host "[OK]   线上 appcast 的 length 与线上安装包一致" -ForegroundColor Green
        }
    }
}

Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue

if ($failed) {
    Write-Host "`n[FAIL] 发布不一致，需要重新上传这些文件：" -ForegroundColor Red
    $uploadList | Select-Object -Unique | ForEach-Object { Write-Host "   $_" -ForegroundColor Red }
    Write-Host "`n（本地文件位置：AppCast\x64|arm64\ 与 Output\x64|arm64\；GitHub 不允许同名覆盖，先删掉旧资产再传）" -ForegroundColor Gray
    exit 1
}

Write-Host "`n[OK] 线上发布与本地构建完全一致，客户端检查更新/下载/安装都应该正常。" -ForegroundColor Green
exit 0
