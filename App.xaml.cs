using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using NTEServerSwitch.Core;
using System.Linq;

namespace NTEServerSwitch
{
    public partial class App : Application
    {
        private bool _smokeTest;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _smokeTest = e.Args != null &&
                         e.Args.Any(a => string.Equals(a, "--smoke-test", StringComparison.OrdinalIgnoreCase));

            LogService log = LogService.Instance;
            log.Initialize();

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            log.Info("异环切服器启动，版本 " + typeof(App).Assembly.GetName().Version);

            var window = new MainWindow();
            MainWindow = window;
            window.Show();

            if (_smokeTest)
            {
                RunSmokeTest(window);
            }
        }

        /// <summary>用 --smoke-test 启动时做的自检：开一个日志窗口并强制布局，跑完直接退出。
        /// 用来验证"发布出来的产物"能不能真正开窗（例如 InvariantGlobalization 会让 WPF 排版崩溃）。</summary>
        private void RunSmokeTest(MainWindow main)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    LogService.Instance.Info("自检：主窗口已显示，准备打开日志窗口。");
                    LogWindow logWindow = main.ShowLogWindow();
                    logWindow.UpdateLayout();
                    LogService.Instance.Info("自检：日志窗口排版正常。");
                    logWindow.ForceClose();
                    LogService.Instance.Info("自检通过。");
                    Shutdown(0);
                }
                catch (Exception ex)
                {
                    LogService.Instance.Exception("自检失败：", ex);
                    Shutdown(2);
                }
            }), DispatcherPriority.ApplicationIdle);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogService.Instance.Exception("界面线程未处理异常：", e.Exception);
            if (_smokeTest)
            {
                // 自检模式不能弹窗等人点，直接以非零码退出
                Shutdown(3);
                e.Handled = true;
                return;
            }
            MessageBox.Show("程序遇到未处理的错误：\n" + e.Exception.Message +
                            "\n\n详细信息已写入日志，可在日志窗口导出。",
                            "出错了", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            LogService.Instance.Exception("后台线程未处理异常：", exception);
        }

        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            LogService.Instance.Exception("未观察的任务异常：", e.Exception);
            e.SetObserved();
        }
    }
}
