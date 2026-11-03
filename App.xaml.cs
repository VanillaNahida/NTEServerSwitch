using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using NTEServerSwitch.Core;

namespace NTEServerSwitch
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            LogService log = LogService.Instance;
            log.Initialize();

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            log.Info("异环切服器启动，版本 " + typeof(App).Assembly.GetName().Version);

            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogService.Instance.Exception("界面线程未处理异常：", e.Exception);
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
