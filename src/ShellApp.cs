using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DshShell
{
    /// <summary>外壳主控制器：托盘、服务托管、窗口/浏览器宿主、热键、开机自启、退出询问。</summary>
    internal sealed class ShellApp : ApplicationContext
    {
        private const int HotkeyId = 0x4D53;
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "DshShell";

        private readonly ShellPaths paths;
        private readonly ShellConfig config;
        private readonly ShellState state;

        private DshServer server;
        private BrowserHost browser;
        private ShellWindow window;
        private NotifyIcon tray;
        private MessageWindow hotkeyWindow;
        private readonly Form anchorForm = new Form();
        private ToolStripMenuItem autoStartItem;
        private bool trayHideHintShown;
        private ToolStripMenuItem engineWebViewItem;
        private ToolStripMenuItem engineEdgeItem;
        private ToolStripMenuItem engineChromeItem;
        private string activeEngine = "";
        private bool quitting;
        private int restartCount;

        public ShellApp(ShellPaths paths, ShellConfig config, ShellState state)
        {
            this.paths = paths;
            this.config = config;
            this.state = state;
        }

        /// <summary>启动外壳：建窗口/托盘、注册热键、拉起服务。</summary>
        public void Start()
        {
            // 建一个永不显示的消息锚点窗口，用于把外部信号（第二次双击图标）切回 UI 线程。
            IntPtr anchorHandle = anchorForm.Handle;

            activeEngine = ResolveEngine();
            ShellLog.Write("宿主引擎 = " + activeEngine + " (config.Host=" + config.Host + ")");

            if (activeEngine == "webview2")
            {
                window = new ShellWindow(config, paths);
                window.QuitRequested += delegate { Quit(false); };
                window.HiddenToTray += OnHiddenToTray;
                if (config.StartMinimized)
                {
                    window.Opacity = 0;
                }

                window.Show();
                ShellLog.Write("窗口已显示 (StartMinimized=" + config.StartMinimized + ")");
                if (config.StartMinimized)
                {
                    window.HideToTray();
                    window.Opacity = 1;
                }
            }

            BuildTray();
            ShellLog.Write("托盘已就绪");

            RegisterHotkey();
            StartServer();
        }

        /// <summary>决定用哪个宿主引擎。</summary>
        private string ResolveEngine()
        {
            if (config.Host == "edge" || config.Host == "chrome")
            {
                string resolved = BrowserHost.ResolveEngine(config.Host);
                if (resolved != null)
                {
                    return resolved;
                }

                return HasWebView2() ? "webview2" : "edge";
            }

            if (config.Host == "webview2")
            {
                return "webview2";
            }

            if (HasWebView2())
            {
                return "webview2";
            }

            string browserEngine = BrowserHost.ResolveEngine("auto");
            return browserEngine == null ? "webview2" : browserEngine;
        }

        private static bool HasWebView2()
        {
            try
            {
                string version = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
                return !string.IsNullOrEmpty(version);
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ---------------------------------------------------------------- 服务托管

        private void StartServer()
        {
            server = new DshServer(paths, config);
            server.UrlReady += OnServerUrlReady;
            server.Failed += OnServerFailed;
            server.Exited += OnServerExited;
            server.Start();
        }

        private void OnServerUrlReady(string url)
        {
            state.ServerPid = server.ProcessId;
            state.ServerUrl = url;
            state.StartedAt = DateTime.Now.ToString("s", CultureInfo.InvariantCulture);
            state.Host = activeEngine;
            try
            {
                state.ServerPort = new Uri(url).Port;
            }
            catch (Exception)
            {
            }

            state.Save(paths.StatePath);

            if (activeEngine == "webview2")
            {
                if (window != null)
                {
                    window.NavigateTo(url);
                }

                return;
            }

            StartBrowserWindow(url);
        }

        private void StartBrowserWindow(string url)
        {
            if (browser != null)
            {
                browser.Stop();
                browser = null;
            }

            browser = new BrowserHost(paths);
            IntPtr handle = browser.Start(activeEngine, url, config.WindowWidth, config.WindowHeight);
            if (handle == IntPtr.Zero)
            {
                Notify("浏览器窗口没有打开", "没找到可用的 Edge/Chrome，或窗口启动超时。可把 config.json 的 Host 改成 webview2。", ToolTipIcon.Warning);
            }
        }

        private void OnServerFailed(string message)
        {
            if (window != null)
            {
                window.ShowError(message);
            }

            Notify("DSH 服务启动失败", "右键托盘图标可以重启服务，或服务日志。", ToolTipIcon.Error);
        }

        private void OnServerExited(int exitCode, bool unexpected)
        {
            if (quitting)
            {
                return;
            }

            if (unexpected)
            {
                Notify("DSH 服务已退出", "退出码 " + exitCode.ToString(CultureInfo.InvariantCulture) + "。", ToolTipIcon.Warning);

                if (config.AutoRestartServer && restartCount < 5)
                {
                    restartCount = restartCount + 1;
                    System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
                    timer.Interval = 1500;
                    timer.Tick += delegate
                    {
                        timer.Stop();
                        timer.Dispose();
                        if (!quitting)
                        {
                            StartServer();
                        }
                    };
                    timer.Start();
                }
            }
        }

        private void RestartServer()
        {
            restartCount = 0;
            if (server != null)
            {
                server.Stop();
            }

            StartServer();
        }

        // ---------------------------------------------------------------- 托盘

        private void BuildTray()
        {
            tray = new NotifyIcon();
            tray.Icon = LoadTrayIcon();
            tray.Text = "DeepSeek Harness";
            tray.Visible = true;
            tray.DoubleClick += delegate { ToggleWindow(); };

            ContextMenuStrip menu = new ContextMenuStrip();


            ToolStripMenuItem openBrowser = new ToolStripMenuItem("浏览器打开");
            openBrowser.Click += delegate { OpenInDefaultBrowser(); };
            menu.Items.Add(openBrowser);

            ToolStripMenuItem reload = new ToolStripMenuItem("重新加载");
            reload.Click += delegate
            {
                if (window != null)
                {
                    window.ReloadWebView();
                }
            };
            menu.Items.Add(reload);

            ToolStripMenuItem restart = new ToolStripMenuItem("重启服务");
            restart.Click += delegate { RestartServer(); };
            menu.Items.Add(restart);

            menu.Items.Add(new ToolStripSeparator());

            // 宿主引擎：直接放在主菜单里，三选一打勾（不再做二级菜单）
            engineWebViewItem = new ToolStripMenuItem("自绘窗口");
            engineWebViewItem.Click += delegate { SwitchEngine("webview2"); };
            engineEdgeItem = new ToolStripMenuItem("Edge 窗口");
            engineEdgeItem.Click += delegate { SwitchEngine("edge"); };
            engineChromeItem = new ToolStripMenuItem("Chrome 窗口");
            engineChromeItem.Click += delegate { SwitchEngine("chrome"); };
            menu.Items.Add(engineWebViewItem);
            menu.Items.Add(engineEdgeItem);
            menu.Items.Add(engineChromeItem);

            autoStartItem = new ToolStripMenuItem("开机自启");
            autoStartItem.CheckOnClick = false;
            autoStartItem.Click += delegate { ToggleAutoStart(); };
            menu.Items.Add(autoStartItem);


            ToolStripMenuItem openLog = new ToolStripMenuItem("服务日志");
            openLog.Click += delegate { OpenPath(server == null ? paths.LogDir : server.LogPath); };
            menu.Items.Add(openLog);


            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem quitItem = new ToolStripMenuItem("退出");
            quitItem.Click += delegate { RequestQuitFromTray(); };
            menu.Items.Add(quitItem);

            // 深色主题 + 大行距（对齐托盘菜单那种比例）
            ToolStripManager.Renderer = new DarkMenuRenderer();
            // 打勾用的图标栏必须留着：WinForms 的勾号就画在那一栏里
            menu.ShowImageMargin = true;
            menu.Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Regular);
            menu.BackColor = DarkMenuRenderer.Back;
            menu.ForeColor = DarkMenuRenderer.Text;
            menu.Padding = new Padding(0, 6, 0, 6);
            foreach (ToolStripItem item in menu.Items) { StyleMenuItem(item); }

            // 定位交回系统默认（NotifyIcon 自己弹），不再自定义位置
            tray.ContextMenuStrip = menu;
            tray.MouseClick += OnTrayMouseClick;
            RefreshTrayChecks();
            ShowTrayHintOnce();
        }

        /// <summary>窗口收进托盘时提示一次图标在哪（Win11 默认把新图标收进溢出区）。</summary>
        private void OnHiddenToTray()
        {
            if (trayHideHintShown)
            {
                return;
            }

            trayHideHintShown = true;
            Notify(
                "DSH 已最小化到托盘",
                "程序在后台继续运行。图标在右下角托盘区；若没看到，点一下 ^ 展开再把它拖到任务栏上即可固定显示。按 Ctrl+Alt+D 也能随时呼出窗口。",
                ToolTipIcon.Info);
        }

        private void ToggleMinimizeToTray()
        {
            config.MinimizeToTray = !config.MinimizeToTray;
            config.Save(paths.ConfigPath);
            RefreshTrayChecks();
        }

        /// <summary>左键单击托盘图标：显示/隐藏窗口。</summary>
        private void OnTrayMouseClick(object sender, MouseEventArgs args)
        {
            if (args.Button == MouseButtons.Left)
            {
                ToggleWindow();
            }
        }

        /// <summary>统一下拉项尺寸：宽 200、高 36，文字左内边距 10。</summary>
        private void StyleMenuItem(ToolStripItem item)
        {
            if (item is ToolStripSeparator)
            {
                return;
            }

            // 用内边距控制尺寸：下拉菜单会用「首选宽度」重排条目，
            // 写死 Size 会被忽略（菜单会缩到文字那么宽，子菜单翻转时还会压住父菜单）
            item.AutoSize = true;
            item.Padding = new Padding(12, 8, 72, 8);
            item.ForeColor = DarkMenuRenderer.Text;

            ToolStripMenuItem menuItem = item as ToolStripMenuItem;
            if (menuItem != null && menuItem.HasDropDownItems)
            {
                // 子菜单也要关掉图标栏，否则两级缩进不一致（看起来就是"错位"）
                ToolStripDropDownMenu dropDownMenu = menuItem.DropDown as ToolStripDropDownMenu;
                if (dropDownMenu != null)
                {
                    dropDownMenu.ShowImageMargin = false;
                    dropDownMenu.BackColor = DarkMenuRenderer.Back;
                    dropDownMenu.ForeColor = DarkMenuRenderer.Text;
                }
                foreach (ToolStripItem child in menuItem.DropDownItems)
                {
                    StyleMenuItem(child);
                }
            }
        }

        private Icon LoadTrayIcon()
        {
            // 托盘取系统小图标尺寸（100% 缩放 16x16，125% 缩放 20x20）
            try
            {
                if (File.Exists(paths.IconPath))
                {
                    return new Icon(paths.IconPath, SystemInformation.SmallIconSize);
                }
            }
            catch (Exception)
            {
            }

            // 退路：直接取 exe 内嵌图标（编译时用 /win32icon 打进去的那份）
            try
            {
                Icon embedded = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (embedded != null)
                {
                    return embedded;
                }
            }
            catch (Exception)
            {
            }

            return SystemIcons.Application;
        }

        /// <summary>首次运行提示托盘图标的位置：Win11 默认会把新图标收进溢出区。</summary>
        private void ShowTrayHintOnce()
        {
            string marker = Path.Combine(paths.DataDir, ".tray-hint-shown");
            bool firstRun = !File.Exists(marker);
            if (firstRun)
            {
                try
                {
                    File.WriteAllText(marker, DateTime.Now.ToString("s", CultureInfo.InvariantCulture));
                }
                catch (Exception)
                {
                }
            }

            if (firstRun)
            {
                Notify(
                    "DSH 外壳已启动",
                    "图标在右下角托盘区；如果没看到，点一下 ^ 展开再把它拖出来。左键单击可显示/隐藏窗口。",
                    ToolTipIcon.Info);
            }
        }

        private void RefreshTrayChecks()
        {
            if (autoStartItem != null)
            {
                autoStartItem.Checked = IsAutoStartEnabled();
            }

            if (engineWebViewItem != null)
            {
                engineWebViewItem.Checked = config.Host == "webview2";
                engineEdgeItem.Checked = config.Host == "edge";
                engineChromeItem.Checked = config.Host == "chrome";
            }

        }

        private void Notify(string title, string text, ToolTipIcon icon)
        {
            try
            {
                if (tray != null)
                {
                    tray.BalloonTipTitle = title;
                    tray.BalloonTipText = text;
                    tray.BalloonTipIcon = icon;
                    tray.ShowBalloonTip(6000);
                }
            }
            catch (Exception)
            {
            }
        }

        // ---------------------------------------------------------------- 窗口动作

        /// <summary>把「再次双击图标」切成 UI 线程上的激活动作。</summary>
        public void RequestActivate()
        {
            try
            {
                anchorForm.BeginInvoke((MethodInvoker)delegate { ActivateExistingInstance(); });
            }
            catch (Exception)
            {
            }
        }

        /// <summary>把已有窗口拉到前台（第二次启动外壳时走这里）。</summary>
        public void ActivateExistingInstance()
        {
            if (activeEngine == "webview2")
            {
                if (window != null)
                {
                    window.RestoreFromTray();
                }
            }
            else if (browser != null)
            {
                browser.Show();
            }

            RefreshTrayChecks();
        }

        private bool IsWindowVisibleNow()
        {
            if (activeEngine == "webview2")
            {
                return window != null && window.Visible && !window.IsHiddenToTray;
            }

            return browser != null && browser.IsVisible;
        }

        private void ToggleWindow()
        {
            if (activeEngine == "webview2")
            {
                if (window != null)
                {
                    window.ToggleFromTray();
                }
            }
            else if (browser != null)
            {
                if (!browser.Toggle())
                {
                    Notify("找不到浏览器窗口", "窗口可能已被关闭。右键托盘选「重启服务」或切换引擎。", ToolTipIcon.Info);
                }
            }

            RefreshTrayChecks();
        }

        private void OpenInDefaultBrowser()
        {
            if (state.ServerUrl != null && state.ServerUrl.Length > 0)
            {
                OpenPath(state.ServerUrl);
            }
        }

        private static void OpenPath(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception)
            {
            }
        }

        // ---------------------------------------------------------------- 热键

        private void RegisterHotkey()
        {
            if (string.IsNullOrEmpty(config.GlobalHotkey))
            {
                return;
            }

            uint modifiers;
            uint key;
            if (!TryParseHotkey(config.GlobalHotkey, out modifiers, out key))
            {
                return;
            }

            hotkeyWindow = new MessageWindow();
            hotkeyWindow.HotkeyPressed += delegate { ToggleWindow(); };
            Native.RegisterHotKey(hotkeyWindow.Handle, HotkeyId, modifiers, key);
        }

        private static bool TryParseHotkey(string text, out uint modifiers, out uint key)
        {
            modifiers = 0;
            key = 0;

            string[] parts = text.Split('+');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Length == 0)
                {
                    continue;
                }

                string upper = part.ToUpperInvariant();
                if (upper == "CTRL" || upper == "CONTROL")
                {
                    modifiers |= Native.MOD_CONTROL;
                }
                else if (upper == "ALT")
                {
                    modifiers |= Native.MOD_ALT;
                }
                else if (upper == "SHIFT")
                {
                    modifiers |= Native.MOD_SHIFT;
                }
                else if (part.Length == 1)
                {
                    key = (uint)char.ToUpperInvariant(part[0]);
                }
                else if (upper.Length >= 2 && upper[0] == 'F')
                {
                    int number;
                    if (int.TryParse(upper.Substring(1), out number) && number >= 1 && number <= 24)
                    {
                        key = (uint)(0x70 + number - 1);
                    }
                }
            }

            return modifiers != 0 && key != 0;
        }

        // ---------------------------------------------------------------- 开机自启

        private bool IsAutoStartEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null)
                    {
                        return false;
                    }

                    object value = key.GetValue(RunValueName);
                    return value != null;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void ToggleAutoStart()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null)
                    {
                        return;
                    }

                    if (IsAutoStartEnabled())
                    {
                        key.DeleteValue(RunValueName, false);
                        config.AutoStart = false;
                    }
                    else
                    {
                        string exe = Application.ExecutablePath;
                        key.SetValue(RunValueName, "\"" + exe + "\" --minimized");
                        config.AutoStart = true;
                    }
                }

                config.Save(paths.ConfigPath);
            }
            catch (Exception error)
            {
                Notify("设置开机自启失败", error.Message, ToolTipIcon.Error);
            }

            RefreshTrayChecks();
        }

        // ---------------------------------------------------------------- 引擎切换与退出

        private void SwitchEngine(string engine)
        {
            config.Host = engine;
            config.Save(paths.ConfigPath);
            Notify("宿主引擎已改为 " + engine, "外壳即将重启以生效。", ToolTipIcon.Info);

            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true });
            }
            catch (Exception)
            {
            }

            Quit(true);
        }

        /// <summary>托盘「退出」：直接退出，不弹询问。</summary>
        private void RequestQuitFromTray()
        {
            Quit(false);
        }

        /// <summary>退出外壳。stopServer 为 true 时一定停服务。</summary>
        public void Quit(bool forceStopServer)
        {
            if (quitting)
            {
                return;
            }

            quitting = true;
            ShellLog.Write("开始退出外壳 (forceStopServer=" + forceStopServer + " quitStopsServer=" + config.QuitStopsServer + ")");

            if (window != null)
            {
                window.CaptureBounds();
            }

            config.Save(paths.ConfigPath);

            if (forceStopServer || config.QuitStopsServer)
            {
                if (server != null)
                {
                    server.Stop();
                }
            }

            if (browser != null)
            {
                browser.Stop();
            }

            if (hotkeyWindow != null)
            {
                try
                {
                    Native.UnregisterHotKey(hotkeyWindow.Handle, HotkeyId);
                }
                catch (Exception)
                {
                }

                hotkeyWindow.DestroyHandle();
                hotkeyWindow = null;
            }

            if (tray != null)
            {
                tray.Visible = false;
                tray.Dispose();
                tray = null;
            }

            if (window != null)
            {
                window.ReallyClose();
                window.Dispose();
                window = null;
            }

            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && server != null)
            {
                server.Stop();
            }

            base.Dispose(disposing);
        }

        /// <summary>只用来接收全局热键消息的隐藏窗口。</summary>
        private sealed class MessageWindow : NativeWindow
        {
            public event Action HotkeyPressed;

            public MessageWindow()
            {
                CreateParams parameters = new CreateParams();
                parameters.Caption = "DshShellHotkeyWindow";
                parameters.X = 0;
                parameters.Y = 0;
                parameters.Width = 1;
                parameters.Height = 1;
                CreateHandle(parameters);
            }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == Native.WM_HOTKEY)
                {
                    Action handler = HotkeyPressed;
                    if (handler != null)
                    {
                        handler();
                    }
                }

                base.WndProc(ref message);
            }
        }
    }
}











