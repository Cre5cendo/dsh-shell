using System;
using System.IO;

namespace DshShell
{
    /// <summary>外壳自身的诊断日志（data\logs\shell.log），排查启动/退出问题用。</summary>
    internal static class ShellLog
    {
        private static readonly object Gate = new object();
        private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "logs", "shell.log");

        public static string Path_
        {
            get { return LogPath; }
        }

        public static void Write(string message)
        {
            lock (Gate)
            {
                try
                {
                    string directory = Path.GetDirectoryName(LogPath);
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.AppendAllText(
                        LogPath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + Environment.NewLine);
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
