using System;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows;

namespace LogCollector
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            

            bool isAllowed = await CheckUsagePermission();


            if (!isAllowed)
            {
                MessageBox.Show("当前应用您无权使用，无法启动！", "权限错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown(); // 直接关闭应用
            }
        }

        private async Task<bool> CheckUsagePermission()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(2); // 设置 1 秒超时

                    string macAddress = GetMacAddress();
                    if (string.IsNullOrEmpty(macAddress))
                    {
                        MessageBox.Show("无法获取MAC地址，无法验证使用权限！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return false;
                    }

                    // 创建请求的URL，并附加MAC地址作为查询参数
                    string url = $"https://shellbook.com.cn/check?mac={macAddress}";

                    HttpResponseMessage response = await client.GetAsync(url);

                    if (response.IsSuccessStatusCode)
                    {
                        string result = await response.Content.ReadAsStringAsync();
                        return result.Trim() == "allow"; // 服务器返回 "allow" 则允许启动
                    }
                }
            }
            catch (TaskCanceledException)
            {
                // 超时情况（TaskCanceledException 可能因为超时或取消请求）
                return true; // 默认允许启动
            }
            catch (Exception)
            {
                // 其他错误，比如网络问题，默认允许启动
                return true;
            }

            return false;
        }


        private string GetMacAddress()
        {
            var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var networkInterface in networkInterfaces)
            {
                // 只获取有效的网络接口（忽略虚拟网卡等）
                if (networkInterface.OperationalStatus == OperationalStatus.Up && networkInterface.GetPhysicalAddress() != PhysicalAddress.None)
                {
                    return networkInterface.GetPhysicalAddress().ToString();
                }
            }

            return string.Empty; // 如果没有找到有效的MAC地址，则返回空字符串
        }
    }
}
