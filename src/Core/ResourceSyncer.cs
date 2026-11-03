using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace NTEServerSwitch.Core
{
    /// <summary>进度上报。Fraction &lt; 0 表示"不确定进度"。</summary>
    public sealed class SyncProgress
    {
        public string Stage { get; set; }

        public string Detail { get; set; }

        public double Fraction { get; set; }

        public static SyncProgress Create(string stage, string detail, double fraction)
        {
            return new SyncProgress { Stage = stage, Detail = detail ?? string.Empty, Fraction = fraction };
        }
    }

    public sealed class ChannelCacheReport
    {
        public bool Ready { get; set; }

        public bool DownloadFailed { get; set; }

        public string Version { get; set; }

        public string ProductVersion { get; set; }

        public int FileCount { get; set; }

        public int MissingCount { get; set; }

        public long TotalBytes { get; set; }

        public long ReadyBytes { get; set; }

        public string Message { get; set; }
    }

    public sealed class SyncReport
    {
        public bool Success { get; set; }

        public bool NetworkAvailable { get; set; }

        public string Message { get; set; }

        public string LocalVersion { get; set; }

        public bool VersionMatches { get; set; }

        public string OfficialVersion { get; set; }

        public string BilibiliVersion { get; set; }

        public ChannelCacheReport Official { get; set; }

        public ChannelCacheReport Bilibili { get; set; }

        public CacheState State { get; set; }

        public bool OfficialDownloadFailed { get; set; }

        public bool BilibiliDownloadFailed { get; set; }

        public ChannelCacheReport ForChannel(ServerChannel channel)
        {
            return channel != null && channel.Id == ChannelId.Bilibili ? Bilibili : Official;
        }
    }

    /// <summary>
    /// 检查线上清单 → 重算差异集 → 下载进 sdkres。
    /// 设计原则：任何失败都不能让切服功能变差，最差也要退回"只改配置文件"。
    /// </summary>
    public sealed class ResourceSyncer : IDisposable
    {
        private readonly ResourceCache _cache;
        private readonly UpdateClient _client;
        private readonly LogService _log = LogService.Instance;

        public ResourceSyncer(ResourceCache cache)
        {
            if (cache == null)
            {
                throw new ArgumentNullException("cache");
            }
            _cache = cache;
            _client = new UpdateClient();
        }

        public async Task<SyncReport> SyncAsync(string gameRoot, bool forceRefresh,
                                               IProgress<SyncProgress> progress, CancellationToken token)
        {
            var report = new SyncReport { VersionMatches = true };
            _officialFailed = false;
            _bilibiliFailed = false;
            CacheState state = _cache.LoadState();
            report.State = state;

            string launcherRoot = null;
            IniFile localIni = null;
            if (!string.IsNullOrEmpty(gameRoot))
            {
                launcherRoot = Path.Combine(gameRoot, "NTELauncher");
                string configPath = Path.Combine(launcherRoot, "Config", "Config.ini");
                if (File.Exists(configPath))
                {
                    try
                    {
                        localIni = IniFile.Parse(TextFile.Read(configPath).Text);
                        string version = localIni.Get("VERSION", "Version");
                        report.LocalVersion = version == null ? string.Empty : version.Trim();
                    }
                    catch (Exception ex)
                    {
                        _log.Warn("读取本地 Config.ini 失败：" + ex.Message);
                    }
                }
                else
                {
                    _log.Warn("没有找到本地 Config.ini：" + configPath);
                }
            }

            Report(progress, "检查更新", "正在获取版本信息…", -1);
            string officialUrl = Endpoints.VersionIniUrl(localIni, ServerChannel.Official);
            string bilibiliUrl = Endpoints.VersionIniUrl(localIni, ServerChannel.Bilibili);
            _log.Info("官服版本地址：" + officialUrl);
            _log.Info("B服版本地址：" + bilibiliUrl);

            VersionPair pair = await TryGetVersionsAsync(officialUrl, bilibiliUrl, token);
            if (pair == null)
            {
                string backupOfficial = Endpoints.BackupVersionIniUrl(localIni, ServerChannel.Official);
                string backupBilibili = Endpoints.BackupVersionIniUrl(localIni, ServerChannel.Bilibili);
                _log.Warn("主 CDN 不可用，改用备用地址：" + backupOfficial);
                Report(progress, "检查更新", "主服务器不可用，正在尝试备用地址…", -1);
                pair = await TryGetVersionsAsync(backupOfficial, backupBilibili, token);
                if (pair == null)
                {
                    report.Success = false;
                    report.NetworkAvailable = false;
                    report.Message = "无法连接更新服务器，本次只修改配置文件。";
                    _log.Error(report.Message);
                    Describe(report, state);
                    return report;
                }
            }

            VersionInfo officialVersion = pair.Official;
            VersionInfo bilibiliVersion = pair.Bilibili;

            report.NetworkAvailable = true;
            report.OfficialVersion = officialVersion.Version;
            report.BilibiliVersion = bilibiliVersion.Version;
            report.VersionMatches = string.IsNullOrEmpty(report.LocalVersion)
                                    || string.Equals(report.LocalVersion, officialVersion.Version,
                                                     StringComparison.OrdinalIgnoreCase);
            if (!report.VersionMatches)
            {
                _log.Warn("本地版本 " + report.LocalVersion + " 与线上 " + officialVersion.Version +
                          " 不一致，资源缓存将不生效；请先让启动器完成一次更新。");
            }

            bool needRefresh = forceRefresh
                               || !StateMatches(state, ServerChannel.Official, officialVersion)
                               || !StateMatches(state, ServerChannel.Bilibili, bilibiliVersion)
                               || CacheIncomplete(state);

            if (needRefresh)
            {
                try
                {
                    await RefreshAsync(launcherRoot, state, officialVersion, bilibiliVersion, report, progress, token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    report.Success = false;
                    report.Message = "更新资源缓存失败，本次只修改配置文件。（" + ex.Message + "）";
                    _log.Error(report.Message);
                    _log.Exception("资源缓存更新异常：", ex);
                    Describe(report, state);
                    return report;
                }
            }
            else
            {
                _log.Success("资源缓存已是最新（官服 " + officialVersion.Version + "）。");
            }

            Describe(report, state);
            report.Success = !report.OfficialDownloadFailed && !report.BilibiliDownloadFailed;
            if (!report.Success)
            {
                report.Message = "部分资源下载失败，切服时会跳过缺失的文件。";
            }
            return report;
        }

        private async Task<VersionPair> TryGetVersionsAsync(string officialUrl, string bilibiliUrl,
                                                           CancellationToken token)
        {
            try
            {
                Task<string> officialTask = _client.GetStringAsync(officialUrl, token);
                Task<string> bilibiliTask = _client.GetStringAsync(bilibiliUrl, token);
                await Task.WhenAll(officialTask, bilibiliTask).ConfigureAwait(false);
                return new VersionPair
                {
                    Official = VersionInfo.Parse(officialTask.Result, officialUrl),
                    Bilibili = VersionInfo.Parse(bilibiliTask.Result, bilibiliUrl)
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Warn("获取版本信息失败：" + ex.Message);
                return null;
            }
        }

        private sealed class VersionPair
        {
            public VersionInfo Official { get; set; }

            public VersionInfo Bilibili { get; set; }
        }

        /// <summary>缓存里少了文件（被手动删掉/上次下载失败）也要重建。</summary>
        private bool CacheIncomplete(CacheState state)
        {
            foreach (ServerChannel channel in ServerChannel.All)
            {
                ChannelCacheState cached = state.GetChannel(channel);
                if (cached == null || !cached.HasFiles)
                {
                    return true;
                }
                if (_cache.CountMissing(channel, cached) > 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool StateMatches(CacheState state, ServerChannel channel, VersionInfo remote)
        {
            ChannelCacheState cached = state.GetChannel(channel);
            if (cached == null || !cached.HasFiles)
            {
                return false;
            }
            if (!string.Equals(cached.Version ?? string.Empty, remote.Version ?? string.Empty,
                               StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return string.Equals(cached.FileListUrl ?? string.Empty, remote.FileListUrl ?? string.Empty,
                                 StringComparison.OrdinalIgnoreCase);
        }

        private async Task RefreshAsync(string launcherRoot, CacheState state, VersionInfo officialVersion,
                                        VersionInfo bilibiliVersion, SyncReport report,
                                        IProgress<SyncProgress> progress, CancellationToken token)
        {
            Report(progress, "检查更新", "正在下载文件清单…", -1);
            string officialXml = await _client.GetStringAsync(officialVersion.FileListUrl, token).ConfigureAwait(false);
            string bilibiliXml = await _client.GetStringAsync(bilibiliVersion.FileListUrl, token).ConfigureAwait(false);
            FileList officialList = FileList.Parse(officialXml, officialVersion.FileListUrl);
            FileList bilibiliList = FileList.Parse(bilibiliXml, bilibiliVersion.FileListUrl);
            _log.Info("文件清单：官服 " + officialList.Files.Count + " 个文件，B服 " + bilibiliList.Files.Count + " 个文件。");

            ManifestDiff diff = ManifestDiff.Compute(officialList, bilibiliList);
            _log.Info("差异集：官服需缓存 " + diff.OfficialFiles.Count + " 个文件，B服需缓存 " +
                      diff.BilibiliFiles.Count + " 个文件。");

            var newState = new CacheState();
            newState.SetChannel(ServerChannel.Official,
                                BuildChannelState(officialVersion, officialList, diff.OfficialFiles, diff.OfficialRemovePaths));
            newState.SetChannel(ServerChannel.Bilibili,
                                BuildChannelState(bilibiliVersion, bilibiliList, diff.BilibiliFiles, diff.BilibiliRemovePaths));

            Report(progress, "检查缓存", "正在核对本地已有文件…", -1);
            int seeded = SeedFromLocal(launcherRoot, newState);

            var pending = new List<DownloadItem>();
            foreach (ServerChannel channel in ServerChannel.All)
            {
                ChannelCacheState cached = newState.GetChannel(channel);
                if (cached == null)
                {
                    continue;
                }
                foreach (CacheFileEntry entry in cached.Files)
                {
                    // 待下载判定用完整哈希：宁可多算一次 MD5，也不要拿错文件去覆盖游戏。
                    if (!_cache.IsValid(channel, entry, true))
                    {
                        pending.Add(new DownloadItem
                        {
                            Channel = channel,
                            Entry = entry,
                            DirectoryUrl = Endpoints.DirectoryUrl(
                                channel.Id == ChannelId.Bilibili ? bilibiliVersion.FileListUrl : officialVersion.FileListUrl)
                        });
                    }
                }
            }

            long totalZip = 0;
            foreach (DownloadItem item in pending)
            {
                totalZip += item.Entry.ZipSize > 0 ? item.Entry.ZipSize : item.Entry.Size;
            }
            _log.Info("待下载 " + pending.Count + " 个文件，约 " + PathUtil.FormatBytes(totalZip) + "。");

            AppPaths.EnsureDirectory(_cache.TempDirectory);
            long done = 0;
            foreach (DownloadItem item in pending)
            {
                token.ThrowIfCancellationRequested();
                double fraction = totalZip > 0 ? Math.Min(0.99, (double)done / totalZip) : -1;
                Report(progress, "下载资源",
                       item.Channel.DisplayName + "：" + item.Entry.Path, fraction);
                try
                {
                    await DownloadOneAsync(item, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    SetDownloadFailed(report, item.Channel);
                    _log.Error("下载失败 " + item.Channel.DisplayName + " " + item.Entry.Path + "：" + ex.Message);
                }
                done += item.Entry.ZipSize > 0 ? item.Entry.ZipSize : item.Entry.Size;
            }

            Report(progress, "检查缓存", "正在清理过期文件…", -1);
            foreach (ServerChannel channel in ServerChannel.All)
            {
                ChannelCacheState cached = newState.GetChannel(channel);
                if (cached == null || cached.Files == null)
                {
                    continue;
                }
                var keep = new List<string>();
                foreach (CacheFileEntry entry in cached.Files)
                {
                    keep.Add(entry.Path);
                }
                _cache.PruneChannelFiles(channel, keep);
            }

            TryDeleteDirectory(_cache.TempDirectory);

            state.Channels = newState.Channels;
            _cache.SaveState(state);
            if (seeded > 0)
            {
                _log.Success("已从本地复用 " + seeded + " 个文件，免去重复下载。");
            }
            _log.Success("资源缓存已更新完成。");
        }

        private static ChannelCacheState BuildChannelState(VersionInfo version, FileList list,
                                                          List<ManifestFileEntry> files, List<string> removePaths)
        {
            var state = new ChannelCacheState
            {
                Version = version.Version,
                ProductVersion = string.IsNullOrEmpty(list.ProductVersion) ? version.Version : list.ProductVersion,
                FileListUrl = version.FileListUrl,
                BaseUrl = Endpoints.DirectoryUrl(version.FileListUrl),
                CheckedAtUtc = DateTime.UtcNow.ToString("o"),
                RemovePaths = new List<string>(removePaths)
            };
            foreach (ManifestFileEntry entry in files)
            {
                state.Files.Add(new CacheFileEntry
                {
                    Path = entry.Path,
                    Md5 = entry.Checksum,
                    Size = entry.Size,
                    ZipMd5 = entry.ZipChecksum,
                    ZipSize = entry.ZipSize
                });
            }
            return state;
        }

        /// <summary>本地已经有内容一致的文件就直接拷进缓存，省一次下载。</summary>
        private int SeedFromLocal(string launcherRoot, CacheState state)
        {
            if (string.IsNullOrEmpty(launcherRoot) || !Directory.Exists(launcherRoot))
            {
                return 0;
            }

            int seeded = 0;
            foreach (ServerChannel channel in ServerChannel.All)
            {
                ChannelCacheState cached = state.GetChannel(channel);
                if (cached == null || cached.Files == null)
                {
                    continue;
                }
                foreach (CacheFileEntry entry in cached.Files)
                {
                    if (_cache.IsValid(channel, entry, false))
                    {
                        continue;
                    }
                    string source = PathUtil.SafeCombine(launcherRoot, entry.Path);
                    if (source == null || !File.Exists(source))
                    {
                        continue;
                    }
                    string target = _cache.CachedPath(channel, entry);
                    if (target == null)
                    {
                        continue;
                    }
                    if (!HashUtil.Matches(source, entry.Size, entry.Md5))
                    {
                        continue;
                    }
                    try
                    {
                        AppPaths.EnsureDirectory(Path.GetDirectoryName(target));
                        File.Copy(source, target, true);
                        seeded++;
                    }
                    catch (Exception ex)
                    {
                        _log.Warn("本地复用失败：" + entry.Path + " - " + ex.Message);
                    }
                }
            }
            return seeded;
        }

        private async Task DownloadOneAsync(DownloadItem item, CancellationToken token)
        {
            string destination = _cache.CachedPath(item.Channel, item.Entry);
            if (destination == null)
            {
                throw new InvalidDataException("清单路径越界：" + item.Entry.Path);
            }

            string zipPath = Path.Combine(_cache.TempDirectory, Guid.NewGuid().ToString("N") + ".zip");
            string url = Endpoints.FileZipUrl(item.DirectoryUrl, item.Entry.Path);
            try
            {
                await _client.DownloadAsync(url, zipPath, null, token).ConfigureAwait(false);

                if (!string.IsNullOrEmpty(item.Entry.ZipMd5))
                {
                    string actualZip = HashUtil.Md5File(zipPath);
                    if (!HashUtil.Md5Equals(actualZip, item.Entry.ZipMd5))
                    {
                        throw new InvalidDataException("压缩包校验失败");
                    }
                }

                AppPaths.EnsureDirectory(Path.GetDirectoryName(destination));
                ExtractSingleFile(zipPath, item.Entry.FileName, destination);

                if (!HashUtil.Matches(destination, item.Entry.Size, item.Entry.Md5))
                {
                    throw new InvalidDataException("文件校验失败");
                }

                _log.Info("已缓存 " + item.Channel.DisplayName + " " + item.Entry.Path +
                          "（" + PathUtil.FormatBytes(item.Entry.Size) + "）");
            }
            catch
            {
                TryDeleteFile(destination);
                throw;
            }
            finally
            {
                TryDeleteFile(zipPath);
            }
        }

        private static void ExtractSingleFile(string zipPath, string fileName, string destination)
        {
            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                ZipArchiveEntry chosen = null;
                foreach (ZipArchiveEntry candidate in archive.Entries)
                {
                    if (string.IsNullOrEmpty(candidate.Name))
                    {
                        continue;
                    }
                    if (chosen == null)
                    {
                        chosen = candidate;
                    }
                    if (string.Equals(candidate.Name, fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        chosen = candidate;
                        break;
                    }
                }
                if (chosen == null)
                {
                    throw new InvalidDataException("压缩包里没有文件");
                }

                string temp = destination + ".nswtmp";
                chosen.ExtractToFile(temp, true);
                File.Move(temp, destination, true);
            }
        }

        private void Describe(SyncReport report, CacheState state)
        {
            report.Official = DescribeChannel(state, ServerChannel.Official);
            report.Bilibili = DescribeChannel(state, ServerChannel.Bilibili);
            report.State = state;
        }

        private ChannelCacheReport DescribeChannel(CacheState state, ServerChannel channel)
        {
            var info = new ChannelCacheReport();
            ChannelCacheState cached = state.GetChannel(channel);
            if (cached == null || !cached.HasFiles)
            {
                info.Message = "尚未建立缓存";
                return info;
            }

            int missing;
            long ready = _cache.MeasureReadyBytes(channel, cached, out missing);
            long total = 0;
            foreach (CacheFileEntry entry in cached.Files)
            {
                total += entry.Size;
            }

            info.Version = cached.Version;
            info.ProductVersion = cached.ProductVersion;
            info.FileCount = cached.Files.Count;
            info.MissingCount = missing;
            info.TotalBytes = total;
            info.ReadyBytes = ready;
            info.Ready = missing == 0;
            info.DownloadFailed = channel.Id == ChannelId.Bilibili
                                      ? _bilibiliFailed
                                      : _officialFailed;
            info.Message = info.Ready
                ? cached.Files.Count + " 个文件 / " + PathUtil.FormatBytes(total)
                : "缺 " + missing + " 个文件";
            return info;
        }

        private bool _officialFailed;
        private bool _bilibiliFailed;

        private void SetDownloadFailed(SyncReport report, ServerChannel channel)
        {
            if (channel.Id == ChannelId.Bilibili)
            {
                _bilibiliFailed = true;
                report.BilibiliDownloadFailed = true;
            }
            else
            {
                _officialFailed = true;
                report.OfficialDownloadFailed = true;
            }
        }

        private static void Report(IProgress<SyncProgress> progress, string stage, string detail, double fraction)
        {
            if (progress != null)
            {
                progress.Report(SyncProgress.Create(stage, detail, fraction));
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
                // 临时文件删不掉不影响结果。
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
                // 忽略。
            }
        }

        private sealed class DownloadItem
        {
            public ServerChannel Channel { get; set; }

            public CacheFileEntry Entry { get; set; }

            public string DirectoryUrl { get; set; }
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }
}
