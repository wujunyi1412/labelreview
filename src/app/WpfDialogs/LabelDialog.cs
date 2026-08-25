using System.Windows;
using System.Windows.Controls;

namespace LabelReviewer.WpfDialogs;

public sealed class LabelDialog : Window
{
    private readonly ComboBox _reviewType = new();
    private readonly ComboBox _category = new();

    public string ReviewType => _reviewType.SelectedItem?.ToString() ?? "误检";
    public string Category => _category.Text.Trim();

    public LabelDialog(IEnumerable<string> categories, string? preferredCategory,
        string? preferredReviewType)
    {
        Title = "选择标注标签";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI");
        FontSize = 13.5;

        var layout = new Grid { Margin = new Thickness(18) };
        for (var index = 0; index < 5; index++)
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Insert(4, new RowDefinition { Height = new GridLength(12) });

        layout.Children.Add(MakeLabel("问题类型：", 0));
        _reviewType.ItemsSource = new[] { "误检", "漏检" };
        _reviewType.SelectedItem = preferredReviewType is "误检" or "漏检"
            ? preferredReviewType
            : "误检";
        _reviewType.Margin = new Thickness(0, 5, 0, 14);
        Grid.SetRow(_reviewType, 1);
        layout.Children.Add(_reviewType);

        layout.Children.Add(MakeLabel("类别（可直接输入新类别）：", 2));
        _category.IsEditable = true;
        _category.ItemsSource = categories.ToList();
        _category.Text = preferredCategory ?? string.Empty;
        _category.Margin = new Thickness(0, 5, 0, 0);
        Grid.SetRow(_category, 3);
        layout.Children.Add(_category);

        var buttons = MakeButtons("确定", Confirm);
        Grid.SetRow(buttons, 5);
        layout.Children.Add(buttons);
        Content = layout;
        Loaded += (_, _) => _category.Focus();
    }

    private void Confirm(object sender, RoutedEventArgs e)
    {
        if (Category.Length == 0)
        {
            MessageBox.Show(this, "请输入或选择一个类别。", "类别不能为空",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }

    private static TextBlock MakeLabel(string text, int row)
    {
        var label = new TextBlock { Text = text };
        Grid.SetRow(label, row);
        return label;
    }

    private static StackPanel MakeButtons(string okText, RoutedEventHandler confirm)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var ok = new Button { Content = okText, IsDefault = true, MinWidth = 78 };
        ok.Click += confirm;
        var cancel = new Button
        {
            Content = "取消", IsCancel = true, MinWidth = 78,
            Margin = new Thickness(8, 0, 0, 0)
        };
        panel.Children.Add(ok);
        panel.Children.Add(cancel);
        return panel;
    }
}
