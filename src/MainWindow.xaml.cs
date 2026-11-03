using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using NTEServerSwitch.Core;

namespace NTEServerSwitch
{
    public partial class MainWindow : Window
    {
        private const string UninstallKey =
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\YH";

        private const string InstallLocationValue = "InstallLocation";
        private const string ConfigFileName = "Config.ini";

        private readonly ResourceCache _cache = new ResourceCache();
        private readonly LogService _log = LogService.Instance;

        private ResourceSyncer _syncer;
        private SwitchService _switchService;
        private UpdateClient _updateClient;
        private bool _updatePromptShown;
        private SyncReport _report;
        private CacheState _state;
        private CancellationTokenSource _cancellation;
        private LogWindow _logWindow;
        private bool _busy;
        private string _gamePath = string.Empty;

        public MainWindow()
        {
            InitializeComponent();

            _syncer = new ResourceSyncer(_cache);
            _switchService = new SwitchService(_cache);
            _updateClient = new UpdateClient();

            Closed += OnWindowClosed;

            _gamePath = DetectGamePath();
            ApplyGamePath(_gamePath);

            Loaded += OnWindowLoaded;
        }

        private async void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            _log.Info("开始检查游戏目录与资源配置。");
            ShowStateFromDisk();
            UpdateServerStatus();

            // 版本检查走后台，失败不打扰用户；跟资源检查并行跑，谁也不等谁。
            _ = RunUpdateCheckAsync(true);

            if (string.IsNullOrEmpty(_gamePath))
            {
                SetStatus("没有检测到游戏安装位置，请手动选择目录。");
                _log.Warn("注册表中没有找到《异环》的安装位置。");
                return;
            }

            await SyncAsync(false);
        }

        private void OnWindowClosed(object sender, EventArgs e)
        {
            if (_cancellation != null)
            {
                try
                {
                    _cancellation.Cancel();
                }
                catch
                {
                    // 忽略。
                }
            }

            if (_logWindow != null)
            {
                _logWindow.ForceClose();
                _logWindow = null;
            }

            if (_syncer != null)
            {
                _syncer.Dispose();
                _syncer = null;
            }

            if (_updateClient != null)
            {
                _updateClient.Dispose();
                _updateClient = null;
            }
        }

        // ---------- 目录检测 ----------

