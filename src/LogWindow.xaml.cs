using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using NTEServerSwitch.Core;

namespace NTEServerSwitch
{
    /// <summary>独立的非模态日志窗口：关掉只是隐藏，不阻塞主窗口。</summary>
    public partial class LogWindow : Window
    {
        private readonly LogService _log = LogService.Instance;
        private bool _allowClose;

        public LogWindow()
        {
            InitializeComponent();

            LogList.ItemsSource = _log.Entries;
            _log.Entries.CollectionChanged += OnEntriesChanged;

            Loaded += delegate
            {
                UpdateCount();
                ScrollToEnd();
            };
            Closing += OnClosing;
            Closed += delegate { _log.Entries.CollectionChanged -= OnEntriesChanged; };
        }

        /// <summary>真正关闭（主窗口退出时调用）。</summary>
        public void ForceClose()
        {
            _allowClose = true;
            Close();
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_allowClose)
            {
                return;
            }
            e.Cancel = true;
            Hide();
        }

        private void OnEntriesChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateCount();
            ScrollToEnd();
        }

        private void UpdateCount()
        {
            CountText.Text = "共 " + _log.Entries.Count + " 条记录 · 文件：" +
                             (_log.FilePath ?? "未初始化");
        }

        private void ScrollToEnd()
        {
            int count = _log.Entries.Count;
            if (count == 0)
            {
                return;
            }
            try
            {
                LogList.ScrollIntoView(_log.Entries[count - 1]);
            }
            catch
            {
                // 视图尚未生成时忽略。
            }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "导出日志",
                Filter = "日志文件 (*.log)|*.log|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                FileName = "异环切服器日志-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log",
                InitialDirectory = AppPaths.LogDirectory
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            if (_log.ExportTo(dialog.FileName))
            {
                MessageBox.Show(this, "日志已导出到：\n" + dialog.FileName, "导出成功",
                                MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(this, "导出失败，请检查目标路径是否可写。", "导出失败",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            var builder = new StringBuilder();
            foreach (LogEntry entry in _log.Entries)
            {
                builder.AppendLine(entry.ToLine());
            }

            if (builder.Length == 0)
            {
                MessageBox.Show(this, "当前没有日志。", "提示",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Clipboard.SetText(builder.ToString());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "复制到剪贴板失败：" + ex.Message, "复制失败",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            string directory = AppPaths.LogDirectory;
            try
            {
                Directory.CreateDirectory(directory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "\"" + directory + "\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "打开日志目录失败：" + ex.Message, "打开失败",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            _log.Clear();
            UpdateCount();
        }
    }
}
