using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Renci.SshNet;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32; // 用于文件对话框

namespace LogCollector
{
    public partial class MainWindow : Window
    {
        public class ServerConfig
        {
            public string Name { get; set; } // 新增配置名称
            public bool IsSelected { get; set; } // 新增用于多选的属性
            public string IP { get; set; }
            public string Username { get; set; }
            public string Password { get; set; }
            public string LogPaths { get; set; }
            public int TailLines { get; set; } = 50; // 默认50行
        }

        public class LogEntry
        {
            public string ServerIP { get; set; }
            public DateTime Timestamp { get; set; }
            public string Content { get; set; }
        }

        private ObservableCollection<ServerConfig> _servers = new ObservableCollection<ServerConfig>();
        private ObservableCollection<LogEntry> _logs = new ObservableCollection<LogEntry>();

        public MainWindow()
        {

            // 显示启动界面
            ShowSplashScreen();

            InitializeComponent();
            LoadConfig();
            ServerGrid.ItemsSource = _servers;
            ServerFilter.ItemsSource = _servers;
            LogDataGrid.ItemsSource = _logs;
            //MessageBox.Show("窗口已加载");

        }

        private async void ShowSplashScreen()
        {
            // 创建并显示启动窗口
            SplashWindow splash = new SplashWindow();
            splash.Show();

            // 延迟2秒钟模拟加载时间
            await Task.Delay(3000);

            // 关闭启动界面，显示主窗口
            splash.Close();
        }