        private string DetectGamePath()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(UninstallKey))
                {
                    if (key != null)
                    {
                        string location = key.GetValue(InstallLocationValue) as string;
                        string normalized = NormalizeGamePath(location);
                        if (!string.IsNullOrEmpty(normalized))
                        {
                            _log.Info("从注册表检测到游戏目录：" + normalized);
                            return normalized;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Warn("读取注册表失败：" + ex.Message);
            }
            return string.Empty;
        }

        private static string NormalizeGamePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string candidate = path.Trim().Trim('"')
                                   .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (HasConfig(candidate))
            {
                return candidate;
            }

            // 用户可能直接选到了 NTELauncher 目录
            if (string.Equals(Path.GetFileName(candidate), "NTELauncher", StringComparison.OrdinalIgnoreCase))
            {
                string parent = Path.GetDirectoryName(candidate);
                if (!string.IsNullOrEmpty(parent) && HasConfig(parent))
                {
                    return parent;
                }
            }
            return string.Empty;
        }

        private static bool HasConfig(string gameRoot)
        {
            if (string.IsNullOrEmpty(gameRoot))
            {
                return false;
            }
            return File.Exists(Path.Combine(gameRoot, "NTELauncher", "Config", ConfigFileName));
        }

        private void ApplyGamePath(string path)
        {
            _gamePath = path ?? string.Empty;
            GamePathTextBox.Text = string.IsNullOrEmpty(_gamePath) ? "（未检测到游戏目录）" : _gamePath;
        }

        private void SelectPathButton_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "请选择《异环》的安装目录（包含 NTELauncher 文件夹的那一层）";
                dialog.UseDescriptionForTitle = true;
                if (!string.IsNullOrEmpty(_gamePath) && Directory.Exists(_gamePath))
                {
                    dialog.SelectedPath = _gamePath;
                }

                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    return;
                }

                string normalized = NormalizeGamePath(dialog.SelectedPath);
                if (string.IsNullOrEmpty(normalized))
                {
                    MessageBox.Show(this,
                                    "这个目录里没有 NTELauncher\\Config\\Config.ini，请重新选择。",
                                    "目录无效", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                ApplyGamePath(normalized);
                _log.Info("手动指定游戏目录：" + normalized);
                UpdateServerStatus();
                _ = SyncAsync(false);
            }
        }

        // ---------- 状态显示 ----------

        private void UpdateServerStatus()
        {
            Brush officialBrush = CreateBrush(ServerChannel.Official.AccentColor);
            Brush bilibiliBrush = CreateBrush(ServerChannel.Bilibili.AccentColor);

            string configPath = string.IsNullOrEmpty(_gamePath)
                                    ? null
                                    : Path.Combine(_gamePath, "NTELauncher", "Config", ConfigFileName);

            if (configPath == null || !File.Exists(configPath))
            {
                ServerStatusText.Text = "未检测到游戏";
                ServerStatusText.Foreground = Brushes.Gray;
                return;
            }

            try
            {
                string content = File.ReadAllText(configPath);
                bool isBilibili = content.IndexOf("publish_ob_B", StringComparison.Ordinal) >= 0;
                ServerStatusText.Text = isBilibili ? "当前服务器：B服" : "当前服务器：官服";
                ServerStatusText.Foreground = isBilibili ? bilibiliBrush : officialBrush;
            }
            catch (Exception ex)
            {
                _log.Warn("读取 Config.ini 失败：" + ex.Message);
                ServerStatusText.Text = "无法读取配置";
                ServerStatusText.Foreground = Brushes.Gray;
            }
        }

        private void ShowStateFromDisk()
        {
            _state = _cache.LoadState();
            OfficialCacheText.Text = BuildCacheLine(ServerChannel.Official, _state);
            BilibiliCacheText.Text = BuildCacheLine(ServerChannel.Bilibili, _state);
        }

        private string BuildCacheLine(ServerChannel channel, CacheState state)
        {
            ChannelCacheState cached = state == null ? null : state.GetChannel(channel);
            if (cached == null || !cached.HasFiles)
            {
                return channel.DisplayName + "缓存：尚未建立";
            }

            int missing;
            long ready = _cache.MeasureReadyBytes(channel, cached, out missing);
            string prefix = channel.DisplayName + "缓存：" +
                            (string.IsNullOrEmpty(cached.Version) ? "未知版本" : cached.Version) +
                            " · " + cached.Files.Count + " 个文件 · " + PathUtil.FormatBytes(ready);
            return missing == 0 ? prefix + " · 已就绪" : prefix + " · 缺 " + missing + " 个";
        }

        private void UpdateCacheLines(SyncReport report)
        {
            if (report == null)
            {
                ShowStateFromDisk();
                return;
            }
            OfficialCacheText.Text = BuildCacheLine(ServerChannel.Official, report.State);
            BilibiliCacheText.Text = BuildCacheLine(ServerChannel.Bilibili, report.State);
        }

        private static Brush CreateBrush(string color)
        {
            try
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
                brush.Freeze();
                return brush;
            }
            catch
            {
                return Brushes.Black;
            }
        }

        private void SetStatus(string message)
        {
            StatusInfoText.Text = message ?? string.Empty;
        }

        private void SetBusy(bool busy, string message)
        {
            _busy = busy;
            SelectPathButton.IsEnabled = !busy;
            SwitchToOfficialButton.IsEnabled = !busy;
            SwitchToBButton.IsEnabled = !busy;
            RefreshCacheButton.IsEnabled = !busy;
            WorkProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            if (!busy)
            {
                WorkProgressBar.IsIndeterminate = false;
                WorkProgressBar.Value = 0;
                WorkProgressText.Text = string.Empty;
            }
            if (!string.IsNullOrEmpty(message))
            {
                SetStatus(message);
            }
        }

        private void OnProgress(SyncProgress progress)
        {
            if (progress == null)
            {
                return;
            }
            if (progress.Fraction < 0)
            {
                WorkProgressBar.IsIndeterminate = true;
            }
            else
            {
                WorkProgressBar.IsIndeterminate = false;
                WorkProgressBar.Value = Math.Max(0, Math.Min(1, progress.Fraction)) * 100;
            }
            string text = string.IsNullOrEmpty(progress.Detail)
                              ? progress.Stage
                              : progress.Stage + " · " + progress.Detail;
            if (progress.BytesPerSecond > 0.5)
            {
                // 速度放在最前面：后面的文件路径会不停变化，放在末尾速度数字会跟着乱移动，很难读
                text = "[" + PathUtil.FormatBytes((long)progress.BytesPerSecond) + "/s] " + text;
            }
            WorkProgressText.Text = text;
        }

        // ---------- 版本检查 ----------

        /// <summary>
        /// 跟 GitHub Releases 比对版本。silent = 启动时自动检查：失败什么都不做，有更新才提示。
        /// </summary>
        private async Task RunUpdateCheckAsync(bool silent)
        {
            if (silent)
            {
                if (_updatePromptShown)
                {
                    return;
                }
                // 先让启动时的资源检查把界面占上，避免两条流程抢状态栏
                await Task.Delay(1200);
            }

            if (_updateClient == null)
            {
                return;
            }
            if (!silent)
            {
                SetStatus("正在检查新版本…");
            }

            ReleaseInfo release = await UpdateChecker.CheckAsync(_updateClient, CancellationToken.None);
            if (release == null)
            {
                _log.Warn("检查新版本失败（网络不可用或 GitHub 接口异常），按静默处理。");
                if (!silent)
                {
                    SetStatus("检查更新失败，请稍后再试。");
                    MessageBox.Show(this, "检查更新失败，请检查网络后重试。", "检查更新",
                                    MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;
            }

            string current = UpdateChecker.CurrentVersionText;
            if (!UpdateChecker.IsNewer(release.TagName, UpdateChecker.CurrentVersion))
            {
                _log.Info("已是最新版本：" + current + "（远端 " + release.Version + "）。");
                if (!silent)
                {
                    SetStatus("已是最新版本 " + current + "。");
                    MessageBox.Show(this, "当前已经是最新版本 " + current + "。", "检查更新",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            _updatePromptShown = true;
            _log.Success("发现新版本 " + release.Version + "（当前 " + current + "）。");
            SetStatus("发现新版本 " + release.Version + "，点击「检查更新」可前往下载。");

            MessageBoxResult answer = MessageBox.Show(
                this,
                "发现新版本 " + release.Version + "（当前 " + current + "）。" + Environment.NewLine +
                Environment.NewLine + "是否前往 GitHub Releases 下载？",
                "发现新版本",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information,
                MessageBoxResult.Yes);
            if (answer == MessageBoxResult.Yes)
            {
                OpenUrl(string.IsNullOrEmpty(release.HtmlUrl)
                            ? UpdateChecker.LatestReleasePageUrl
                            : release.HtmlUrl);
            }
        }

        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _log.Warn("打开链接失败：" + url + " - " + ex.Message);
            }
        }

        private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            await RunUpdateCheckAsync(false);
        }

        // ---------- 资源同步 ----------

        private async Task SyncAsync(bool forceRefresh)
        {
            if (_busy)
            {
                return;
            }
            if (string.IsNullOrEmpty(_gamePath))
            {
                SetStatus("请先选择游戏目录。");
                return;
            }

            SetBusy(true, forceRefresh ? "正在更新资源缓存…" : "正在检查资源更新…");
            _cancellation = new CancellationTokenSource();
            var progress = new Progress<SyncProgress>(OnProgress);
            int started = Environment.TickCount;

            try
            {
                SyncReport report = await _syncer.SyncAsync(_gamePath, forceRefresh, progress, _cancellation.Token);
                _report = report;
                _state = report.State;
                UpdateCacheLines(report);

                string elapsed = ((Environment.TickCount - started) / 1000.0)
                    .ToString("0.0", CultureInfo.InvariantCulture);
                if (string.IsNullOrEmpty(report.Message))
                {
                    SetStatus("资源检查完成（" + elapsed + " 秒）。");
                }
                else
                {
                    SetStatus(report.Message + "（" + elapsed + " 秒）");
                }
            }
            catch (OperationCanceledException)
            {
                SetStatus("已取消。");
            }
            catch (Exception ex)
            {
                _log.Exception("检查资源更新时出错：", ex);
                SetStatus("检查资源更新失败，详见日志。");
            }
            finally
            {
                if (_cancellation != null)
                {
                    _cancellation.Dispose();
                    _cancellation = null;
                }
                SetBusy(false, null);
                UpdateServerStatus();
            }
        }

        private void RefreshCacheButton_Click(object sender, RoutedEventArgs e)
        {
            if (_busy)
            {
                return;
            }
            _ = SyncAsync(true);
        }

        // ---------- 切换 ----------

        private async void SwitchToOfficialButton_Click(object sender, RoutedEventArgs e)
        {
            await SwitchAsync(ServerChannel.Official);
        }

        private async void SwitchToBButton_Click(object sender, RoutedEventArgs e)
        {
            await SwitchAsync(ServerChannel.Bilibili);
        }

        private async Task SwitchAsync(ServerChannel target)
        {
            if (_busy)
            {
                return;
            }
            if (string.IsNullOrEmpty(_gamePath) || !HasConfig(_gamePath))
            {
                MessageBox.Show(this, "请先选择正确的游戏目录。", "无法切换",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_report == null)
            {
                await SyncAsync(false);
            }

            SyncReport report = _report;
            bool versionMatches = report == null || report.VersionMatches;
            if (report != null && !report.VersionMatches && !string.IsNullOrEmpty(report.LocalVersion))
            {
                MessageBoxResult answer = MessageBox.Show(this,
                    "本地游戏版本 " + report.LocalVersion + " 与线上版本 " + report.OfficialVersion + " 不一致。\n\n" +
                    "为避免文件版本错配，这次只会修改配置文件，不会替换游戏文件。\n" +
                    "建议先启动一次启动器完成更新，再回来切换。\n\n是否继续？",
                    "版本不一致", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            ChannelCacheReport cacheReport = report == null ? null : report.ForChannel(target);
            bool cacheReady = versionMatches && cacheReport != null && cacheReport.Ready;

            if (!cacheReady)
            {
                MessageBoxResult answer = MessageBox.Show(this,
                    target.DisplayName + " 的资源缓存还没有准备好。\n\n" +
                    "选择「是」现在联网下载（官服约 55 MB / B服约 241 MB），\n" +
                    "选择「否」只修改配置文件（游戏文件不会被替换，体积也不会减少）。\n\n是否现在下载？",
                    "资源缓存不完整", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer == MessageBoxResult.Yes)
                {
                    await SyncAsync(true);
                    report = _report;
                    versionMatches = report == null || report.VersionMatches;
                    cacheReport = report == null ? null : report.ForChannel(target);
                    cacheReady = versionMatches && cacheReport != null && cacheReport.Ready;
                }
            }

            if (!cacheReady)
            {
                MessageBoxResult answer = MessageBox.Show(this,
                    "现在只能修改配置文件，游戏里仍会保留另一个渠道的文件。\n\n是否继续？",
                    "仅修改配置", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            if (ProcessGuard.AnyRunning(_gamePath))
            {
                MessageBoxResult answer = MessageBox.Show(this,
                    "切换前需要关闭《异环》启动器和游戏。\n\n是否现在关闭并继续？",
                    "需要关闭游戏", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            CacheState state = cacheReady && report != null ? report.State : null;

            SetBusy(true, "正在切换到" + target.DisplayName + "…");
            _cancellation = new CancellationTokenSource();
            var progress = new Progress<SyncProgress>(OnProgress);
            SwitchReport result = null;

            try
            {
                result = await Task.Run(delegate
                {
                    return _switchService.Apply(_gamePath, target, state, progress, _cancellation.Token);
                });
            }
            catch (OperationCanceledException)
            {
                SetStatus("已取消。");
            }
            catch (Exception ex)
            {
                _log.Exception("切换服务器时出错：", ex);
                SetStatus("切换失败，详见日志。");
                MessageBox.Show(this, "切换失败：" + ex.Message + "\n\n详细信息见日志窗口。",
                                "切换失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (_cancellation != null)
                {
                    _cancellation.Dispose();
                    _cancellation = null;
                }
                SetBusy(false, null);
                UpdateServerStatus();
            }

            if (result == null)
            {
                return;
            }

            SetStatus(result.Message);
            if (result.Failed > 0)
            {
                MessageBox.Show(this,
                                result.Message + "\n\n有 " + result.Failed + " 个文件处理失败，详情见日志窗口。",
                                "部分文件处理失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ---------- 其他窗口 ----------

        private void LogButton_Click(object sender, RoutedEventArgs e)
        {
            ShowLogWindow();
        }

        /// <summary>打开（或激活）日志窗口。自检代码也走这里，保证测的是同一条路径。</summary>
        internal LogWindow ShowLogWindow()
        {
            if (_logWindow == null)
            {
                _logWindow = new LogWindow();
                _logWindow.Owner = this;
            }
            _logWindow.Show();
            _logWindow.Activate();
            return _logWindow;
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new AboutWindow { Owner = this };
            window.ShowDialog();
        }
    }
}
