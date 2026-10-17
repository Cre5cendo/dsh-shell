using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DshShell
{
    /// <summary>无边框自绘窗口 + WebView2（独立 profile）。窗口归本进程所有，因此任务栏图标与分组都是 DSH 自己的。</summary>
    internal sealed class ShellWindow : Form
    {
        private const int ResizeBorder = 7;
        private const int TitleBarHeight = 34;

        // 与 DSH 界面一致的深色底
        private static readonly Color SurfaceBack = Color.FromArgb(24, 25, 28);

        // 标题栏底色（近黑）
        private static readonly Color TitleBarBack = Color.FromArgb(16, 17, 19);

        private readonly ShellConfig config;
        private readonly ShellPaths paths;

        private Panel contentPanel;
        private Image titleIconImage;
        private Font titleFont;
        private CaptionButton minimizeButton;
        private CaptionButton maximizeButton;
        private CaptionButton closeButton;
        private WebView2 webView;

        private bool webViewReady;
        private string pendingUrl;
        private string pendingError;
        private bool reallyClosing;
        private bool closeDialogOpen;

        /// <summary>用户确认要退出外壳时触发。</summary>
        public event Action QuitRequested;

        public ShellWindow(ShellConfig config, ShellPaths paths)
        {
            this.config = config;
            this.paths = paths;

            SuspendLayout();

            Text = "DeepSeek Harness";
            // 用系统默认标题栏：原生最小化/最大化/关闭、贴靠布局、右键系统菜单都回来了
            FormBorderStyle = FormBorderStyle.Sizable;
            BackColor = Color.FromArgb(24, 25, 28);
            MinimumSize = new Size(720, 520);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            KeyPreview = true;

            if (config.WindowX >= 0 && config.WindowY >= 0)
            {
                StartPosition = FormStartPosition.Manual;
                Location = new Point(config.WindowX, config.WindowY);
                Size = new Size(config.WindowWidth, config.WindowHeight);
            }
            else
            {
                StartPosition = FormStartPosition.CenterScreen;
                Size = new Size(config.WindowWidth, config.WindowHeight);
            }

            if (File.Exists(paths.IconPath))
            {
                try
                {
                    Icon = new Icon(paths.IconPath);
                }
                catch (Exception)
                {
                }
            }

            BuildWebView();

            ResumeLayout(false);

            Load += OnLoaded;
            Resize += OnResized;
        }

        /// <summary>由外壳应用在服务就绪后调用。可能来自后台线程，因此统一切回 UI 线程。</summary>
        public void NavigateTo(string url)
        {
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke((MethodInvoker)delegate { NavigateTo(url); });
                }
                catch (Exception)
                {
                }

                return;
            }

            pendingUrl = url;
            pendingError = null;

            if (webViewReady && webView != null && webView.CoreWebView2 != null)
            {
                ShellLog.Write("窗口开始加载 " + url);
                webView.CoreWebView2.Navigate(url);
            }
        }

        /// <summary>服务启动失败时展示错误页。</summary>
        public void ShowError(string message)
        {
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke((MethodInvoker)delegate { ShowError(message); });
                }
                catch (Exception)
                {
                }

                return;
            }

            pendingError = message;
            if (webViewReady && webView != null && webView.CoreWebView2 != null)
            {
                webView.CoreWebView2.NavigateToString(BuildErrorPage(message));
            }
        }

        /// <summary>把当前窗口尺寸写回配置。</summary>
        public void CaptureBounds()
        {
            try
            {
                if (WindowState == FormWindowState.Normal)
                {
                    config.WindowWidth = Width;
                    config.WindowHeight = Height;
                    config.WindowX = Location.X;
                    config.WindowY = Location.Y;
                }

                config.WindowMaximized = WindowState == FormWindowState.Maximized;
            }
            catch (Exception)
            {
            }
        }

        /// <summary>真正关闭窗口（退出流程用），不再弹询问。</summary>
        public void ReallyClose()
        {
            reallyClosing = true;
            try
            {
                Close();
            }
            catch (Exception)
            {
            }
        }

        /// <summary>窗口被收进托盘时触发。</summary>
        public event Action HiddenToTray;

        public void HideToTray()
        {
            Hide();
            ShowInTaskbar = false;
            ShellLog.Write("窗口已隐藏到托盘");

            Action handler = HiddenToTray;
            if (handler != null)
            {
                handler();
            }
        }

        public void RestoreFromTray()
        {
            ShellLog.Write("窗口从托盘恢复");
            ShowInTaskbar = true;
            if (!Visible)
            {
                Show();
            }

            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }

            Activate();
            BringToFront();
            Native.SetForegroundWindow(Handle);
        }

        public void ToggleFromTray()
        {
            if (Visible && WindowState != FormWindowState.Minimized)
            {
                HideToTray();
            }
            else
            {
                RestoreFromTray();
            }
        }

        public bool IsHiddenToTray
        {
            get { return !Visible || !ShowInTaskbar; }
        }

        public void ReloadWebView()
        {
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke((MethodInvoker)delegate { ReloadWebView(); });
                }
                catch (Exception)
                {
                }

                return;
            }

            if (webViewReady && webView != null && webView.CoreWebView2 != null)
            {
                webView.CoreWebView2.Reload();
            }
        }

        /// <summary>
        /// 只创建三个标题栏按钮；标题栏本身画在窗体上（不建子控件）。
        /// 这样窗口才能在 WM_NCHITTEST 里把标题栏区域报成 HTCAPTION，
        /// 拖动与「双击最大化」交给系统原生处理。
        /// </summary>
        private void BuildCaptionButtons()
        {
            closeButton = new CaptionButton(CaptionGlyph.Close);
            closeButton.Click += delegate { HandleUserClose(); };
            Controls.Add(closeButton);

            maximizeButton = new CaptionButton(CaptionGlyph.Maximize);
            maximizeButton.Click += delegate { ToggleMaximize(); };
            Controls.Add(maximizeButton);

            minimizeButton = new CaptionButton(CaptionGlyph.Minimize);
            minimizeButton.Click += delegate { MinimizeOrHide(); };
            Controls.Add(minimizeButton);

            if (File.Exists(paths.IconPath))
            {
                try
                {
                    titleIconImage = new Icon(paths.IconPath, new Size(18, 18)).ToBitmap();
                }
                catch (Exception)
                {
                }
            }

            titleFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular);
            LayoutCaptionButtons();
        }

        private void LayoutCaptionButtons()
        {
            if (closeButton == null)
            {
                return;
            }

            int buttonWidth = 44;
            closeButton.Size = new Size(buttonWidth, TitleBarHeight);
            maximizeButton.Size = new Size(buttonWidth, TitleBarHeight);
            minimizeButton.Size = new Size(buttonWidth, TitleBarHeight);

            int right = ClientSize.Width;
            closeButton.Location = new Point(right - buttonWidth, 0);
            maximizeButton.Location = new Point(closeButton.Left - buttonWidth, 0);
            minimizeButton.Location = new Point(maximizeButton.Left - buttonWidth, 0);
        }

        private enum CaptionGlyph
        {
            Minimize,
            Maximize,
            Restore,
            Close
        }

        /// <summary>自绘标题栏按钮：悬停显示圆角高亮，关闭键悬停变红。</summary>
        private sealed class CaptionButton : Control
        {
            private CaptionGlyph glyph;
            private bool hovering;

            public CaptionButton(CaptionGlyph glyph)
            {
                this.glyph = glyph;
                Cursor = Cursors.Hand;
                TabStop = false;
                SetStyle(
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor,
                    true);
                BackColor = TitleBarBack;
            }

            public void SetGlyph(CaptionGlyph value)
            {
                glyph = value;
                Invalidate();
            }

            protected override void OnMouseEnter(EventArgs args)
            {
                hovering = true;
                Invalidate();
                base.OnMouseEnter(args);
            }

            protected override void OnMouseLeave(EventArgs args)
            {
                hovering = false;
                Invalidate();
                base.OnMouseLeave(args);
            }

            protected override void OnPaint(PaintEventArgs args)
            {
                Graphics graphics = args.Graphics;
                // 先把整块底色铺满，圆角以外才不会露出白色默认底
                graphics.Clear(TitleBarBack);
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                if (hovering)
                {
                    Color fill = glyph == CaptionGlyph.Close
                        ? Color.FromArgb(232, 78, 78)
                        : Color.FromArgb(60, 255, 255, 255);
                    using (GraphicsPath path = new GraphicsPath())
                    {
                        int radius = 7;
                        path.AddArc(4, 4, radius * 2, radius * 2, 180, 90);
                        path.AddArc(Width - radius * 2 - 5, 4, radius * 2, radius * 2, 270, 90);
                        path.AddArc(Width - radius * 2 - 5, Height - radius * 2 - 5, radius * 2, radius * 2, 0, 90);
                        path.AddArc(4, Height - radius * 2 - 5, radius * 2, radius * 2, 90, 90);
                        path.CloseFigure();
                        using (SolidBrush brush = new SolidBrush(fill))
                        {
                            graphics.FillPath(brush, path);
                        }
                    }
                }

                using (Pen pen = new Pen(Color.White, 1.2f))
                {
                    float centerX = Width / 2f;
                    float centerY = Height / 2f;

                    if (glyph == CaptionGlyph.Minimize)
                    {
                        graphics.DrawLine(pen, centerX - 5f, centerY + 4f, centerX + 5f, centerY + 4f);
                    }
                    else if (glyph == CaptionGlyph.Maximize)
                    {
                        graphics.DrawRectangle(pen, centerX - 5f, centerY - 5f, 10f, 10f);
                    }
                    else if (glyph == CaptionGlyph.Restore)
                    {
                        graphics.DrawRectangle(pen, centerX - 3f, centerY - 6f, 9f, 9f);
                        graphics.DrawRectangle(pen, centerX - 6f, centerY - 3f, 9f, 9f);
                    }
                    else
                    {
                        graphics.DrawLine(pen, centerX - 5f, centerY - 5f, centerX + 5f, centerY + 5f);
                        graphics.DrawLine(pen, centerX + 5f, centerY - 5f, centerX - 5f, centerY + 5f);
                    }
                }
            }
        }

        private void BuildWebView()
        {
            contentPanel = new Panel();
            contentPanel.BackColor = SurfaceBack;
            Controls.Add(contentPanel);

            webView = new WebView2();
            webView.Dock = DockStyle.Fill;
            webView.DefaultBackgroundColor = SurfaceBack;
            contentPanel.Controls.Add(webView);

            LayoutContent();
        }

        /// <summary>内容面板铺满客户区（标题栏由系统绘制，客户区本来就已排除它）。</summary>
        private void LayoutContent()
        {
            if (contentPanel == null)
            {
                return;
            }

            contentPanel.Bounds = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
        }

        protected override void OnActivated(EventArgs args)
        {
            base.OnActivated(args);
            // 激活/最大化还原后系统可能重画标题栏，这里补一次，保证始终是深色
            ApplyDarkTitleBar();
        }

        protected override void OnHandleCreated(EventArgs args)
        {
            base.OnHandleCreated(args);
            // 必须在窗口显示前就设置，否则 DWM 深色标题栏不生效
            ApplyDarkTitleBar();
        }

        private void OnLoaded(object sender, EventArgs args)
        {
            ShellLog.Write("窗口 Loaded，开始初始化 WebView2（profile=" + paths.WebViewProfileDir + "）");
            ApplyDarkTitleBar();
            if (config.WindowMaximized)
            {
                MaximizedBounds = Screen.FromControl(this).WorkingArea;
                WindowState = FormWindowState.Maximized;
            }

            InitializeWebView();
        }

        private void OnResized(object sender, EventArgs args)
        {
            LayoutContent();

            if (WindowState == FormWindowState.Minimized && config.MinimizeToTray)
            {
                HideToTray();
            }
        }

        private void ApplyDarkTitleBar()
        {
            try
            {
                int enabled = 1;
                Native.DwmSetWindowAttribute(Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int));

                // 旧版 Windows 的属性号是 19，两处都设一遍以保证黑标题栏生效
                Native.DwmSetWindowAttribute(Handle, 19, ref enabled, sizeof(int));
                Native.SetWindowTheme(Handle, "DarkMode_Explorer", null);

                // Win11：让系统给窗口加圆角（最大化时系统会自动取消）
                int round = Native.DWMWCP_ROUND;
                Native.DwmSetWindowAttribute(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            }
            catch (Exception)
            {
            }
        }

        private void InitializeWebView()
        {
            webView.CoreWebView2InitializationCompleted += OnCoreWebViewInitialized;

            try
            {
                System.Threading.Tasks.Task<CoreWebView2Environment> task =
                    CoreWebView2Environment.CreateAsync(null, paths.WebViewProfileDir, null);
                task.ContinueWith(delegate(System.Threading.Tasks.Task<CoreWebView2Environment> completed)
                {
                    if (completed.IsFaulted || completed.IsCanceled)
                    {
                        string message = completed.Exception == null ? "未知错误" : completed.Exception.GetBaseException().Message;
                        BeginInvoke((MethodInvoker)delegate { ShowInitFailure("创建 WebView2 环境失败：" + message); });
                        return;
                    }

                    CoreWebView2Environment environment = completed.Result;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        try
                        {
                            webView.EnsureCoreWebView2Async(environment);
                        }
                        catch (Exception error)
                        {
                            ShowInitFailure("初始化 WebView2 失败：" + error.Message);
                        }
                    });
                }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
            }
            catch (Exception error)
            {
                ShowInitFailure("初始化 WebView2 失败：" + error.Message);
            }
        }

        private void OnCoreWebViewInitialized(object sender, CoreWebView2InitializationCompletedEventArgs args)
        {
            if (args != null && args.IsSuccess && webView.CoreWebView2 != null)
            {
                CoreWebView2 core = webView.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = true;
                core.Settings.AreDevToolsEnabled = config.DevTools;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsZoomControlEnabled = true;
                core.NewWindowRequested += OnNewWindowRequested;
                core.ProcessFailed += OnProcessFailed;

                webViewReady = true;
                ShellLog.Write("WebView2 就绪");

                if (pendingError != null)
                {
                    core.NavigateToString(BuildErrorPage(pendingError));
                }
                else if (pendingUrl != null)
                {
                    core.Navigate(pendingUrl);
                }
                else
                {
                    core.NavigateToString(BuildLoadingPage("正在启动 DSH 服务…"));
                }

                return;
            }

            string reason = args == null || args.InitializationException == null
                ? "未知原因"
                : args.InitializationException.Message;
            ShowInitFailure("WebView2 初始化未成功：" + reason);
        }

        private void OnProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs args)
        {
            try
            {
                webView.CoreWebView2.Reload();
            }
            catch (Exception)
            {
            }
        }

        private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs args)
        {
            args.Handled = true;
            try
            {
                Process.Start(args.Uri);
            }
            catch (Exception)
            {
            }
        }

        private void ShowInitFailure(string message)
        {
            ShellLog.Write("WebView2 初始化失败: " + message);
            if (webViewReady && webView != null && webView.CoreWebView2 != null)
            {
                webView.CoreWebView2.NavigateToString(BuildErrorPage(message));
            }
            else
            {
                MessageBox.Show(message, "DeepSeek Harness 外壳", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ToggleMaximize()
        {
            if (WindowState == FormWindowState.Maximized)
            {
                WindowState = FormWindowState.Normal;
                maximizeButton.SetGlyph(CaptionGlyph.Maximize);
            }
            else
            {
                MaximizedBounds = Screen.FromControl(this).WorkingArea;
                WindowState = FormWindowState.Maximized;
                maximizeButton.SetGlyph(CaptionGlyph.Restore);
            }
        }

        private void MinimizeOrHide()
        {
            if (config.MinimizeToTray)
            {
                HideToTray();
                return;
            }

            WindowState = FormWindowState.Minimized;
        }

        /// <summary>关闭按钮 / Alt+F4 / 托盘退出统一走这里。</summary>
        public void HandleUserClose()
        {
            if (closeDialogOpen)
            {
                return;
            }

            if (config.CloseAction == "quit")
            {
                RaiseQuitRequested();
                return;
            }

            if (config.CloseAction == "tray")
            {
                HideToTray();
                return;
            }

            closeDialogOpen = true;
            int choice;
            try
            {
                choice = CloseDialog.Ask(this, config.QuitStopsServer);
            }
            finally
            {
                closeDialogOpen = false;
            }

            if (choice == CloseDialog.ChoiceTray)
            {
                HideToTray();
            }
            else if (choice == CloseDialog.ChoiceQuit)
            {
                RaiseQuitRequested();
            }
        }

        private void RaiseQuitRequested()
        {
            Action handler = QuitRequested;
            if (handler != null)
            {
                handler();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs args)
        {
            // 除「我们主动退出」和「系统关机」之外，一律拦下来走询问流程。
            // （早先只拦 CloseReason.UserClosing，导致 Alt+F4 / 程序化 WM_CLOSE 会直接关掉窗口。）
            if (!reallyClosing && args.CloseReason != CloseReason.WindowsShutDown)
            {
                args.Cancel = true;
                HandleUserClose();
                return;
            }

            base.OnFormClosing(args);
        }

        // 用系统默认标题栏后，命中测试、拖动、双击最大化、贴靠布局全部由系统负责，
        // 这里不再需要任何自定义 WndProc 逻辑。

        private static string BuildLoadingPage(string text)
        {
            StringBuilder html = new StringBuilder();
            html.Append("<!doctype html><html><head><meta charset=\"utf-8\"><style>");
            html.Append("html,body{margin:0;height:100%;background:#18191c;color:#c9ccd2;");
            html.Append("font-family:'Microsoft YaHei UI',Segoe UI,sans-serif;}");
            html.Append(".wrap{height:100%;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:14px}");
            html.Append(".dot{width:34px;height:34px;border-radius:50%;border:3px solid #3a3d44;border-top-color:#4d6bfe;animation:s 1s linear infinite}");
            html.Append("@keyframes s{to{transform:rotate(360deg)}}");
            html.Append(".t{font-size:14px}.h{font-size:12px;color:#7d838d}");
            html.Append("</style></head><body><div class=\"wrap\"><div class=\"dot\"></div><div class=\"t\">");
            html.Append(Escape(text));
            html.Append("</div><div class=\"h\">首次启动可能需要十几秒</div></div></body></html>");
            return html.ToString();
        }

        private static string BuildErrorPage(string message)
        {
            StringBuilder html = new StringBuilder();
            html.Append("<!doctype html><html><head><meta charset=\"utf-8\"><style>");
            html.Append("html,body{margin:0;height:100%;background:#18191c;color:#e6e8ec;");
            html.Append("font-family:'Microsoft YaHei UI',Segoe UI,sans-serif;font-size:13px}");
            html.Append(".wrap{padding:28px 32px;height:100%;box-sizing:border-box;display:flex;flex-direction:column;gap:12px}");
            html.Append("h2{margin:0;font-size:16px;color:#ff8a80}");
            html.Append("pre{flex:1;overflow:auto;background:#101114;border:1px solid #2a2c31;border-radius:8px;padding:12px;");
            html.Append("white-space:pre-wrap;color:#b9bec7;font-family:Consolas,monospace;font-size:12px}");
            html.Append("</style></head><body><div class=\"wrap\"><h2>DSH 服务没有正常启动</h2>");
            html.Append("<div>可以右键托盘图标选「重启 DSH 服务」重试；下面是服务日志的尾部。</div><pre>");
            html.Append(Escape(message));
            html.Append("</pre></div></body></html>");
            return html.ToString();
        }

        private static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}


