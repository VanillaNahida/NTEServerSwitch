using System;
using System.IO;
using System.Diagnostics;
using Microsoft.Win32;
using System.Windows;

namespace NTEServerSwitch
{
    public partial class MainWindow : Window
    {
        private const string RegistryKey = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\YH";
        private const string ConfigFileName = "Config.ini";
        private string _gamePath = string.Empty;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            DetectGamePath();
        }

        private void DetectGamePath()
        {
            string installLocation = GetInstallLocationFromRegistry();
            if (!string.IsNullOrEmpty(installLocation))
            {
                string configPath = Path.Combine(installLocation, "NTELauncher", "Config", ConfigFileName);
                if (File.Exists(configPath))
                {
                    _gamePath = installLocation;
                    GamePathTextBox.Text = _gamePath;
                    StatusInfoText.Text = $"已自动检测到游戏安装路径：{_gamePath}";
                    UpdateServerStatus();
                    return;
                }
            }
            StatusInfoText.Text = "未检测到游戏安装路径，请手动选择";
            MessageBox.Show("未检测到游戏安装路径，请手动选择游戏安装目录。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private string GetInstallLocationFromRegistry()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(RegistryKey))
                {
                    if (key != null)
                    {
                        return key.GetValue("InstallLocation") as string;
                    }
                }
            }
            catch
            {
            }
            return string.Empty;
        }

        private void SelectPathButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "选择游戏安装目录";
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _gamePath = dialog.SelectedPath;
                GamePathTextBox.Text = _gamePath;
                StatusInfoText.Text = $"已选择游戏安装路径：{_gamePath}";
                UpdateServerStatus();
            }
        }

        private void UpdateServerStatus()
        {
            string configPath = Path.Combine(_gamePath, "NTELauncher", "Config", ConfigFileName);
            if (File.Exists(configPath))
            {
                string content = File.ReadAllText(configPath);
                if (content.Contains("publish_ob_B") || content.Contains("Branch=publish_ob_B"))
                {
                    ServerStatusText.Text = "当前服务器：B服";
                    ServerStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x65, 0x97));
                }
                else
                {
                    ServerStatusText.Text = "当前服务器：官服";
                    ServerStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
                }
            }
        }

        private void SwitchToOfficialButton_Click(object sender, RoutedEventArgs e)
        {
            SwitchServer(true);
        }

        private void SwitchToBButton_Click(object sender, RoutedEventArgs e)
        {
            SwitchServer(false);
        }

        private void SwitchServer(bool isOfficial)
        {
            if (string.IsNullOrEmpty(_gamePath))
            {
                MessageBox.Show("请先选择游戏安装路径。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string configPath = Path.Combine(_gamePath, "NTELauncher", "Config", ConfigFileName);

            if (!File.Exists(configPath))
            {
                MessageBox.Show("配置文件不存在，请确认游戏安装路径正确。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!CheckAndCloseProcesses())
            {
                return;
            }

            try
            {
                string content = File.ReadAllText(configPath);

                if (isOfficial)
                {
                    content = content.Replace("Partner=123\r\n", "");
                    content = content.Replace("Partner=123\n", "");
                    content = content.Replace("publish_ob_B", "publish_ob");
                    content = content.Replace("serialNumber=2", "serialNumber=1");
                    content = content.Replace("Branch=publish_ob_B", "Branch=publish_ob");
                }
                else
                {
                    if (!content.Contains("Partner=123"))
                    {
                        content = content.Replace("[General]\r\n", "[General]\r\nPartner=123\r\n");
                        content = content.Replace("[General]\n", "[General]\nPartner=123\n");
                    }
                    content = content.Replace("publish_ob/", "publish_ob_B/");
                    content = content.Replace("serialNumber=1", "serialNumber=2");
                    content = content.Replace("Branch=publish_ob", "Branch=publish_ob_B");
                }

                File.WriteAllText(configPath, content);
                UpdateServerStatus();
                string serverName = isOfficial ? "官服" : "B服";
                StatusInfoText.Text = $"切换{serverName}成功！";
                MessageBox.Show($"已切换到{serverName}！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusInfoText.Text = $"切换失败：{ex.Message}";
                MessageBox.Show($"切换失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool CheckAndCloseProcesses()
        {
            string[] processNames = { "NTELauncher", "HTGame", "NTEGame" };
            bool hasRunningProcess = false;

            foreach (string processName in processNames)
            {
                Process[] processes = Process.GetProcessesByName(processName);
                if (processes.Length > 0)
                {
                    hasRunningProcess = true;
                    break;
                }
            }

            if (!hasRunningProcess)
            {
                return true;
            }

            MessageBoxResult result = MessageBox.Show(
                "检测到游戏或启动器相关进程正在运行。\n请先关闭这些进程，否则切换可能失败。\n\n是否强制关闭这些进程？",
                "提示",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                foreach (string processName in processNames)
                {
                    Process[] processes = Process.GetProcessesByName(processName);
                    foreach (Process process in processes)
                    {
                        try
                        {
                            process.Kill();
                            process.WaitForExit(3000);
                        }
                        catch
                        {
                        }
                    }
                }
                return true;
            }
            else
            {
                MessageBox.Show("请手动关闭游戏进程后再尝试切换。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            AboutWindow aboutWindow = new AboutWindow();
            aboutWindow.ShowDialog();
        }
    }
}
