using System.Collections.Generic;
using System.Windows;

namespace LogCollector
{
    public partial class ContactsWindow : Window
    {
        public ContactsWindow()
        {
            InitializeComponent();

            // 模拟一些联系人数据
            var contacts = new List<Contact>
            {
                new Contact { Name = "张三", Phone = "123-456-7890", Email = "zhangsan@example.com" },
                new Contact { Name = "李四", Phone = "234-567-8901", Email = "lisi@example.com" },
                new Contact { Name = "王五", Phone = "345-678-9012", Email = "wangwu@example.com" }
            };

            // 将联系人数据绑定到 ListView
            ContactsListView.ItemsSource = contacts;
        }
    }

    public class Contact
    {
        public string Name { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
    }
}