        private void LoadConfig()
        {
            try
            {
                if (File.Exists("config.json"))
                {
                    var json = File.ReadAllText("config.json");
                    var loadedServers = JsonSerializer.Deserialize<ObservableCollection<ServerConfig>>(json);
                    _servers.Clear();
                    foreach (var server in loadedServers)
                    {
                        _servers.Add(server);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载配置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var json = JsonSerializer.Serialize(_servers);
                File.WriteAllText("config.json", json);
                MessageBox.Show("配置保存成功", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存配置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TestConnection_Click(object sender, RoutedEventArgs e)
        {
            if (ServerGrid.SelectedItem is ServerConfig server)
            {
                try
                {
                    if (IsLocalIP(server.IP))
                    {
                        MessageBox.Show("本地连接测试成功", "测试结果", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        using (var client = new SshClient(server.IP, server.Username, server.Password))
                        {
                            client.Connect();
                            MessageBox.Show("SSH连接测试成功", "测试结果", MessageBoxButton.OK, MessageBoxImage.Information);
                            client.Disconnect();
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"连接测试失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("请先选择一个服务器", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void CollectLogs_Click(object sender, RoutedEventArgs e)
        {
            if (_servers.Count == 0)
            {
                MessageBox.Show("请先添加服务器配置", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _logs.Clear();
            LogTab.IsSelected = true; // 切换到日志标签页

            var progress = new Progress<string>(msg =>
            {
                _logs.Add(new LogEntry
                {
                    ServerIP = "系统",
                    Timestamp = DateTime.Now,
                    Content = msg
                });
            });

            await Task.Run(() => CollectAllLogs(progress));
        }

        private void CollectAllLogs(IProgress<string> progress)
        {
            var selectedServers = _servers.Where(s => s.IsSelected).ToList();
            if (selectedServers.Count == 0)
            {
                MessageBox.Show("请先选择要操作的服务器", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            foreach (var server in selectedServers)
            {
                if (string.IsNullOrWhiteSpace(server.IP)) continue;

                var logPaths = server.LogPaths?
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .ToList();

                if (logPaths == null || logPaths.Count == 0)
                {
                    progress.Report($"服务器 {server.IP} 没有配置日志路径");
                    continue;
                }

                foreach (var path in logPaths.Select(ResolveLogPath))
                {
                    try
                    {
                        if (IsLocalIP(server.IP))
                        {
                            if (!File.Exists(path))
                            {
                                progress.Report($"服务器 {server.IP} 日志文件不存在: {path}");
                                continue;
                            }

                            var tailLines = server.TailLines > 0 ? server.TailLines : 50; // 默认50
                            var lines = File.ReadLines(path).TakeLast(tailLines);
                            foreach (var line in lines)
                            {
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    _logs.Add(new LogEntry
                                    {
                                        ServerIP = server.IP,
                                        Timestamp = DateTime.Now,
                                        Content = $"[{path}] {line}"
                                    });
                                });
                            }
                            progress.Report($"已收集 {server.IP} 的日志: {path}");
                        }
                        else
                        {
                            using (var client = new SshClient(server.IP, server.Username, server.Password))
                            {
                                client.Connect();
                                var tailLines = server.TailLines > 0 ? server.TailLines : 50; // 默认50
                                var cmd = client.CreateCommand($"tail -n {tailLines} {path}");
                                var result = cmd.Execute();

                                foreach (var line in result.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)))
                                {
                                    Application.Current.Dispatcher.Invoke(() =>
                                    {
                                        _logs.Add(new LogEntry
                                        {
                                            ServerIP = server.IP,
                                            Timestamp = DateTime.Now,
                                            Content = $"[{path}] {line}"
                                        });
                                    });
                                }
                                progress.Report($"已收集 {server.IP} 的日志: {path}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        progress.Report($"服务器 {server.IP} 收集日志失败({path}): {ex.Message}");
                    }
                }
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show($"日志收集完成，共收集 {_logs.Count} 条日志", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }

        private void SearchLogs_Click(object sender, RoutedEventArgs e)
        {
            var keyword = SearchTextBox.Text;
            var selectedServer = ServerFilter.SelectedItem as ServerConfig;

            var filtered = _logs.Where(log =>
                (selectedServer == null || log.ServerIP == selectedServer.IP) &&
                (string.IsNullOrEmpty(keyword) || log.Content.Contains(keyword))
            ).ToList();

            LogDataGrid.ItemsSource = filtered;
        }

        private bool IsLocalIP(string ip)
        {
            return ip == "127.0.0.1" || ip == "localhost" ||
                   ip.Equals(System.Net.Dns.GetHostName(), StringComparison.OrdinalIgnoreCase);
        }
        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox passwordBox && passwordBox.Tag is ServerConfig server)
            {
                server.Password = passwordBox.Password;
            }
        }



        private void ImportConfig_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("开始导入配置");
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "JSON 文件 (*.json)|*.json",
                Title = "导入服务器配置"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    string json = File.ReadAllText(openFileDialog.FileName);
                    var importedServers = JsonSerializer.Deserialize<ObservableCollection<ServerConfig>>(json);

                    if (importedServers != null)
                    {
                        _servers.Clear();
                        foreach (var server in importedServers)
                        {
                            _servers.Add(server);
                        }
                        MessageBox.Show("配置导入成功", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导入失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExportConfig_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "JSON 文件 (*.json)|*.json",
                Title = "导出服务器配置",
                FileName = "config.json"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    string json = JsonSerializer.Serialize(_servers, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(saveFileDialog.FileName, json);
                    MessageBox.Show("配置导出成功", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ShowAbout_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("EasyLog v1.0\n分布式日志采集工具 \n联系方式xagent@126.com", "关于", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void DeleteServer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ServerConfig server)
            {
                if (MessageBox.Show($"确定要删除服务器 {server.Name} ({server.IP}) 吗？",
                    "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    _servers.Remove(server);
                    MessageBox.Show("服务器已删除", "删除成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void AddServer_Click(object sender, RoutedEventArgs e)
        {
            // 创建一个新的服务器配置对象
            var newServer = new ServerConfig
            {
                Name = "新服务器",
                IP = "127.0.0.1",
                Username = "root",
                Password = "",
                LogPaths = "/var/log/syslog-{YYYY}-{MM}-{DD}.log"
            };

            // 添加到服务器列表
            _servers.Add(newServer);

            // 选中新添加的服务器（如果需要）
            ServerGrid.SelectedItem = newServer;
        }

        private string ResolveLogPath(string path)
        {
            var now = DateTime.Now;
            var replacements = new Dictionary<string, string>
            {
                { "{YYYY}", now.ToString("yyyy") },
                { "{yyyy}", now.ToString("yyyy") },
                { "{MM}", now.ToString("MM") },
                { "{mm}", now.ToString("MM") },
                { "{DD}", now.ToString("dd") },
                { "{dd}", now.ToString("dd") },
                { "{HH}", now.ToString("HH") },
                { "{hh}", now.ToString("HH") },
                { "{YYYYMMDD}", now.ToString("yyyyMMdd") },
                { "{yyyymmdd}", now.ToString("yyyyMMdd") }
            };

            foreach (var kvp in replacements)
            {
                path = path.Replace(kvp.Key, kvp.Value);
            }

            return path;
        }

        // 关闭窗口时弹出确认框
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // 弹出确认框
            var result = MessageBox.Show("确定要关闭应用吗？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.No)
            {
                // 如果用户点击了 No，取消关闭操作
                e.Cancel = true;
            }
            else
            {
                // 用户点击 Yes，继续关闭
                e.Cancel = false;
            }
        }

    }
}