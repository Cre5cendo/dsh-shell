DshShell 1.0.0 - 快速开始
=========================

这是 DeepSeek Harness (DSH) 的 Windows 桌面外壳安装包。

安装
----
1. 把整个 zip 解压到一个普通目录（不要放在 DSH 的工作区里）
2. 双击 install.cmd（或在 PowerShell 里运行 install.ps1）
3. 桌面上会出现「DeepSeek Harness」，双击启动

安装脚本会：
  * 把程序装到 %LOCALAPPDATA%\Programs\DshShell
  * 解除文件的"来自 Internet"标记（避免每次弹 SmartScreen 提示）
  * 创建桌面和开始菜单快捷方式

卸载
----
双击 uninstall.cmd，或运行 uninstall.ps1。

需要什么
--------
* Windows 10 1809+ / Windows 11 (x64)
* WebView2 运行时：Win11 自带；Win10 一般随 Edge 自带。
  缺失时窗口会退回 Edge 窗口模式，功能不完整。
  下载：https://developer.microsoft.com/microsoft-edge/webview2/
* 已安装 DSH（二选一）：
  - Node.js 22+，然后 npx @deepseek-ai/dsh（首次会下载）
  - 或 DSH 管理器安装的版本（%APPDATA%\DSH\data\.bin\dsh.cmd）
* .NET Framework 4.8（Windows 10 1903+ 自带）

用法
----
* 托盘图标：左键 显示/隐藏窗口，右键 菜单（浏览器打开 / 重新加载 / 重启服务 /
  宿主引擎切换 / 开机自启 / 服务日志 / 退出）
* 热键 Ctrl+Alt+D：显示/隐藏窗口
* 关闭窗口 = 收进托盘；真正退出走托盘菜单的「退出」
* 重复双击图标不会开第二个实例，只会把已有窗口拉到前台

配置
----
%LOCALAPPDATA%\Programs\DshShell\config.json

  Port           0 = 每次随机端口（推荐，避免和你手动开的 dsh 抢端口）
  Host           auto | webview2 | edge | chrome
  CloseAction    tray（点 X 收托盘）| ask（询问）| quit（退出）
  MinimizeToTray 最小化时是否收进托盘
  QuitStopsServer 退出时是否顺带停掉 DSH 服务
  GlobalHotkey   全局热键，留空禁用

排障
----
* data\logs\shell.log        外壳自己的步骤日志
* data\logs\server-*.log     DSH 服务的原始输出（含 dsh web: URL）
* 没有托盘图标：Win11 会把新图标收进 ^ 溢出区；另外不要把 exe 放在
  DSH 工作区里（那里是低完整性目录，会弹安全警告且托盘注册失败）

更详细的说明见 README.md 或 GitHub 仓库页面。
