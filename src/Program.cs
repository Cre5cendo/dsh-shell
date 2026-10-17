using System;
using System.Collections.Generic;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace DshShell
{
    internal static class Program
    {
        private const string MutexName = "DshShell.SingleInstance.v1";
        private const string ActivateEventName = "DshShell.Activate.v1";
        private const string AppUserModelId = "DeepSeek.Harness.Shell";

        [STAThread]
        private static void Main(string[] args)
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                ShellLog.Write("Main 启动 createdNew=" + createdNew.ToString() + " args=" + string.Join(",", args));
                if (!createdNew)
                {
                    ShellLog.Write("已有实例在运行，转发激活信号后退出");
                    SignalExistingInstance();
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                try
                {
                    Native.SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
                }
                catch (Exception)
                {
                }

                ShellPaths paths = new ShellPaths();
                ShellConfig config = ShellConfig.Load(paths.ConfigPath);
                ShellState state = ShellState.Load(paths.StatePath);

                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--minimized", StringComparison.OrdinalIgnoreCase))
                    {
                        config.StartMinimized = true;
                    }
                    else if (string.Equals(args[i], "--engine", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        config.Host = args[i + 1];
                        i = i + 1;
                    }
                }

                ShellApp app = new ShellApp(paths, config, state);
                ShellLog.Write("配置: Host=" + config.Host + " Port=" + config.Port + " CloseAction=" + config.CloseAction);
                LogIdentity();

                if (HasFlag(args, "--selftest"))
                {
                    RunSelfTest(config, paths, state);
                    return;
                }

                ListenForActivation(app);
                app.Start();
                ShellLog.Write("进入消息循环");
                Application.Run(app);
                ShellLog.Write("消息循环结束，进程退出");
            }
        }

        private static bool HasFlag(string[] args, string flag)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>把当前进程的令牌身份写进日志，用于排查「为什么子进程写不了某个目录」。</summary>
        private static void LogIdentity()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                ShellLog.Write("进程身份: " + identity.Name + " 64bit=" + Environment.Is64BitProcess);

                List<string> interesting = new List<string>();
                List<string> all = new List<string>();
                foreach (IdentityReference reference in identity.Groups)
                {
                    try
                    {
                        string value = reference.Value;
                        all.Add(value);
                        if (value.StartsWith("S-1-15-2") || value == "S-1-5-12" || value.StartsWith("S-1-16-"))
                        {
                            interesting.Add(value);
                        }
                    }
                    catch (Exception)
                    {
                    }
                }

                ShellLog.Write("令牌特殊组: " + (interesting.Count == 0 ? "(无，即未受限)" : string.Join(",", interesting.ToArray())));
                ShellLog.Write("令牌全部组(" + all.Count + "): " + string.Join(",", all.ToArray()));
            }
            catch (Exception error)
            {
                ShellLog.Write("读取进程身份失败: " + error.Message);
            }
        }

        /// <summary>免 GUI 自检：只走「拉起服务 → 等待 dsh web: URL」这条链路，用于排障。</summary>
        private static void RunSelfTest(ShellConfig config, ShellPaths paths, ShellState state)
        {
            AttachParentConsole();
            ShellLog.Write("selftest 开始");
            Console.WriteLine("[DshShell] 自检开始：拉起 DSH 服务并等待带 token 的 URL…");

            DshServer server = new DshServer(paths, config);
            ManualResetEvent done = new ManualResetEvent(false);
            string url = null;
            string failure = null;

            server.UrlReady += delegate(string value)
            {
                url = value;
                done.Set();
            };
            server.Failed += delegate(string message)
            {
                failure = message;
                done.Set();
            };

            server.Start();
            bool finished = done.WaitOne(TimeSpan.FromSeconds(120));
            ShellLog.Write("selftest 结果: finished=" + finished + " url=" + (url == null ? "(无)" : url) + " fail=" + (failure == null ? "(无)" : failure));

            if (url != null)
            {
                state.ServerUrl = url;
                state.ServerPid = server.ProcessId;
                state.StartedAt = DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture);
                state.Host = "selftest";
                try
                {
                    state.ServerPort = new Uri(url).Port;
                }
                catch (Exception)
                {
                }

                state.Save(paths.StatePath);
                Console.WriteLine("[DshShell] 自检通过 ✓  " + url);
                Console.WriteLine("[DshShell] 服务日志: " + server.LogPath);
                server.Stop();
                Environment.Exit(0);
            }

            Console.WriteLine("[DshShell] 自检失败 ✗  " + (failure == null ? "(超时)" : failure));
            Console.WriteLine("[DshShell] 外壳日志: " + ShellLog.Path_);
            Environment.Exit(2);
        }

        /// <summary>作为 win-exe 从终端运行时，把输出接到调用者的控制台，方便直接看自检结果。</summary>
        private static void AttachParentConsole()
        {
            try
            {
                if (Native.AttachConsole(-1))
                {
                    System.IO.StreamWriter writer = new System.IO.StreamWriter(Console.OpenStandardOutput());
                    writer.AutoFlush = true;
                    Console.SetOut(writer);
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>后台等待"再点一次图标"的信号，把已有窗口拉到前台。</summary>
        private static void ListenForActivation(ShellApp app)
        {
            try
            {
                EventWaitHandle handle = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
                Thread worker = new Thread(delegate()
                {
                    while (true)
                    {
                        handle.WaitOne();
                        try
                        {
                            app.RequestActivate();
                        }
                        catch (Exception)
                        {
                            return;
                        }
                    }
                });
                worker.IsBackground = true;
                worker.Name = "dsh-activate-listener";
                worker.Start();
            }
            catch (Exception)
            {
            }
        }

        private static void SignalExistingInstance()
        {
            try
            {
                EventWaitHandle handle = EventWaitHandle.OpenExisting(ActivateEventName);
                handle.Set();
            }
            catch (Exception)
            {
            }
        }
    }
}


