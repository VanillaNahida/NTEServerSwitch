using System;
using System.Globalization;
using System.IO;

namespace NTEServerSwitch.Core
{
    public static class PathUtil
    {
        /// <summary>
        /// 把清单里的绝对路径（形如 /Config/Config.ini）拼到 root 下。
        /// 任何会逃出 root 的结果都返回 null —— 绝不越界写文件。
        /// </summary>
        public static string SafeCombine(string root, string manifestPath)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(manifestPath))
            {
                return null;
            }

            string relative = manifestPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            if (relative.Length == 0)
            {
                return null;
            }

            string full;
            string rootFull;
            try
            {
                full = Path.GetFullPath(Path.Combine(root, relative));
                rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                           + Path.DirectorySeparatorChar;
            }
            catch
            {
                return null;
            }

            if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            return full;
        }

        /// <summary>UserData 里是账号、日志、本地存档，任何情况下都不许删。</summary>
        public static bool IsUserDataPath(string manifestPath)
        {
            return !string.IsNullOrEmpty(manifestPath)
                   && manifestPath.StartsWith("/UserData/", StringComparison.OrdinalIgnoreCase);
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
            {
                return bytes.ToString(CultureInfo.InvariantCulture) + " B";
            }
            double value = bytes;
            string[] units = { "KB", "MB", "GB", "TB" };
            int index = -1;
            do
            {
                value /= 1024.0;
                index++;
            }
            while (value >= 1024.0 && index < units.Length - 1);

            return value.ToString(value >= 100 ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + units[index];
        }
    }
}
