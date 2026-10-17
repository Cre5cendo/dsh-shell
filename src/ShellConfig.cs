using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;

namespace DshShell
{
    /// <summary>外壳使用的路径集合：全部相对于 exe 所在目录，方便整体搬移。</summary>
    internal sealed class ShellPaths
    {
        public readonly string RootDir;
        public readonly string ConfigPath;
        public readonly string DataDir;
        public readonly string StatePath;
        public readonly string LogDir;
        public readonly string AssetDir;
        public readonly string IconPath;
        public readonly string WebViewProfileDir;
        public readonly string ServerLauncherPath;

        public ShellPaths()
        {
            RootDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            ConfigPath = Path.Combine(RootDir, "config.json");
            DataDir = Path.Combine(RootDir, "data");
            StatePath = Path.Combine(DataDir, "state.json");
            LogDir = Path.Combine(DataDir, "logs");
            AssetDir = Path.Combine(RootDir, "assets");
            IconPath = Path.Combine(AssetDir, "icon.ico");
            WebViewProfileDir = Path.Combine(DataDir, "profile", "webview2");
            ServerLauncherPath = Path.Combine(DataDir, "launch-server.cmd");

            EnsureDirectory(DataDir);
            EnsureDirectory(LogDir);
            EnsureDirectory(AssetDir);
        }

        public string BrowserProfileDir(string engine)
        {
            return Path.Combine(DataDir, "profile", engine);
        }

        /// <summary>
        /// 每次启动服务都用一个新的日志文件：既不会被上一轮残留进程占住，
        /// 也保证「日志里出现的 dsh web: URL」一定属于本轮启动的服务。
        /// </summary>
        public string CreateServerLogPath()
        {
            string name = "server-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log";
            return Path.Combine(LogDir, name);
        }

        private static void EnsureDirectory(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
            }
            catch (Exception)
            {
            }
        }
    }

    /// <summary>config.json 的模型。字段用公开字段，便于 JavaScriptSerializer 直接读写。</summary>
    internal sealed class ShellConfig
    {
        /// <summary>宿主引擎：auto | webview2 | edge | chrome。</summary>
        public string Host = "auto";

        /// <summary>DSH 服务端口；0 表示让系统随机分配。</summary>
        public int Port = 0;

        /// <summary>自定义 dsh 启动命令；留空则自动探测（PATH → npx 缓存 → npx）。</summary>
        public string DshCommand = "";

        /// <summary>点关闭时的行为：ask | tray | quit。</summary>
        public string CloseAction = "tray";

        /// <summary>退出外壳时是否顺带停掉自己拉起的 DSH 服务。</summary>
        public bool QuitStopsServer = true;

        /// <summary>最小化时隐藏到托盘而不是留在任务栏。</summary>
        public bool MinimizeToTray = false;

        /// <summary>DSH 服务意外退出时是否自动重启。</summary>
        public bool AutoRestartServer = true;

        /// <summary>全局热键（显示/隐藏窗口），例如 Ctrl+Alt+D；留空禁用。</summary>
        public string GlobalHotkey = "Ctrl+Alt+D";

        /// <summary>启动后直接进托盘，不弹窗口（配合开机自启）。</summary>
        public bool StartMinimized = false;

        /// <summary>开机自启（写 HKCU Run）。由托盘菜单切换。</summary>
        public bool AutoStart = false;

        public int WindowWidth = 1280;
        public int WindowHeight = 860;
        public int WindowX = -1;
        public int WindowY = -1;
        public bool WindowMaximized = false;

        /// <summary>WebView2 打开开发者工具（调试用）。</summary>
        public bool DevTools = false;

        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        public static ShellConfig Load(string path)
        {
            ShellConfig config = new ShellConfig();
            try
            {
                if (File.Exists(path))
                {
                    Dictionary<string, object> map = Serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                    if (map != null)
                    {
                        config.Apply(map);
                    }
                }
                else
                {
                    config.Save(path);
                }
            }
            catch (Exception)
            {
            }

            return config;
        }

        private void Apply(Dictionary<string, object> rawMap)
        {
            // 键名大小写不敏感，避免用户手改 config.json 时因为 HOST/host 之类差异静默失效。
            Dictionary<string, object> map = new Dictionary<string, object>(rawMap, StringComparer.OrdinalIgnoreCase);
            Host = ReadString(map, "Host", Host);
            Port = ReadInt(map, "Port", Port);
            DshCommand = ReadString(map, "DshCommand", DshCommand);
            CloseAction = ReadString(map, "CloseAction", CloseAction);
            QuitStopsServer = ReadBool(map, "QuitStopsServer", QuitStopsServer);
            MinimizeToTray = ReadBool(map, "MinimizeToTray", MinimizeToTray);
            AutoRestartServer = ReadBool(map, "AutoRestartServer", AutoRestartServer);
            GlobalHotkey = ReadString(map, "GlobalHotkey", GlobalHotkey);
            StartMinimized = ReadBool(map, "StartMinimized", StartMinimized);
            AutoStart = ReadBool(map, "AutoStart", AutoStart);
            WindowWidth = ReadInt(map, "WindowWidth", WindowWidth);
            WindowHeight = ReadInt(map, "WindowHeight", WindowHeight);
            WindowX = ReadInt(map, "WindowX", WindowX);
            WindowY = ReadInt(map, "WindowY", WindowY);
            WindowMaximized = ReadBool(map, "WindowMaximized", WindowMaximized);
            DevTools = ReadBool(map, "DevTools", DevTools);

            if (Host != "webview2" && Host != "edge" && Host != "chrome")
            {
                Host = "auto";
            }

            if (CloseAction != "ask" && CloseAction != "tray" && CloseAction != "quit")
            {
                CloseAction = "ask";
            }

            if (WindowWidth < 640)
            {
                WindowWidth = 640;
            }

            if (WindowHeight < 480)
            {
                WindowHeight = 480;
            }
        }

        public void Save(string path)
        {
            try
            {
                File.WriteAllText(path, Serializer.Serialize(this).Replace(",", ", " + Environment.NewLine));
            }
            catch (Exception)
            {
            }
        }

        private static string ReadString(Dictionary<string, object> map, string key, string fallback)
        {
            object value;
            if (map.TryGetValue(key, out value) && value != null)
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            return fallback;
        }

        private static int ReadInt(Dictionary<string, object> map, string key, int fallback)
        {
            object value;
            if (map.TryGetValue(key, out value) && value != null)
            {
                try
                {
                    return Convert.ToInt32(value, CultureInfo.InvariantCulture);
                }
                catch (Exception)
                {
                    return fallback;
                }
            }

            return fallback;
        }

        private static bool ReadBool(Dictionary<string, object> map, string key, bool fallback)
        {
            object value;
            if (map.TryGetValue(key, out value) && value != null)
            {
                try
                {
                    return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                }
                catch (Exception)
                {
                    return fallback;
                }
            }

            return fallback;
        }
    }

    /// <summary>跨进程状态：当前托管服务与浏览器窗口的信息。</summary>
    internal sealed class ShellState
    {
        public int ServerPid = 0;
        public int ServerPort = 0;
        public string ServerUrl = "";
        public string StartedAt = "";
        public string Host = "";

        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        public static ShellState Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    ShellState state = Serializer.Deserialize<ShellState>(File.ReadAllText(path));
                    if (state != null)
                    {
                        return state;
                    }
                }
            }
            catch (Exception)
            {
            }

            return new ShellState();
        }

        public void Save(string path)
        {
            try
            {
                File.WriteAllText(path, Serializer.Serialize(this));
            }
            catch (Exception)
            {
            }
        }
    }
}


