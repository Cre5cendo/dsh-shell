# 创建 / 删除 DshShell 的桌面与开始菜单快捷方式，指向已安装的程序。
#
#   pwsh -File tools\install-shortcut.ps1 -Desktop -StartMenu
#   pwsh -File tools\install-shortcut.ps1 -Remove
#
# 一般来说不需要单独跑这个脚本：install.ps1 已经会建快捷方式。
[CmdletBinding()]
param(
    [string]$Dir = (Join-Path $env:LOCALAPPDATA 'Programs\DshShell'),
    [switch]$Desktop,
    [switch]$StartMenu,
    [switch]$Remove,
    [string]$Name = 'DeepSeek Harness'
)

$ErrorActionPreference = 'Stop'
$exe = Join-Path $Dir 'DshShell.exe'
$icon = Join-Path $Dir 'assets\icon.ico'

$targets = @()
if ($Desktop) { $targets += [Environment]::GetFolderPath('Desktop') }
if ($StartMenu) { $targets += (Join-Path ([Environment]::GetFolderPath('Programs')) $Name) }

if ($targets.Count -eq 0) {
    Write-Output '请至少指定 -Desktop 或 -StartMenu（或用 -Remove 删除）。'
    exit 1
}

$shell = New-Object -ComObject WScript.Shell

foreach ($dir in $targets) {
    $link = Join-Path $dir ($Name + '.lnk')

    if ($Remove) {
        if (Test-Path $link) { Remove-Item $link -Force; Write-Output "已删除 $link" }
        else { Write-Output "不存在: $link" }
        continue
    }

    if (-not (Test-Path $exe)) { throw "找不到 $exe，请先安装（install.ps1）或用 -Dir 指定安装目录" }
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

    $shortcut = $shell.CreateShortcut($link)
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $Dir
    $shortcut.Description = 'DeepSeek Harness 桌面外壳'
    if (Test-Path $icon) { $shortcut.IconLocation = "$icon,0" }
    $shortcut.Save()

    Write-Output "已创建 $link"
}
