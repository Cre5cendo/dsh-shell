<#
.SYNOPSIS
  安装 DshShell —— DeepSeek Harness 的 Windows 桌面外壳。

.DESCRIPTION
  1) 把程序复制到 %LOCALAPPDATA%\Programs\DshShell
  2) 解除"来自 Internet"的标记，避免每次启动弹 SmartScreen「未知发布者」
  3) 创建桌面与开始菜单快捷方式
  已存在的 config.json 与 data\ 会保留，覆盖安装不会丢配置和登录态。

.PARAMETER Source
  含 DshShell.exe 的目录，默认是本脚本所在目录（解压安装包后直接运行即可）。

.PARAMETER InstallDir
  安装目录，默认 %LOCALAPPDATA%\Programs\DshShell。

.PARAMETER Start
  安装完成后立即启动。

.PARAMETER NoShortcuts
  不创建快捷方式。

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File install.ps1

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File install.ps1 -Start
#>
param(
    [string]$Source = $PSScriptRoot,
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\DshShell'),
    [switch]$Start,
    [switch]$NoShortcuts
)

$ErrorActionPreference = 'Stop'

function Write-Step([string]$text) { Write-Host "  $text" -ForegroundColor Gray }
function Write-Ok([string]$text) { Write-Host "  [OK] $text" -ForegroundColor Green }
function Write-Warn([string]$text) { Write-Host "  [!]  $text" -ForegroundColor Yellow }
function Write-Fail([string]$text) { Write-Host "  [X]  $text" -ForegroundColor Red }

Write-Host ''
Write-Host '  DeepSeek Harness 桌面外壳 - 安装程序' -ForegroundColor Cyan
Write-Host '  ====================================' -ForegroundColor DarkGray
Write-Host ''

# ---- 0. 安装包完整性 ----
$required = @(
    'DshShell.exe',
    'Microsoft.Web.WebView2.Core.dll',
    'Microsoft.Web.WebView2.WinForms.dll',
    'WebView2Loader.dll'
)
foreach ($name in $required) {
    if (-not (Test-Path (Join-Path $Source $name))) {
        Write-Fail "安装包不完整，缺少 $name"
        Write-Step "请把 zip 完整解压后再运行 install.ps1（当前目录：$Source）"
        exit 1
    }
}
Write-Ok '安装包完整'

# ---- 1. 关掉正在运行的旧实例（否则 exe 会被占用，覆盖失败）----
# 只关"安装目录里那一份"，避免误杀别处的副本；关掉的会在安装结束后自动重新拉起。
$targetExe = Join-Path $InstallDir 'DshShell.exe'
$wasRunning = $false
$running = @(Get-Process -Name 'DshShell' -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -and ($_.Path -ieq $targetExe) } catch { $false }
    })
if ($running.Count -gt 0) {
    Write-Step '检测到同一位置的 DshShell 正在运行，先关闭…'
    $running | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800
    $wasRunning = $true
    Write-Ok '已关闭旧实例（安装完成后会自动重新拉起）'
}

# ---- 2. 复制文件 ----
Write-Step "安装到 $InstallDir"
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item (Join-Path $Source 'DshShell.exe') $InstallDir -Force
Get-ChildItem (Join-Path $Source '*.dll') | ForEach-Object { Copy-Item $_.FullName $InstallDir -Force }
if (Test-Path (Join-Path $Source 'assets')) { Copy-Item (Join-Path $Source 'assets') $InstallDir -Recurse -Force }
foreach ($doc in @('README.md', 'LICENSE')) {
    if (Test-Path (Join-Path $Source $doc)) { Copy-Item (Join-Path $Source $doc) $InstallDir -Force }
}

$configSource = Join-Path $Source 'config.json'
$configTarget = Join-Path $InstallDir 'config.json'
if ((Test-Path $configSource) -and -not (Test-Path $configTarget)) {
    Copy-Item $configSource $configTarget -Force
    Write-Ok '已写入默认 config.json'
}
elseif (Test-Path $configTarget) {
    Write-Ok '保留已有的 config.json（覆盖安装不丢配置）'
}

