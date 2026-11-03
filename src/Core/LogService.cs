using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace NTEServerSwitch.Core
{
    public enum LogLevel
    {
        Info = 0,
        Success = 1,
        Warning = 2,
        Error = 3
    }

    public sealed class LogEntry
    {
        private static readonly Brush InfoBrush = Frozen("#FF444444");
        private static readonly Brush SuccessBrush = Frozen("#FF2E9B4F");
        private static readonly Brush WarningBrush = Frozen("#FFB26B00");
        private static readonly Brush ErrorBrush = Frozen("#FFC62F2F");

        public DateTime Time { get; set; }

        public LogLevel Level { get; set; }

        public string Message { get; set; }

        public string TimeText
        {
            get { return Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture); }
        }

        public string LevelText
        {
            get
            {
                switch (Level)
                {
                    case LogLevel.Success:
                        return "成功";
                    case LogLevel.Warning:
                        return "警告";
                    case LogLevel.Error:
                        return "错误";
                    default:
                        return "信息";
                }
            }
        }

        public Brush LevelBrush
        {
            get
            {
                switch (Level)
                {
                    case LogLevel.Success:
                        return SuccessBrush;
                    case LogLevel.Warning:
                        return WarningBrush;
                    case LogLevel.Error:
                        return ErrorBrush;
                    default:
                        return InfoBrush;
                }
            }
        }

        public string ToLine()
        {
            return Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                   + " [" + LevelText + "] " + Message;
        }

        private static Brush Frozen(string color)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>
    /// 全局日志：内存里一份给日志窗口看，磁盘上一份给开发者分析。
    /// 任何异常都不允许从日志里抛出去。
    /// </summary>
    public sealed class LogService
    {
        public const int MaxEntries = 4000;

        private static readonly LogService Shared = new LogService();
        private static readonly object FileLock = new object();

        private readonly ObservableCollection<LogEntry> _entries = new ObservableCollection<LogEntry>();
        private string _filePath;

        private LogService()
        {
        }

        public static LogService Instance
        {
            get { return Shared; }
        }

        public ObservableCollection<LogEntry> Entries
        {
            get { return _entries; }
        }

        public string FilePath
        {
            get { return _filePath; }
        }

        public bool HasError { get; private set; }

        public bool HasWarning { get; private set; }

        public event EventHandler EntryAdded;

        public void Initialize()
        {
            try
            {
                AppPaths.EnsureDirectory(AppPaths.LogDirectory);
                _filePath = Path.Combine(AppPaths.LogDirectory,
                                         "app-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".log");
                File.AppendAllText(_filePath,
                                   "===== 异环切服器 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " =====\r\n",
                                   new UTF8Encoding(true));
            }
            catch
            {
                _filePath = null;
            }
        }

        public void Info(string message)
        {
            Write(LogLevel.Info, message);
        }

        public void Success(string message)
        {
            Write(LogLevel.Success, message);
        }

        public void Warn(string message)
        {
            Write(LogLevel.Warning, message);
        }

        public void Error(string message)
        {
            Write(LogLevel.Error, message);
        }

        public void Exception(string context, Exception exception)
        {
            string detail = exception == null ? "未知异常" : exception.GetType().Name + "：" + exception.Message;
            Write(LogLevel.Error, context + " " + detail);
            if (exception != null && exception.StackTrace != null)
            {
                AppendText(exception.StackTrace);
            }
        }

        public void Write(LogLevel level, string message)
        {
            var entry = new LogEntry
            {
                Time = DateTime.Now,
                Level = level,
                Message = message ?? string.Empty
            };

            if (level == LogLevel.Error)
            {
                HasError = true;
            }
            else if (level == LogLevel.Warning)
            {
                HasWarning = true;
            }

            RunOnUiThread(delegate
            {
                _entries.Add(entry);
                while (_entries.Count > MaxEntries)
                {
                    _entries.RemoveAt(0);
                }
                var handler = EntryAdded;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            });

            AppendText(entry.ToLine());
        }

        public void Clear()
        {
            RunOnUiThread(delegate { _entries.Clear(); });
        }

        public bool ExportTo(string path)
        {
            try
            {
                var builder = new StringBuilder();
                builder.AppendLine("===== 异环切服器日志导出 " +
                                   DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " =====");
                builder.AppendLine("程序目录：" + AppPaths.BaseDirectory);
                builder.AppendLine("系统：" + Environment.OSVersion + " / " + Environment.Version);
                builder.AppendLine();
                foreach (LogEntry entry in _entries.ToArray())
                {
                    builder.AppendLine(entry.ToLine());
                }
                File.WriteAllText(path, builder.ToString(), new UTF8Encoding(true));
                return true;
            }
            catch (Exception ex)
            {
                Write(LogLevel.Error, "导出日志失败：" + ex.Message);
                return false;
            }
        }

        private void AppendText(string text)
        {
            string path = _filePath;
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(text))
            {
                return;
            }
            try
            {
                lock (FileLock)
                {
                    File.AppendAllText(path, text + "\r\n", new UTF8Encoding(false));
                }
            }
            catch
            {
                // 日志写不进去不影响主流程。
            }
        }

        private static void RunOnUiThread(Action action)
        {
            Application app = Application.Current;
            if (app == null || app.Dispatcher == null)
            {
                action();
                return;
            }
            try
            {
                if (app.Dispatcher.CheckAccess())
                {
                    action();
                }
                else
                {
                    app.Dispatcher.BeginInvoke(new Action(action));
                }
            }
            catch
            {
                action();
            }
        }
    }
}
