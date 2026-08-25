using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace LabelReviewer.WpfDialogs;

public sealed class ScanScopeDialog : Window
{
    private const int MaximumSelectableLevel = 20;
    private readonly string _root;
    private readonly ComboBox _level = new();
    private readonly ListBox _folders = new();
    private readonly TextBlock _status = new();
    private List<FolderOption> _options = [];

    public int SelectedLevel => _level.SelectedIndex;
    public IReadOnlyList<string> SelectedFolders => _options
        .Where(option => option.IsSelected)
        .Select(option => option.FullPath)
        .ToList();

    public ScanScopeDialog(string root)
    {
        _root = Path.GetFullPath(root);
        Title = "选择扫描范围";
        Width = 620;
        Height = 560;
        MinWidth = 520;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI");
        FontSize = 13.5;

        var rootGrid = new Grid { Margin = new Thickness(18) };
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var description = new TextBlock
        {
            Text = "选择目录层级，再勾选需要递归扫描的目录。第 0 级表示扫描整个输入根目录。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        rootGrid.Children.Add(description);

        var levelLine = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        levelLine.Children.Add(new TextBlock
        {
            Text = "目录层级：",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        for (var value = 0; value <= MaximumSelectableLevel; value++)
            _level.Items.Add(value == 0 ? "第 0 级（整个根目录）" : $"第 {value} 级目录");
        _level.MinWidth = 200;
        _level.SelectionChanged += (_, _) => LoadSelectedLevel();
        levelLine.Children.Add(_level);
        Grid.SetRow(levelLine, 1);
        rootGrid.Children.Add(levelLine);

        var selectionButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        var selectAll = new Button { Content = "全选", MinWidth = 72 };
        selectAll.Click += (_, _) => SetAll(true);
        var clearAll = new Button
        {
            Content = "全不选", MinWidth = 72,
            Margin = new Thickness(8, 0, 0, 0)
        };
        clearAll.Click += (_, _) => SetAll(false);
        selectionButtons.Children.Add(selectAll);
        selectionButtons.Children.Add(clearAll);
        Grid.SetRow(selectionButtons, 2);
        rootGrid.Children.Add(selectionButtons);

        var template = new DataTemplate(typeof(FolderOption));
        var checkBox = new FrameworkElementFactory(typeof(CheckBox));
        checkBox.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(FolderOption.IsSelected))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        checkBox.SetBinding(ContentControl.ContentProperty,
            new Binding(nameof(FolderOption.RelativePath)));
        checkBox.SetValue(FrameworkElement.MarginProperty, new Thickness(3));
        template.VisualTree = checkBox;
        _folders.ItemTemplate = template;
        _folders.BorderThickness = new Thickness(1);
        Grid.SetRow(_folders, 3);
        rootGrid.Children.Add(_folders);

        _status.Margin = new Thickness(0, 8, 0, 5);
        _status.Foreground = System.Windows.Media.Brushes.DimGray;
        Grid.SetRow(_status, 4);
        rootGrid.Children.Add(_status);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var ok = new Button { Content = "开始扫描", IsDefault = true, MinWidth = 96 };
        ok.Click += Confirm;
        var cancel = new Button
        {
            Content = "取消", IsCancel = true, MinWidth = 78,
            Margin = new Thickness(8, 0, 0, 0)
        };
        actions.Children.Add(ok);
        actions.Children.Add(cancel);
        Grid.SetRow(actions, 5);
        rootGrid.Children.Add(actions);
        Content = rootGrid;

        Loaded += (_, _) => _level.SelectedIndex = 0;
    }

    private void LoadSelectedLevel()
    {
        if (_level.SelectedIndex < 0) return;
        var directories = EnumerateDirectoriesAtLevel(_level.SelectedIndex);
        _options = directories.Select(path => new FolderOption(
            path,
            _level.SelectedIndex == 0 ? ".（整个根目录）" : Path.GetRelativePath(_root, path),
            true)).ToList();
        _folders.ItemsSource = _options;
        _status.Text = directories.Count == 0
            ? "该层级没有文件夹，请选择其他层级。"
            : $"共 {directories.Count} 个目录，当前默认全部勾选。";
    }

    private List<string> EnumerateDirectoriesAtLevel(int level)
    {
        var current = new List<string> { _root };
        for (var depth = 0; depth < level && current.Count > 0; depth++)
        {
            var next = new List<string>();
            foreach (var directory in current)
            {
                try
                {
                    next.AddRange(Directory.EnumerateDirectories(directory));
                }
                catch (Exception error) when (error is UnauthorizedAccessException or IOException)
                {
                    // Inaccessible branches are omitted while other folders remain selectable.
                }
            }
            current = next;
        }
        current.Sort(StringComparer.CurrentCultureIgnoreCase);
        return current;
    }

    private void SetAll(bool selected)
    {
        foreach (var option in _options) option.IsSelected = selected;
        _folders.Items.Refresh();
    }

    private void Confirm(object sender, RoutedEventArgs e)
    {
        if (SelectedFolders.Count == 0)
        {
            MessageBox.Show(this, "请至少勾选一个需要扫描的目录。", "尚未选择扫描范围",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }

    private sealed class FolderOption : INotifyPropertyChanged
    {
        private bool _isSelected;
        public string FullPath { get; }
        public string RelativePath { get; }
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public FolderOption(string fullPath, string relativePath, bool isSelected)
        {
            FullPath = fullPath;
            RelativePath = relativePath;
            _isSelected = isSelected;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
