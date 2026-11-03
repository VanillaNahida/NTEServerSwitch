using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace NTEServerSwitch.Core
{
    /// <summary>切服前把游戏相关进程关掉，否则文件被占用会复制失败。</summary>
    public static class ProcessGuard
    {
        /// <summary>启动器与游戏的全部可执行文件名（不带 .exe）。</summary>
        private static readonly string[] ProcessNames =
        {
            "NTELauncher",
            "NTEUpdate",
            "NTEGame",
            "HTGame",
            "NTEBrowser",
            "NTEWebBooster",
            "NTEErrRep",
            "PCGamePlatform",
            "game_security_protection"
        };

        /// <summary>有游戏/启动器进程正在跑（不管路径，同名即算）。</summary>
        public static bool AnyRunning(string gameRoot)
        {
            foreach (string name in ProcessNames)
            {
                Process[] processes;
                try
                {
                    processes = Process.GetProcessesByName(name);
                }
                catch
                {
                    continue;
                }

                try
                {
                    foreach (Process process in processes)
                    {
                        try
                        {
                            if (BelongsTo(process, gameRoot))
                            {
                                return true;
                            }
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                }
                finally
                {
                    // processes 已逐个 Dispose。
                }
            }
            return false;
        }

        public static IList<string> CloseAll(string gameRoot, int waitMilliseconds)
        {
            var closed = new List<string>();
            LogService log = LogService.Instance;

            foreach (string name in ProcessNames)
            {
                Process[] processes;
                try
                {
                    processes = Process.GetProcessesByName(name);
                }
                catch
                {
                    continue;
                }

                foreach (Process process in processes)
                {
                    try
                    {
                        if (!BelongsTo(process, gameRoot))
                        {
                            continue;
                        }

                        string label = name + " (PID " + process.Id + ")";
                        process.Kill();
                        if (!process.WaitForExit(waitMilliseconds))
                        {
                            log.Warn("结束进程超时：" + label);
                            continue;
                        }
                        log.Info("已结束进程：" + label);
                        closed.Add(name);
                    }
                    catch (Exception ex)
                    {
                        log.Warn("结束进程 " + name + " 失败：" + ex.Message);
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }

            if (closed.Count == 0)
            {
                log.Info("没有需要结束的游戏进程。");
            }
            return closed;
        }

        /// <summary>读不到可执行路径时（权限/受保护进程）按同名处理，宁可关掉。</summary>
        private static bool BelongsTo(Process process, string gameRoot)
        {
            if (string.IsNullOrEmpty(gameRoot))
            {
                return true;
            }
            try
            {
                string executable = process.MainModule == null ? null : process.MainModule.FileName;
                if (string.IsNullOrEmpty(executable))
                {
                    return true;
                }
                string root = Path.GetFullPath(gameRoot)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                return Path.GetFullPath(executable).StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return true;
            }
        }
    }
}
