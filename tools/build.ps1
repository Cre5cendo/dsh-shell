# 编译 DshShell —— 只用 Windows 自带的 .NET Framework 编译器，不需要安装 .NET SDK 或 Visual Studio。
#
#   pwsh -File tools\build.ps1                # 只编译到 app\
#   pwsh -File tools\build.ps1 -Install       # 编译并安装到 %LOCALAPPDATA%\Programs\DshShell
#   pwsh -File tools\build.ps1 -Install -Sign # 顺便自签名（需管理员，仅本机信任）
#   pwsh -File tools\build.ps1 -Release       # 顺便打包 dist\DshShell-<版本>-win-x64.zip
#   pwsh -File tools\build.ps1 -Clean         # 先清旧产物（保留 app\data）
[CmdletBinding()]
param(
    [switch]$Clean,
    [switch]$Install,
    [switch]$Sign,
    [switch]$Release,
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root 'src'
$app = Join-Path $root 'app'
$vendor = Join-Path $root 'vendor'
$webview2Dir = Join-Path $vendor 'webview2'
$webview2Version = '1.0.4258.31'

# ---- 1. 编译器 ----
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) {
    throw '找不到 .NET Framework 4.x 编译器（csc.exe）。Windows 10/11 系统自带，不需要装 .NET SDK。'
}

# ---- 2. WebView2 SDK（首次编译自动下载）----
$coreDll = Join-Path $webview2Dir 'lib\net462\Microsoft.Web.WebView2.Core.dll'
if (-not (Test-Path $coreDll)) {
    Write-Host "下载 WebView2 SDK $webview2Version …" -ForegroundColor Cyan
    New-Item -ItemType Directory -Force -Path $vendor | Out-Null
    $nupkg = Join-Path $vendor "Microsoft.Web.WebView2.$webview2Version.nupkg"
    if (-not (Test-Path $nupkg)) {
        $url = "https://www.nuget.org/api/v2/package/Microsoft.Web.WebView2/$webview2Version"
        try {
            Invoke-WebRequest -Uri $url -OutFile $nupkg -UseBasicParsing
        }
        catch {
            throw "下载 WebView2 SDK 失败：$($_.Exception.Message)`n可手动下载 $url 保存为 vendor\Microsoft.Web.WebView2.$webview2Version.nupkg 后重试。"
        }
    }
    $zip = Join-Path $vendor 'sdk.zip'
    Copy-Item $nupkg $zip -Force
    if (Test-Path $webview2Dir) { Remove-Item $webview2Dir -Recurse -Force }
    Expand-Archive -LiteralPath $zip -DestinationPath $webview2Dir -Force
    Remove-Item $zip -Force
    if (-not (Test-Path $coreDll)) { throw 'WebView2 SDK 解包后仍找不到 lib\net462\Microsoft.Web.WebView2.Core.dll' }
}

# ---- 3. 图标 ----
$icon = Join-Path $root 'assets\icon.ico'
if (-not (Test-Path $icon)) {
    Write-Host '生成图标…' -ForegroundColor Cyan
    $source = Join-Path $root 'assets\source-icon.png'
    if (Test-Path $source) {
        & (Join-Path $PSScriptRoot 'make-icon.ps1') -Source $source -CropSize 670 -CropTop 10 -OutPath $icon | Out-Null
    }
    else {
        & (Join-Path $PSScriptRoot 'make-icon.ps1') -OutPath $icon | Out-Null
    }
}

# ---- 4. 清理 ----
if ($Clean -and (Test-Path $app)) {
    # 保留 data\：配置、状态、日志、WebView2 profile 都在里面
    Get-ChildItem $app -Force | Where-Object { $_.Name -ne 'data' } | Remove-Item -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $app | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $app 'assets') | Out-Null

# ---- 5. 编译 ----
$sources = @(Get-ChildItem -Path $src -Filter '*.cs' | ForEach-Object { $_.FullName })
$references = @(
    (Join-Path $webview2Dir 'lib\net462\Microsoft.Web.WebView2.Core.dll'),
    (Join-Path $webview2Dir 'lib\net462\Microsoft.Web.WebView2.WinForms.dll'),
    'System.dll',
    'System.Core.dll',
    'System.Drawing.dll',
    'System.Windows.Forms.dll',
    'System.Web.Extensions.dll'
)

$arguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/warn:4')
$arguments += "/out:$(Join-Path $app 'DshShell.exe')"
if (Test-Path $icon) { $arguments += "/win32icon:$icon" }
$manifest = Join-Path $src 'app.manifest'
if (Test-Path $manifest) { $arguments += "/win32manifest:$manifest" }
foreach ($reference in $references) { $arguments += "/r:$reference" }
foreach ($source in $sources) { $arguments += $source }

Write-Host "编译 $($sources.Count) 个源文件 → app\DshShell.exe" -ForegroundColor Cyan
& $csc @arguments
if ($LASTEXITCODE -ne 0) { throw "编译失败（csc 退出码 $LASTEXITCODE）" }

# ---- 6. 运行时依赖与资源 ----
Copy-Item (Join-Path $webview2Dir 'lib\net462\Microsoft.Web.WebView2.Core.dll') $app -Force
Copy-Item (Join-Path $webview2Dir 'lib\net462\Microsoft.Web.WebView2.WinForms.dll') $app -Force
Copy-Item (Join-Path $webview2Dir 'runtimes\win-x64\native\WebView2Loader.dll') $app -Force
if (Test-Path $icon) { Copy-Item $icon (Join-Path $app 'assets\icon.ico') -Force }
$configTemplate = Join-Path $root 'config.json'
if ((Test-Path $configTemplate) -and -not (Test-Path (Join-Path $app 'config.json'))) {
    Copy-Item $configTemplate $app -Force
}
foreach ($doc in @('README.md', 'LICENSE')) {
    $path = Join-Path $root $doc
    if (Test-Path $path) { Copy-Item $path $app -Force }
}

Write-Host ''
Write-Host '编译完成，产物：' -ForegroundColor Green
Get-ChildItem $app -Recurse -File |
    Where-Object { $_.FullName -notlike "*$([IO.Path]::DirectorySeparatorChar)data$([IO.Path]::DirectorySeparatorChar)*" } |
    ForEach-Object {
        Write-Host ("  {0,-46} {1,9:N0} 字节" -f $_.FullName.Substring($app.Length + 1), $_.Length)
    }

# ---- 7. 可选：自签名 ----
if ($Sign) {
    $signScript = Join-Path $PSScriptRoot 'sign.ps1'
    if (Test-Path $signScript) {
        & $signScript -Exe (Join-Path $app 'DshShell.exe')
    }
}

# ---- 8. 可选：安装 ----
if ($Install) {
    $installScript = Join-Path $root 'install.ps1'
    if (-not (Test-Path $installScript)) { throw '找不到 install.ps1' }
    & $installScript -Source $app
}

# ---- 9. 可选：打发布包 ----
if ($Release) {
    $dist = Join-Path $root 'dist'
    New-Item -ItemType Directory -Force -Path $dist | Out-Null
    $stage = Join-Path $dist 'package'
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $stage | Out-Null

    Copy-Item (Join-Path $app 'DshShell.exe') $stage -Force
    Get-ChildItem (Join-Path $app '*.dll') | ForEach-Object { Copy-Item $_.FullName $stage -Force }
    Copy-Item (Join-Path $app 'assets') $stage -Recurse -Force
    Copy-Item (Join-Path $app 'config.json') $stage -Force
    foreach ($name in @('install.ps1', 'install.cmd', 'uninstall.ps1', 'uninstall.cmd', 'README.md', 'README.txt', 'LICENSE')) {
        $path = Join-Path $root $name
        if (Test-Path $path) { Copy-Item $path $stage -Force }
    }

    $zip = Join-Path $dist "DshShell-$Version-win-x64.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
    Remove-Item $stage -Recurse -Force

    Write-Host ''
    Write-Host "发布包：$zip" -ForegroundColor Green
    Write-Host ("大小：{0:N0} 字节" -f (Get-Item $zip).Length)
}
