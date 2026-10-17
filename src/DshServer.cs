using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace DshShell
{
    /// <summary>托管一个由本外壳拉起的 DSH Web 服务进程。</summary>
    /// <remarks>
    /// 拉起方式刻意采用「cmd + 启动脚本 + 输出重定向到文件」而不是管道重定向：
    /// 一是拿得到一份可直接查看的服务日志，二是不依赖命名管道（受限环境下管道会被拒绝）。
    /// 带 token 的 URL 只在启动时打印一次，因此必须自己拉起服务才能拿到。
    /// </remarks>
    internal sealed class DshServer
    {
        private const string UrlPattern = @"dsh web:\s*(\S+)";

        private readonly ShellPaths paths;
        private readonly ShellConfig config;
        private readonly object gate = new object();

        private Process process;
        private bool stopping;
        private int generation;
        private string logPath;

        public event Action<string> UrlReady;
        public event Action<string> Failed;
        public event Action<int, bool> Exited;

        public DshServer(ShellPaths paths, ShellConfig config)
        {
            this.paths = paths;
            this.config = config;
        }

        public bool IsAlive
        {
            get
            {
                lock (gate)
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
        }

        public int ProcessId
        {
            get
            {
                lock (gate)
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
        }

        public string LogPath
        {
            get { return logPath; }
        }

        /// <summary>启动服务并异步等待 dsh web: URL。返回是否成功把进程拉起来。</summary>
        public bool Start()
        {
            string commandLine = ResolveCommandLine();
            if (commandLine == null)
            {
                RaiseFailed("找不到 dsh 命令：PATH 上没有 dsh，npx 缓存里也没有 @deepseek-ai/dsh。请在 config.json 的 DshCommand 里写死启动命令。");
                return false;
            }

            // 每轮一个独立日志：避免上一轮残留进程占住文件，也避免把旧 URL 当成本轮结果。
            logPath = paths.CreateServerLogPath();
            PruneOldLogs();

            try
            {
                File.WriteAllText(logPath, string.Empty, Encoding.UTF8);
            }
            catch (Exception)
            {
            }

            string launcher =
                "@echo off" + Environment.NewLine +
                "chcp 65001 > nul 2>&1" + Environment.NewLine +
                commandLine + " 1> " + Quote(logPath) + " 2>&1" + Environment.NewLine;

            try
            {
                File.WriteAllText(paths.ServerLauncherPath, launcher, new UTF8Encoding(false));
            }
            catch (Exception error)
            {
                RaiseFailed("写启动脚本失败：" + error.Message);
                return false;
            }

            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            info.Arguments = "/c " + Quote(paths.ServerLauncherPath);
            info.WorkingDirectory = paths.RootDir;
            info.UseShellExecute = false;
            info.CreateNoWindow = true;

            try
            {
                lock (gate)
                {
                    stopping = false;
                    generation = generation + 1;
                    process = new Process();
                    process.StartInfo = info;
                    process.EnableRaisingEvents = true;
                    process.Exited += OnProcessExited;
                    process.Start();
                    ShellLog.Write("已启动服务进程 cmd PID=" + process.Id + "  命令行: " + commandLine);
                }
            }
            catch (Exception error)
            {
                RaiseFailed("启动 DSH 服务失败：" + error.Message);
                return false;
            }

            int currentGeneration = generation;
            Thread worker = new Thread(delegate() { Watch(currentGeneration); });
            worker.IsBackground = true;
            worker.Name = "dsh-url-watch";
            worker.Start();
            return true;
        }

        /// <summary>只保留最近若干份服务日志。</summary>
        private void PruneOldLogs()
        {
            try
            {
                string[] files = Directory.GetFiles(paths.LogDir, "server-*.log");
                if (files.Length <= 12)
                {
                    return;
                }

                Array.Sort(files);
                for (int i = 0; i < files.Length - 12; i++)
                {
                    try
                    {
                        File.Delete(files[i]);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>停止服务（整棵进程树）。</summary>
        public void Stop()
        {
            int pid;
            lock (gate)
            {
                stopping = true;
                pid = ProcessId;
                process = null;
            }

            if (pid > 0)
            {
                KillTree(pid);
            }
        }

        /// <summary>服务日志末尾若干行，用于失败提示。</summary>
        public string Tail(int lines)
        {
            try
            {
                if (logPath == null || !File.Exists(logPath))
                {
                    return string.Empty;
                }

                string[] all = File.ReadAllLines(logPath);
                int start = all.Length > lines ? all.Length - lines : 0;
                StringBuilder builder = new StringBuilder();
                for (int i = start; i < all.Length; i++)
                {
                    builder.AppendLine(all[i]);
                }

                return builder.ToString().Trim();
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>等待 URL 出现的看门线程。</summary>
        private void Watch(int ownerGeneration)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(150);
            Regex regex = new Regex(UrlPattern, RegexOptions.IgnoreCase);

            while (DateTime.UtcNow < deadline)
            {
                if (ownerGeneration != generation)
                {
                    return;
                }

                string content = ReadLogQuietly();
                if (content.Length > 0)
                {
                    Match match = regex.Match(content);
                    if (match.Success)
                    {
                        string url = match.Groups[1].Value.Trim();
                        ShellLog.Write("从服务日志解析到 URL: " + url);
                        Action<string> handler = UrlReady;
                        if (handler != null)
                        {
                            handler(url);
                        }

                        return;
                    }
                }

                if (!IsAlive)
                {
                    string tail = Tail(25);
                    RaiseFailed("DSH 服务启动后立即退出。日志：" + Environment.NewLine + tail);
                    return;
                }

                Thread.Sleep(200);
            }

            RaiseFailed("等待 dsh web: URL 超时（150 秒）。日志：" + Environment.NewLine + Tail(25));
        }

        private string ReadLogQuietly()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (logPath == null || !File.Exists(logPath))
                    {
                        return string.Empty;
                    }

                    using (FileStream stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        return reader.ReadToEnd();
                    }
                }
                catch (IOException)
                {
                    Thread.Sleep(60);
                }
                catch (Exception)
                {
                    return string.Empty;
                }
            }

            return string.Empty;
        }

        private void OnProcessExited(object sender, EventArgs args)
        {
            int exitCode = 0;
            bool wasStopping;
            try
            {
                exitCode = process == null ? 0 : process.ExitCode;
            }
            catch (Exception)
            {
            }

            lock (gate)
            {
                wasStopping = stopping;
            }

            ShellLog.Write("服务进程退出 exitCode=" + exitCode + " stopping=" + wasStopping);

            Action<int, bool> handler = Exited;
            if (handler != null)
            {
                handler(exitCode, !wasStopping);
            }
        }

        /// <summary>把自动探测到的启动命令拼成一行 cmd 可执行的命令。</summary>
        private string ResolveCommandLine()
        {
            string portArgs = " web --no-open --port " + config.Port.ToString();

            if (!string.IsNullOrEmpty(config.DshCommand))
            {
                string custom = config.DshCommand.Trim();
                if (custom.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                {
                    return Quote(ResolveNode()) + " " + Quote(custom) + portArgs;
                }

                return Quote(custom) + portArgs;
            }

            // 优先用用户实际安装的那份 dsh：
            //   1) DSH 管理器安装的 .bin\dsh.cmd（%APPDATA%\DSH\data\.bin）
            //   2) PATH 上的 dsh
            // 早先优先用 npx 缓存，会跑到另一套 DSH 上，profile 状态对不上，服务起不来。
            string dshCmd = FindDshManagerCommand() ?? FindOnPath("dsh.cmd") ?? FindOnPath("dsh.exe");
            if (dshCmd != null)
            {
                return "call " + Quote(dshCmd) + portArgs;
            }

            // 退路一：npx 缓存里的入口（用 node 直接跑，省掉 cmd 包装）
            string binJs = FindNpxBinJs();
            if (binJs != null)
            {
                string node = ResolveNode();
                if (node != null)
                {
                    return Quote(node) + " " + Quote(binJs) + portArgs;
                }
            }

            // 退路二：让 npx 自己去装/找
            if (binJs != null)
            {
                return "call npx -y @deepseek-ai/dsh" + portArgs;
            }

            return null;
        }

        private static string ResolveNode()
        {
            string node = FindOnPath("node.exe");
            if (node != null)
            {
                return node;
            }

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string candidate = Path.Combine(programFiles, "nodejs", "node.exe");
            return File.Exists(candidate) ? candidate : "node.exe";
        }

        /// <summary>在 npx 缓存里找 @deepseek-ai/dsh 的入口 bin.js。</summary>
        /// <summary>DSH 管理器（%APPDATA%\DSH）安装的 dsh 启动器。</summary>
        private static string FindDshManagerCommand()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string[] candidates = new string[]
                {
                    Path.Combine(appData, "DSH", "data", ".bin", "dsh.cmd"),
                    Path.Combine(appData, "DSH", "data", ".bin", "dsh.exe"),
                    Path.Combine(appData, "DSH", "data", ".bin", "dsh")
                };

                for (int i = 0; i < candidates.Length; i++)
                {
                    if (File.Exists(candidates[i]))
                    {
                        return candidates[i];
                    }
                }
            }
            catch (Exception)
            {
            }

            return null;
        }

        private static string FindNpxBinJs()
        {
            try
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string root = Path.Combine(local, "npm-cache", "_npx");
                if (!Directory.Exists(root))
                {
                    return null;
                }

                string best = null;
                DateTime bestTime = DateTime.MinValue;
                foreach (string dir in Directory.GetDirectories(root))
                {
                    string candidate = Path.Combine(dir, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
                    if (File.Exists(candidate))
                    {
                        DateTime written = File.GetLastWriteTimeUtc(candidate);
                        if (written > bestTime)
                        {
                            bestTime = written;
                            best = candidate;
                        }
                    }
                }

                return best;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string FindOnPath(string fileName)
        {
            try
            {
                string path = Environment.GetEnvironmentVariable("PATH");
                if (string.IsNullOrEmpty(path))
                {
                    return null;
                }

                string[] parts = path.Split(';');
                for (int i = 0; i < parts.Length; i++)
                {
                    string dir = parts[i].Trim();
                    if (dir.Length == 0)
                    {
                        continue;
                    }

                    try
                    {
                        string candidate = Path.Combine(dir, fileName);
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            catch (Exception)
            {
            }

            return null;
        }

        private static void KillTree(int pid)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = Path.Combine(Environment.SystemDirectory, "taskkill.exe");
                info.Arguments = "/PID " + pid.ToString() + " /T /F";
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                Process killer = Process.Start(info);
                if (killer != null)
                {
                    killer.WaitForExit(5000);
                }
            }
            catch (Exception)
            {
            }
        }

        private void RaiseFailed(string message)
        {
            ShellLog.Write("服务失败: " + message.Replace(Environment.NewLine, " | "));
            Action<string> handler = Failed;
            if (handler != null)
            {
                handler(message);
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value + "\"";
        }
    }
}
