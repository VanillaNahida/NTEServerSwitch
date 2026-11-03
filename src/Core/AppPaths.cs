using System;
using System.IO;

namespace NTEServerSwitch.Core
{
    /// <summary>
    /// 程序自身的目录约定。所有相对路径都相对 exe 所在目录（绿色版，可随意挪动）。
    /// </summary>
    public static class AppPaths
    {
        /// <summary>exe 所在目录，带尾部反斜杠。</summary>
        public static string BaseDirectory
        {
            get
            {
                string dir = AppContext.BaseDirectory;
                if (string.IsNullOrEmpty(dir))
                {
                    dir = AppDomain.CurrentDomain.BaseDirectory;
                }
                if (string.IsNullOrEmpty(dir))
                {
                    dir = Directory.GetCurrentDirectory();
                }
                return dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;
            }
        }

        /// <summary>渠道资源缓存根目录：&lt;exe目录&gt;\sdkres</summary>
        public static string CacheRoot
        {
            get { return Path.Combine(BaseDirectory, "sdkres"); }
        }

        /// <summary>日志目录：&lt;exe目录&gt;\logs</summary>
        public static string LogDirectory
        {
            get { return Path.Combine(BaseDirectory, "logs"); }
        }

        public static string StateFilePath
        {
            get { return Path.Combine(CacheRoot, "state.json"); }
        }

        public static string ChannelDirectory(ServerChannel channel)
        {
            return Path.Combine(CacheRoot, channel.CacheFolderName);
        }

        public static string ChannelFilesDirectory(ServerChannel channel)
        {
            return Path.Combine(ChannelDirectory(channel), "files");
        }

        public static void EnsureDirectory(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                Directory.CreateDirectory(path);
            }
        }
    }
}
