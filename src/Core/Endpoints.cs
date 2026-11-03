using System;
using System.IO;
using System.Text.RegularExpressions;

namespace NTEServerSwitch.Core
{
    /// <summary>
    /// 从本地 Config.ini 推导两个渠道的 CDN 地址；推导不出来时退回已知常量。
    /// CDN 结构：
    ///   https://yhcdn1.wmupd.com/hd/{publish_ob|publish_ob_B}/launcher/Version.ini
    ///   Version.ini 里的 FileListURL 指向 {版本目录}/AllFiles.xml
    ///   单个文件下载 = AllFiles.xml 所在目录 + 文件路径 + ".zip"
    /// </summary>
    public static class Endpoints
    {
        public const string PrimaryCdn = "https://yhcdn1.wmupd.com";
        public const string BackupCdn = "https://yhcdn2.wmupd.com";

        private static readonly Regex BranchToken =
            new Regex("publish_ob(?:_B)?(?![A-Za-z0-9_])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>把任意一个渠道的 URL 改写成目标渠道的 URL。</summary>
        public static string BuildBranchUrl(string url, ServerChannel channel)
        {
            if (string.IsNullOrEmpty(url))
            {
                return string.Empty;
            }
            return BranchToken.Replace(url, channel.Branch);
        }

        public static string VersionIniUrl(IniFile config, ServerChannel channel)
        {
            return ResolveVersionIniUrl(config, channel, false);
        }

        public static string BackupVersionIniUrl(IniFile config, ServerChannel channel)
        {
            return ResolveVersionIniUrl(config, channel, true);
        }

        private static string ResolveVersionIniUrl(IniFile config, ServerChannel channel, bool backup)
        {
            string hint = null;
            if (config != null)
            {
                hint = backup
                    ? config.Get("UPDATE_CONFIG", "BackupVersionURL")
                    : config.Get("VERSION", "VersionInfoFileURL");
            }

            if (!string.IsNullOrEmpty(hint) && hint.IndexOf("launcher", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return BuildBranchUrl(hint.Trim(), channel);
            }

            // 注意：branch 只能来自 channel。若用配置里的 Branch= 反推，官服配置会推出两渠道同一个地址。
            string branch = channel.Branch;
            string backupServer = null;
            if (config != null)
            {
                backupServer = config.Get("UPDATE_CONFIG", "BackupServer");
            }

            string cdn = backup ? BackupCdn : PrimaryCdn;
            if (backup && !string.IsNullOrEmpty(backupServer))
            {
                cdn = backupServer.Trim().TrimEnd('/');
            }
            return cdn + "/hd/" + branch + "/launcher/Version.ini";
        }

        /// <summary>取 URL 所在目录（去掉最后一段和查询串）。</summary>
        public static string DirectoryUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return string.Empty;
            }
            string clean = url;
            int query = clean.IndexOf('?');
            if (query >= 0)
            {
                clean = clean.Substring(0, query);
            }
            int slash = clean.LastIndexOf('/');
            return slash > 0 ? clean.Substring(0, slash) : clean;
        }

        /// <summary>拼成单个文件的 zip 下载地址。</summary>
        public static string FileZipUrl(string directoryUrl, string manifestPath)
        {
            if (string.IsNullOrEmpty(directoryUrl) || string.IsNullOrEmpty(manifestPath))
            {
                return string.Empty;
            }
            return directoryUrl.TrimEnd('/') + manifestPath + ".zip";
        }

        public static string CombineUrl(string baseUrl, string relative)
        {
            if (string.IsNullOrEmpty(relative))
            {
                return string.Empty;
            }
            if (relative.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return relative;
            }
            if (string.IsNullOrEmpty(baseUrl))
            {
                return relative;
            }
            return baseUrl.TrimEnd('/') + "/" + relative.TrimStart('/');
        }
    }
}
