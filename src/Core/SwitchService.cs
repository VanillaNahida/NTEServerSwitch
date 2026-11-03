using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace NTEServerSwitch.Core
{
    public sealed class SwitchReport
    {
        public bool Success { get; set; }

        public string Message { get; set; }

        public bool UsedCache { get; set; }

        public int Copied { get; set; }

        public int Skipped { get; set; }

        public int Deleted { get; set; }

        public int Failed { get; set; }

        public List<string> Problems { get; set; } = new List<string>();
    }

    /// <summary>
    /// 真正落地：先删掉目标渠道清单里没有的文件（B服的 bilibili SDK 之类），
    /// 再把缓存里的文件覆盖过去，Config.ini 最后写。
    /// 缓存不可用时退回"只改配置文件"，保证任何情况下都比旧版更好或持平。
    /// </summary>
    public sealed class SwitchService
    {
        public const string ConfigRelativePath = "/Config/Config.ini";

        private readonly ResourceCache _cache;
        private readonly LogService _log = LogService.Instance;

        public SwitchService(ResourceCache cache)
        {
            if (cache == null)
            {
                throw new ArgumentNullException("cache");
            }
            _cache = cache;
        }

        public SwitchReport Apply(string gameRoot, ServerChannel target, CacheState state,
                                 IProgress<SyncProgress> progress, CancellationToken token)
        {
            var report = new SwitchReport();
            if (string.IsNullOrEmpty(gameRoot))
            {
                report.Message = "请先选择游戏目录。";
                return report;
            }
            if (target == null)
            {
                report.Message = "目标服务器无效。";
                return report;
            }

            string launcherRoot = Path.Combine(gameRoot, "NTELauncher");
            string configPath = Path.Combine(launcherRoot, "Config", "Config.ini");
            if (!File.Exists(configPath))
            {
                report.Message = "找不到配置文件：" + configPath;
                _log.Error(report.Message);
                return report;
            }

            ChannelCacheState cached = state == null ? null : state.GetChannel(target);
            bool cacheUsable = cached != null && cached.HasFiles && _cache.CountMissing(target, cached) == 0;

            if (cacheUsable)
            {
                ApplyFromCache(launcherRoot, configPath, target, cached, report, progress, token);
            }
            else
            {
                if (cached != null && cached.HasFiles)
                {
                    _log.Warn(target.DisplayName + " 的资源缓存不完整（缺 " +
                              _cache.CountMissing(target, cached) + " 个文件），本次只修改配置文件。");
                }
                else
                {
                    _log.Warn("没有 " + target.DisplayName + " 的资源缓存，本次只修改配置文件。");
                }
                ApplyConfigInPlace(configPath, target, report);
                report.Success = report.Failed == 0;
                if (report.Success)
                {
                    report.Message = "已切换到" + target.DisplayName + "（仅修改配置文件）。";
                }
            }

            if (report.Failed > 0)
            {
                report.Success = false;
                if (string.IsNullOrEmpty(report.Message))
                {
                    report.Message = "有 " + report.Failed + " 个文件处理失败，详见日志。";
                }
            }
            return report;
        }

        private void ApplyFromCache(string launcherRoot, string configPath, ServerChannel target,
                                   ChannelCacheState cached, SwitchReport report,
                                   IProgress<SyncProgress> progress, CancellationToken token)
        {
            report.UsedCache = true;

            Report(progress, "关闭进程", "正在确认游戏已退出…", -1);
            ProcessGuard.CloseAll(Path.GetDirectoryName(launcherRoot), 5000);

            var deletedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1) 删掉目标渠道清单里没有的文件（UserData 永不删除）
            IList<string> removePaths = cached.RemovePaths ?? new List<string>();
            if (removePaths.Count > 0)
            {
                Report(progress, "清理差异文件", "正在删除 " + target.DisplayName + " 不需要的文件…", -1);
            }
            int index = 0;
            foreach (string path in removePaths)
            {
                token.ThrowIfCancellationRequested();
                index++;
                if (PathUtil.IsUserDataPath(path))
                {
                    continue;
                }

                string full = PathUtil.SafeCombine(launcherRoot, path);
                if (full == null)
                {
                    report.Failed++;
                    report.Problems.Add("非法路径：" + path);
                    continue;
                }
                if (!File.Exists(full))
                {
                    continue;
                }

                try
                {
                    File.Delete(full);
                    report.Deleted++;
                    _log.Info("已删除多余文件：" + path);
                    string parent = Path.GetDirectoryName(full);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        deletedDirectories.Add(parent);
                    }
                }
                catch (Exception ex)
                {
                    report.Failed++;
                    report.Problems.Add("删除失败 " + path + "：" + ex.Message);
                    _log.Warn("删除失败 " + path + "：" + ex.Message);
                }

                if (removePaths.Count > 0 && index % 20 == 0)
                {
                    Report(progress, "清理差异文件", "已删除 " + report.Deleted + " 个文件…",
                           (double)index / removePaths.Count * 0.3);
                }
            }

            TryRemoveEmptyDirectories(deletedDirectories, launcherRoot);

            // 2) 覆盖目标渠道的文件，Config.ini 留到最后
            CacheFileEntry configEntry = null;
            int total = cached.Files.Count;
            int done = 0;
            foreach (CacheFileEntry entry in cached.Files)
            {
                token.ThrowIfCancellationRequested();
                done++;

                if (string.Equals(entry.Path, ConfigRelativePath, StringComparison.OrdinalIgnoreCase))
                {
                    configEntry = entry;
                    continue;
                }

                string destination = PathUtil.SafeCombine(launcherRoot, entry.Path);
                if (destination == null)
                {
                    report.Failed++;
                    report.Problems.Add("非法路径：" + entry.Path);
                    continue;
                }

                if (HashUtil.Matches(destination, entry.Size, entry.Md5))
                {
                    report.Skipped++;
                    continue;
                }

                string source = _cache.CachedPath(target, entry);
                if (source == null || !File.Exists(source))
                {
                    report.Failed++;
                    report.Problems.Add("缓存缺少文件：" + entry.Path);
                    _log.Warn("缓存里缺少文件：" + entry.Path);
                    continue;
                }

                // 先验缓存本身：缓存被损坏时宁可报错也不能拿坏文件盖掉游戏里的好文件。
                if (!HashUtil.Matches(source, entry.Size, entry.Md5))
                {
                    report.Failed++;
                    report.Problems.Add("缓存文件已损坏：" + entry.Path);
                    _log.Warn("缓存文件已损坏，已删除，下次检查更新会重新下载：" + entry.Path);
                    TryDeleteFile(source);
                    continue;
                }

                Report(progress, "替换文件", target.DisplayName + "：" + entry.Path,
                       0.3 + 0.65 * done / Math.Max(1, total));
                try
                {
                    string directory = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    File.Copy(source, destination, true);
                    if (!HashUtil.Matches(destination, entry.Size, entry.Md5))
                    {
                        throw new InvalidDataException("复制后校验失败");
                    }
                    report.Copied++;
                }
                catch (Exception ex)
                {
                    report.Failed++;
                    report.Problems.Add("复制失败 " + entry.Path + "：" + ex.Message);
                    _log.Warn("复制失败 " + entry.Path + "：" + ex.Message);
                }
            }

            // 3) Config.ini 最后写
            Report(progress, "写入配置", "正在切换配置文件…", 0.98);
            if (configEntry != null)
            {
                string source = _cache.CachedPath(target, configEntry);
                if (source != null && File.Exists(source))
                {
                    try
                    {
                        File.Copy(source, configPath, true);
                        report.Copied++;
                        _log.Info("已写入 " + target.DisplayName + " 的 Config.ini。");
                    }
                    catch (Exception ex)
                    {
                        report.Failed++;
                        report.Problems.Add("写入 Config.ini 失败：" + ex.Message);
                        ApplyConfigInPlace(configPath, target, report);
                    }
                }
                else
                {
                    _log.Warn("缓存里缺少 Config.ini，改为原地修改。");
                    ApplyConfigInPlace(configPath, target, report);
                }
            }
            else
            {
                _log.Warn("缓存清单里没有 Config.ini，改为原地修改。");
                ApplyConfigInPlace(configPath, target, report);
            }

            Report(progress, "完成", "切换完成。", 1.0);
            report.Success = report.Failed == 0;
            report.Message = "已切换到" + target.DisplayName + "：复制 " + report.Copied +
                             " 个文件、跳过 " + report.Skipped + " 个、删除 " + report.Deleted + " 个多余文件。";
            _log.Success(report.Message);
        }

        /// <summary>缓存不可用时的兜底：直接改本地 Config.ini（与旧版行为一致）。</summary>
        private void ApplyConfigInPlace(string configPath, ServerChannel target, SwitchReport report)
        {
            try
            {
                TextDocument document = TextFile.Read(configPath);
                IniFile ini = IniFile.Parse(document.Text);

                if (target.Id == ChannelId.Bilibili)
                {
                    ini.Ensure("General", "Partner", "123");
                }
                else
                {
                    if (ini.Remove("General", "Partner"))
                    {
                        _log.Info("已移除 Partner=123。");
                    }
                }

                string versionUrl = ini.Get("VERSION", "VersionInfoFileURL");
                if (!string.IsNullOrEmpty(versionUrl))
                {
                    ini.Set("VERSION", "VersionInfoFileURL", Endpoints.BuildBranchUrl(versionUrl, target));
                }
                string backupUrl = ini.Get("UPDATE_CONFIG", "BackupVersionURL");
                if (!string.IsNullOrEmpty(backupUrl))
                {
                    ini.Set("UPDATE_CONFIG", "BackupVersionURL", Endpoints.BuildBranchUrl(backupUrl, target));
                }
                ini.Set("VERSION", "serialNumber", target.SerialNumberText);
                ini.Set("UPDATE_CONFIG", "Branch", target.Branch);

                document.Text = ini.ToText();
                TextFile.Write(configPath, document);
                _log.Info("已原地修改 Config.ini（" + target.DisplayName + "）。");
            }
            catch (Exception ex)
            {
                report.Failed++;
                report.Problems.Add("修改 Config.ini 失败：" + ex.Message);
                _log.Error("修改 Config.ini 失败：" + ex.Message);
            }
        }

        private static void TryRemoveEmptyDirectories(IEnumerable<string> directories, string launcherRoot)
        {
            string root = Path.GetFullPath(launcherRoot)
                              .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                          + Path.DirectorySeparatorChar;

            foreach (string start in directories)
            {
                string current = start;
                while (!string.IsNullOrEmpty(current) && current.Length > root.Length)
                {
                    try
                    {
                        string full = Path.GetFullPath(current).TrimEnd(Path.DirectorySeparatorChar)
                                      + Path.DirectorySeparatorChar;
                        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }
                        if (!Directory.Exists(current))
                        {
                            current = Path.GetDirectoryName(current);
                            continue;
                        }
                        if (Directory.EnumerateFileSystemEntries(current).Any())
                        {
                            break;
                        }
                        Directory.Delete(current);
                        current = Path.GetDirectoryName(current);
                    }
                    catch
                    {
                        break;
                    }
                }
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 删不掉也无所谓，下次同步会覆盖。
            }
        }

        private static void Report(IProgress<SyncProgress> progress, string stage, string detail, double fraction)
        {
            if (progress != null)
            {
                progress.Report(SyncProgress.Create(stage, detail, fraction));
            }
        }
    }
}
