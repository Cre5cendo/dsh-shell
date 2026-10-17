<#
.SYNOPSIS
  卸载 DshShell（DeepSeek Harness 桌面外壳）。

.DESCRIPTION
  停止进程 → 移除开机自启 → 删除桌面/开始菜单快捷方式 → 删除安装目录。
  不会动 DSH 本身，也不会动 ~\.dsh 里的会话数据。

.PARAMETER InstallDir
  安装目录，默认 %LOCALAPPDATA%\Programs\DshShell。

.PARAMETER KeepData
  保留 data\ 目录（日志、WebView2 profile）。

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File uninstall.ps1
#>
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\DshShell'),
    [switch]$KeepData
)

$ErrorActionPreference = 'Continue'

Write-Host ''
Write-Host '  卸载 DeepSeek Harness 桌面外壳' -ForegroundColor Cyan
Write-Host '  ============================' -ForegroundColor DarkGray
Write-Host ''

# ---- 1. 停止进程 ----
$running = @(Get-Process -Name 'DshShell' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    $running | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800
    Write-Host '  已关闭 DshShell' -ForegroundColor Gray
}

# ---- 2. 开机自启 ----
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (Test-Path $runKey) {
    $item = Get-ItemProperty -Path $runKey -Name 'DshShell' -ErrorAction SilentlyContinue
    if ($null -ne $item) {
        Remove-ItemProperty -Path $runKey -Name 'DshShell' -Force -ErrorAction SilentlyContinue
        Write-Host '  已移除开机自启' -ForegroundColor Gray
    }
}

# ---- 3. 快捷方式 ----
$links = @(
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'DeepSeek Harness.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'DeepSeek Harness\DeepSeek Harness.lnk')
)
foreach ($link in $links) {
    if (Test-Path $link) {
        Remove-Item $link -Force -ErrorAction SilentlyContinue
        Write-Host "  已删除快捷方式：$link" -ForegroundColor Gray
    }
}
$startMenuFolder = Join-Path ([Environment]::GetFolderPath('Programs')) 'DeepSeek Harness'
if ((Test-Path $startMenuFolder) -and -not (Get-ChildItem $startMenuFolder -Force -ErrorAction SilentlyContinue)) {
    Remove-Item $startMenuFolder -Force -ErrorAction SilentlyContinue
}

# ---- 4. 安装目录 ----
if (Test-Path $InstallDir) {
    $dataDir = Join-Path $InstallDir 'data'
    if ($KeepData -and (Test-Path $dataDir)) {
        Get-ChildItem $InstallDir -Force | Where-Object { $_.Name -ne 'data' } | ForEach-Object {
            Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
        Write-Host "  已删除程序文件，保留数据目录：$dataDir" -ForegroundColor Gray
    }
    else {
        Remove-Item $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "  已删除 $InstallDir" -ForegroundColor Gray
    }
}
else {
    Write-Host "  没有找到安装目录：$InstallDir" -ForegroundColor Gray
}

Write-Host ''
Write-Host '  卸载完成（DSH 与 ~\.dsh 中的数据未受影响）' -ForegroundColor Green
Write-Host ''