# ---- 3. 解除"来自 Internet"标记 ----
# 从 GitHub 下载的 zip 解压后每个文件都带 Zone.Identifier，Windows 会当成"网上来的程序"
# 而弹 SmartScreen。安装后复制到本机目录，这里统一 Unblock，之后就安静了。
$unblocked = 0
Get-ChildItem $InstallDir -Recurse -File | ForEach-Object {
    try {
        Unblock-File -LiteralPath $_.FullName
        $unblocked++
    }
    catch {
    }
}
Write-Ok "已解除网络标记（$unblocked 个文件）"

# ---- 4. 快捷方式 ----
$exe = Join-Path $InstallDir 'DshShell.exe'
$icon = Join-Path $InstallDir 'assets\icon.ico'
if (-not $NoShortcuts) {
    $shell = New-Object -ComObject WScript.Shell
    $links = @(
        (Join-Path ([Environment]::GetFolderPath('Desktop')) 'DeepSeek Harness.lnk'),
        (Join-Path ([Environment]::GetFolderPath('Programs')) 'DeepSeek Harness\DeepSeek Harness.lnk')
    )
    foreach ($link in $links) {
        $folder = Split-Path -Parent $link
        if (-not (Test-Path $folder)) { New-Item -ItemType Directory -Force -Path $folder | Out-Null }
        $shortcut = $shell.CreateShortcut($link)
        $shortcut.TargetPath = $exe
        $shortcut.WorkingDirectory = $InstallDir
        $shortcut.Description = 'DeepSeek Harness 桌面外壳'
        if (Test-Path $icon) { $shortcut.IconLocation = "$icon,0" }
        $shortcut.Save()
    }
    Write-Ok '已创建桌面与开始菜单快捷方式'
}

# ---- 5. 运行环境检查（只提示，不阻断）----
Write-Host ''
Write-Host '  运行环境检查' -ForegroundColor Cyan

$webview2Version = $null
$webview2Keys = @(
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
    'HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
    'HKCU:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
)
foreach ($key in $webview2Keys) {
    if (Test-Path $key) {
        $version = (Get-ItemProperty -Path $key -Name 'pv' -ErrorAction SilentlyContinue).pv
        if ($version) { $webview2Version = $version; break }
    }
}
if ($webview2Version) {
    Write-Ok "WebView2 运行时 $webview2Version"
}
else {
    Write-Warn '没检测到 WebView2 运行时：窗口会退回 Edge/Chrome 窗口模式（功能不完整）'
    Write-Step '安装 WebView2：https://developer.microsoft.com/microsoft-edge/webview2/'
}

$hasDsh = $false
$dshManager = Join-Path $env:APPDATA 'DSH\data\.bin\dsh.cmd'
if (Test-Path $dshManager) {
    $hasDsh = $true
    Write-Ok '检测到 DSH 自带的 dsh 启动器'
}
if (-not $hasDsh -and (Get-Command 'dsh' -ErrorAction SilentlyContinue)) {
    $hasDsh = $true
    Write-Ok '检测到 PATH 上的 dsh'
}
if (-not $hasDsh) {
    if (Get-Command 'node' -ErrorAction SilentlyContinue) {
        Write-Warn '没找到 dsh，但检测到 Node.js：首次启动会用 npx 自动下载 @deepseek-ai/dsh（需联网，第一次会慢一点）'
    }
    else {
        Write-Warn '既没找到 dsh 也没找到 Node.js'
        Write-Step '请先安装 Node.js 22+（https://nodejs.org），再执行 npm i -g @deepseek-ai/dsh'
    }
}

# ---- 6. 完成 ----
Write-Host ''
Write-Host '  安装完成' -ForegroundColor Green
Write-Step "程序位置：$InstallDir"
Write-Step '配置与运行数据：同目录下的 config.json 与 data\'
Write-Step '托盘图标：左键切换显示/隐藏，右键打开菜单（可退出、重启服务、开机自启）'
Write-Step '热键：Ctrl+Alt+D 显示/隐藏窗口'
Write-Host ''

if ($Start -or $wasRunning) {
    Write-Step '正在启动…'
    Start-Process -FilePath $exe -WorkingDirectory $InstallDir
}
else {
    Write-Step '双击桌面上的「DeepSeek Harness」即可启动'
}
Write-Host ''
