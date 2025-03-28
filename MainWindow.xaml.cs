using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Renci.SshNet;
using System.Text.Json;
using System.Threading.Tasks;

namespace LogCollector
{
    public partial class MainWindow : Window
    {
        public class ServerConfig
        {
            public string IP { get; set; }
            public string Username { get; set; }
            public string Password { get; set; }
            public string LogPaths { get; set; }
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
            InitializeComponent();
            LoadConfig();
            ServerGrid.ItemsSource = _servers;
            ServerFilter.ItemsSource = _servers;
            LogDataGrid.ItemsSource = _logs;
            //MessageBox.Show("窗口已加载");

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
            foreach (var server in _servers)
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

                foreach (var path in logPaths)
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

                            var lines = File.ReadLines(path).TakeLast(50);
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
                                var cmd = client.CreateCommand($"tail -n 50 {path}");
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


        // 显示联系人窗口
        private void ShowContacts_Click(object sender, RoutedEventArgs e)
        {
            ContactsWindow contactsWindow = new ContactsWindow();
            contactsWindow.ShowDialog(); // 弹出联系人窗口
        }
    }
}