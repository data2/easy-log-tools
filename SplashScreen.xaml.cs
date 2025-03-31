using System;
using System.Threading.Tasks;
using System.Windows;

namespace LogCollector
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
        }

        // 确保事件绑定正确，事件触发时调用
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 将进度条的最小值和最大值设置好
            progressBar.Minimum = 0;
            progressBar.Maximum = 100;

            // 模拟启动进度，假设加载过程分为 5 步
            for (int i = 0; i <= 5; i++)
            {
                // 使用 Dispatcher 更新 UI 线程上的进度条
                progressBar.Dispatcher.Invoke(() =>
                {
                    progressBar.Value = i * 20;  // 更新进度条的值
                    progressText.Text = $"{i * 20}%";  // 更新显示的进度值
                });

                await Task.Delay(600); // 模拟每步耗时 0.8 秒
            }

            // 加载完成后，关闭启动窗口
            this.Close();
        }
    }
}
