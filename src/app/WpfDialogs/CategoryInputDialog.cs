using System.Windows;
using System.Windows.Controls;

namespace LabelReviewer.WpfDialogs;

public sealed class CategoryInputDialog : Window
{
    private readonly TextBox _category = new();
    public string Category => _category.Text.Trim();

    public CategoryInputDialog()
    {
        Title = "添加类别";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI");
        FontSize = 13.5;

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = "新类别名称：" });
        _category.Margin = new Thickness(0, 6, 0, 12);
        panel.Children.Add(_category);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var ok = new Button { Content = "添加", IsDefault = true, MinWidth = 78 };
        ok.Click += Confirm;
        var cancel = new Button
        {
            Content = "取消", IsCancel = true, MinWidth = 78,
            Margin = new Thickness(8, 0, 0, 0)
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => _category.Focus();
    }

    private void Confirm(object sender, RoutedEventArgs e)
    {
        if (Category.Length == 0)
        {
            MessageBox.Show(this, "请输入类别名称。", "类别不能为空",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }
}
