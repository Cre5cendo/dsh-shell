using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace DshShell
{
    /// <summary>用系统已装的 Edge/Chrome 以 --app 模式开一个独立窗口（独立 user-data-dir，不受其它浏览器影响）。</summary>
    /// <remarks>
    /// 这条路线无法拦截窗口的关闭按钮（跨进程做不到），所以关闭询问退化为：
    /// 点 X 只关窗口、服务与托盘继续存活，真正的「退出」收进托盘菜单并弹自定义询问。
    /// </remarks>
    internal sealed class BrowserHost
    {
        private const string ChromeWindowClass = "Chrome_WidgetWin_1";

        private readonly ShellPaths paths;
        private Process process;
        private string engine;

        public BrowserHost(ShellPaths paths)
        {
            this.paths = paths;
        }

        public string Engine
        {
            get { return engine; }
        }

        public int ProcessId
        {
            get
            {
                try
                {
                    return process == null ? 0 : process.Id;
                }
                catch (Exception)
                {
                    return 0;
                }
            }
        }

        /// <summary>解析要使用的浏览器；返回 null 表示一个都没找到。</summary>
        public static string ResolveEngine(string requested)
        {
            if (requested == "edge" || requested == "chrome")
            {
                return ResolveExe(requested) == null ? null : requested;
            }

            if (ResolveExe("edge") != null)
            {
                return "edge";
            }

            return ResolveExe("chrome") != null ? "chrome" : null;
        }

        public static string ResolveExe(string engine)
        {
            List<string> candidates = new List<string>();
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            if (engine == "edge")
            {
                candidates.Add(Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"));
                candidates.Add(Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"));
                candidates.Add(Path.Combine(local, "Microsoft", "Edge", "Application", "msedge.exe"));
            }
            else
            {
                candidates.Add(Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"));
                candidates.Add(Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"));
                candidates.Add(Path.Combine(local, "Google", "Chrome", "Application", "chrome.exe"));
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                if (File.Exists(candidates[i]))
                {
                    return candidates[i];
                }
            }

            return null;
        }

        /// <summary>打开独立窗口；返回找到的窗口句柄，失败返回 IntPtr.Zero。</summary>
        public IntPtr Start(string requestedEngine, string url, int width, int height)
        {
            engine = ResolveEngine(requestedEngine);
            if (engine == null)
            {
                return IntPtr.Zero;
            }

            string exe = ResolveExe(engine);
            string profile = paths.BrowserProfileDir(engine);
            try
            {
                Directory.CreateDirectory(profile);
            }
            catch (Exception)
            {
            }

            List<IntPtr> before = FindChromeWindows(null);

            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = exe;
            info.Arguments =
                "--app=" + Quote(url) +
                " --user-data-dir=" + Quote(profile) +
                " --no-first-run --no-default-browser-check --hide-crash-restore-bubble" +
                " --disable-features=Translate,AutofillServerCommunication" +
                " --window-size=" + width.ToString() + "," + height.ToString();
            info.UseShellExecute = false;
            info.WorkingDirectory = paths.RootDir;

            try
            {
                process = Process.Start(info);
            }
            catch (Exception)
            {
                return IntPtr.Zero;
            }

            if (process == null)
            {
                return IntPtr.Zero;
            }

            IntPtr window = WaitForWindow(before, process.Id, 20000);
            return window;
        }

        public bool IsAlive
        {
            get
            {
                try
                {
                    return process != null && !process.HasExited;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        public bool HasWindow
        {
            get { return FindChromeWindows(process == null ? (int?)null : ProcessId).Count > 0; }
        }

        public bool Show()
        {
            IntPtr window = PrimaryWindow();
            if (window == IntPtr.Zero)
            {
                return false;
            }

            Native.ShowWindow(window, Native.SW_RESTORE);
            Native.ShowWindow(window, Native.SW_SHOW);
            Native.SetForegroundWindow(window);
            return true;
        }

        public bool Hide()
        {
            IntPtr window = PrimaryWindow();
            if (window == IntPtr.Zero)
            {
                return false;
            }

            Native.ShowWindow(window, Native.SW_HIDE);
            return true;
        }

        public bool IsVisible
        {
            get
            {
                IntPtr window = PrimaryWindow();
                return window != IntPtr.Zero && Native.IsWindowVisible(window) && !Native.IsIconic(window);
            }
        }

        public bool Toggle()
        {
            if (IsVisible)
            {
                return Hide();
            }

            return Show();
        }

        /// <summary>关掉浏览器窗口（服务继续运行）。</summary>
        public void CloseWindow()
        {
            IntPtr window = PrimaryWindow();
            if (window != IntPtr.Zero)
            {
                Native.PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
            }
        }

        public void Stop()
        {
            try
            {
                if (process != null && !process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (Exception)
            {
            }
        }

        private IntPtr PrimaryWindow()
        {
            List<IntPtr> windows = FindChromeWindows(ProcessId);
            if (windows.Count == 0)
            {
                windows = FindChromeWindows(null);
            }

            return windows.Count > 0 ? windows[0] : IntPtr.Zero;
        }

        private IntPtr WaitForWindow(List<IntPtr> before, int pid, int timeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                List<IntPtr> now = FindChromeWindows(null);
                List<IntPtr> mine = FindChromeWindows(pid);
                if (mine.Count > 0)
                {
                    return mine[0];
                }

                for (int i = 0; i < now.Count; i++)
                {
                    if (!before.Contains(now[i]))
                    {
                        return now[i];
                    }
                }

                Thread.Sleep(250);
            }

            return IntPtr.Zero;
        }

        /// <summary>枚举顶层浏览器窗口；传入 pid 则只保留属于该进程的。</summary>
        private static List<IntPtr> FindChromeWindows(int? pid)
        {
            List<IntPtr> result = new List<IntPtr>();
            Native.EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                StringBuilder className = new StringBuilder(256);
                Native.GetClassName(hWnd, className, className.Capacity);
                if (className.ToString() != ChromeWindowClass)
                {
                    return true;
                }

                if (!Native.IsWindowVisible(hWnd))
                {
                    return true;
                }

                if (pid.HasValue)
                {
                    uint owner;
                    Native.GetWindowThreadProcessId(hWnd, out owner);
                    if (owner != (uint)pid.Value)
                    {
                        return true;
                    }
                }

                StringBuilder title = new StringBuilder(512);
                Native.GetWindowText(hWnd, title, title.Capacity);
                if (title.Length == 0)
                {
                    return true;
                }

                result.Add(hWnd);
                return true;
            }, IntPtr.Zero);

            return result;
        }

        private static string Quote(string value)
        {
            return "\"" + value + "\"";
        }
    }
}
