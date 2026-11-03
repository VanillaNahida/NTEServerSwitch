using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using NTEServerSwitch.Core;

namespace NTEServerSwitch
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            // 版本号只认程序集，不再手改这里的字面量
            VersionText.Text = "版本：" + UpdateChecker.CurrentVersionText;
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
