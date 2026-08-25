using LabelReviewer.Controls;
using LabelReviewer.Models;
using LabelReviewer.Services;

namespace LabelReviewer.Forms;

public sealed class MainForm : Form
{
    private readonly ImageCanvas _canvas = new();
    private readonly TextBox _inputPath = ReadOnlyTextBox();
    private readonly TextBox _outputPath = ReadOnlyTextBox();
    private readonly ComboBox _reviewType = new();
    private readonly ComboBox _category = new();
    private readonly ListBox _annotationList = new();
    private readonly ListView _statistics = new();
    private readonly Label _imageName = new();
    private readonly ToolStripStatusLabel _positionStatus = new();
    private readonly ToolStripStatusLabel _imageStatus = new();
    private readonly ToolStripStatusLabel _saveStatus = new();
    private readonly ToolStripButton _previousButton;
    private readonly ToolStripButton _nextButton;

    private readonly CategoryStore _categoryStore = new();
    private List<ImageItem> _images = [];
    private AnnotationRepository? _repository;
    private string? _inputRoot;
    private string? _outputRoot;
    private bool _outputSelectedManually;
    private int _currentIndex = -1;
    private Bitmap? _currentBitmap;

    public MainForm()
    {
        Text = "图像复判与矩形标注工具";
        WindowState = FormWindowState.Maximized;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1100, 700);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 9f);
        KeyPreview = true;

        var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(5) };
        toolbar.Items.Add(MakeButton("选择输入文件夹", (_, _) => ChooseInputFolder()));
        toolbar.Items.Add(MakeButton("选择输出文件夹", (_, _) => ChooseOutputFolder()));
        toolbar.Items.Add(new ToolStripSeparator());
        _previousButton = MakeButton("◀ 上一张 (A)", (_, _) => Navigate(-1));
        _nextButton = MakeButton("下一张 (D) ▶", (_, _) => Navigate(1));
        toolbar.Items.Add(_previousButton);
        toolbar.Items.Add(_nextButton);
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(MakeButton("创建矩形框 (W)", (_, _) => _canvas.BeginCreate(false)));
        toolbar.Items.Add(MakeButton("编辑选中框 (E)", (_, _) => BeginEditSelected()));
        toolbar.Items.Add(MakeButton("删除选中框 (Delete)", (_, _) => DeleteSelected()));
        toolbar.Items.Add(MakeButton("适应窗口 (F)", (_, _) => _canvas.FitToWindow()));
        toolbar.Items.Add(MakeButton("保存 (Ctrl+S)", (_, _) => SaveCurrent()));

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel2,
            SplitterWidth = 5
        };
        split.Panel1.Controls.Add(_canvas);
        split.Panel2.Controls.Add(BuildSidePanel());
        split.Resize += (_, _) =>
        {
            var maximum = split.Width - split.Panel2MinSize - split.SplitterWidth;
            if (maximum > split.Panel1MinSize)
                split.SplitterDistance = Math.Clamp(split.Width - 400,
                    split.Panel1MinSize, maximum);
        };

        var status = new StatusStrip();
        _positionStatus.Spring = true;
        _positionStatus.TextAlign = ContentAlignment.MiddleLeft;
        _positionStatus.Text = "共 0 张";
        _imageStatus.Text = "未加载图像";
        _saveStatus.Text = "请选择输出文件夹";
        status.Items.AddRange([_positionStatus, _imageStatus, _saveStatus]);

        Controls.Add(split);
        Controls.Add(toolbar);
        Controls.Add(status);
        toolbar.Dock = DockStyle.Top;
        status.Dock = DockStyle.Bottom;

        _canvas.BoxCompleted += AddBox;
        _canvas.BoxEdited += FinishBoxEdit;
        _canvas.SelectionChanged += SelectAnnotationInList;
        _canvas.DeleteRequested += DeleteSelected;
        _annotationList.SelectedIndexChanged += (_, _) =>
        {
            if (_annotationList.SelectedItem is Annotation annotation)
                _canvas.SelectedId = annotation.Id;
        };
        KeyDown += HandleShortcut;
        Shown += (_, _) =>
        {
            WindowState = FormWindowState.Maximized;
            split.Panel1MinSize = 500;
            split.Panel2MinSize = 380;
            var maximum = split.Width - split.Panel2MinSize - split.SplitterWidth;
            if (maximum >= split.Panel1MinSize)
                split.SplitterDistance = Math.Clamp(
                    split.Width - 400, split.Panel1MinSize, maximum);
        };
        FormClosing += (_, e) =>
        {
            try { SaveCurrent(); }
            catch (Exception error)
            {
                if (MessageBox.Show(this, $"保存失败：{error.Message}\n仍要退出吗？", "保存失败",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No)
                    e.Cancel = true;
            }
        };
        UpdateNavigation();
    }

    private Control BuildSidePanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 13,
            Padding = new Padding(10),
            AutoScroll = true,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        panel.Controls.Add(SectionLabel("输入文件夹"));
        panel.Controls.Add(_inputPath);
        panel.Controls.Add(SectionLabel("输出文件夹"));
        panel.Controls.Add(_outputPath);
        _imageName.AutoEllipsis = true;
        _imageName.AutoSize = false;
        _imageName.Dock = DockStyle.Fill;
        _imageName.MinimumSize = new Size(0, 40);
        _imageName.Margin = new Padding(3, 6, 3, 4);
        _imageName.TextAlign = ContentAlignment.MiddleLeft;
        _imageName.Text = "尚未加载图片";
        panel.Controls.Add(_imageName);

        var categoryLine = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            MinimumSize = new Size(0, 36),
            ColumnCount = 3,
            RowCount = 1,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize
        };
        categoryLine.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        categoryLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        categoryLine.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _reviewType.DropDownStyle = ComboBoxStyle.DropDownList;
        _reviewType.Dock = DockStyle.Fill;
        _reviewType.Items.AddRange(["误检", "漏检"]);
        _reviewType.SelectedIndex = 0;
        _category.DropDownStyle = ComboBoxStyle.DropDown;
        _category.Dock = DockStyle.Fill;
        var addCategory = new Button { Text = "添加类别", AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
        addCategory.Click += (_, _) => AddCategoryFromEditor();
        categoryLine.Controls.Add(_reviewType, 0, 0);
        categoryLine.Controls.Add(_category, 1, 0);
        categoryLine.Controls.Add(addCategory, 2, 0);
        panel.Controls.Add(SectionLabel("问题类型 / 当前类别"));
        panel.Controls.Add(categoryLine);

        panel.Controls.Add(SectionLabel("当前图片标注"));
        _annotationList.Dock = DockStyle.Fill;
        _annotationList.IntegralHeight = false;
        panel.Controls.Add(_annotationList);
        panel.Controls.Add(SectionLabel("全部图片问题类型 / 类别统计"));

        _statistics.Dock = DockStyle.Fill;
        _statistics.View = View.Details;
        _statistics.FullRowSelect = true;
        _statistics.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _statistics.Columns.Add("问题类型", 90);
        _statistics.Columns.Add("类别", 175);
        _statistics.Columns.Add("数量", 70, HorizontalAlignment.Right);
        _statistics.Resize += (_, _) => ResizeStatisticsColumns();
        panel.Controls.Add(_statistics);

        var actionLine = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var create = new Button { Text = "创建矩形框 (W)", AutoSize = true };
        var edit = new Button { Text = "编辑选中框 (E)", AutoSize = true };
        var delete = new Button { Text = "删除选中框", AutoSize = true };
        create.Click += (_, _) => _canvas.BeginCreate(false);
        edit.Click += (_, _) => BeginEditSelected();
        delete.Click += (_, _) => DeleteSelected();
        actionLine.Controls.Add(create);
        actionLine.Controls.Add(edit);
        actionLine.Controls.Add(delete);
        panel.Controls.Add(actionLine);

        var help = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ForeColor = Color.DimGray,
            Text = "W：进入画框模式　E：编辑选中框\n两次左键单击：确定两个角\n滚轮：缩放　中键：平移\nA/D：上一张/下一张\nEsc：取消画框　Delete：删除"
        };
        panel.Controls.Add(help);
        return panel;
    }

    private void ChooseInputFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择包含待复判图片的根文件夹",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            SaveCurrent();
            _inputRoot = Path.GetFullPath(dialog.SelectedPath);
            _inputPath.Text = _inputRoot;
            if (!_outputSelectedManually)
                SetOutputRoot(GetDefaultOutputRoot(_inputRoot));
            _images = ImageCatalog.Scan(_inputRoot, _outputRoot);
            _currentIndex = _images.Count > 0 ? 0 : -1;
            RecreateRepository();
            if (_images.Count == 0)
            {
                ClearCurrentImage();
                MessageBox.Show(this, "所选文件夹及其子文件夹中没有支持的图片。",
                    "没有图片", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                LoadCurrentImage();
            }
            UpdateStatistics();
            UpdateNavigation();
        }
        catch (Exception error)
        {
            ShowError("读取输入文件夹失败", error);
        }
    }

    private void ChooseOutputFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择结果图片和 JSON 的输出根文件夹",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            SaveCurrent();
            _outputSelectedManually = true;
            SetOutputRoot(Path.GetFullPath(dialog.SelectedPath));
            foreach (var item in _images) item.AnnotationDataLoaded = false;
            RecreateRepository();
            if (_repository is not null)
            {
                foreach (var item in _images) _repository.Load(item);
            }
            foreach (var category in _images.SelectMany(item => item.Annotations)
                         .Select(annotation => annotation.Category))
                _categoryStore.Add(category);
            RefreshCategoryEditor();
            if (_currentIndex >= 0) LoadCurrentImage();
            UpdateStatistics();
            _saveStatus.Text = "自动保存已开启";
        }
        catch (Exception error)
        {
            ShowError("设置输出文件夹失败", error);
        }
    }

    private void RecreateRepository()
    {
        _repository = _inputRoot is not null && _outputRoot is not null
            ? new AnnotationRepository(_outputRoot)
            : null;
        if (_repository is not null)
            foreach (var item in _images) _repository.Load(item);
    }

    private void LoadCurrentImage()
    {
        if (_currentIndex < 0 || _currentIndex >= _images.Count) return;
        var item = _images[_currentIndex];
        try
        {
            _repository?.Load(item);
            var loaded = NativeImageLoader.Load(item.FullPath);
            _currentBitmap?.Dispose();
            _currentBitmap = loaded.Bitmap;
            item.Width = loaded.Bitmap.Width;
            item.Height = loaded.Bitmap.Height;
            item.SourceBitDepth = loaded.BitDepth;
            item.SourceChannels = loaded.Channels;
            _canvas.SetImage(_currentBitmap, item.Annotations);
            RefreshAnnotationList();
            _imageName.Text = item.RelativePath;
            _imageStatus.Text = $"{item.Width} × {item.Height}  {item.SourceBitDepth} 位  {item.SourceChannels} 通道";
            _positionStatus.Text = $"第 {_currentIndex + 1} / {_images.Count} 张　当前 {item.Annotations.Count} 个框";
        }
        catch (Exception error)
        {
            ShowError($"无法加载图片：{item.RelativePath}", error);
        }
        UpdateNavigation();
    }

    private void Navigate(int offset)
    {
        var next = _currentIndex + offset;
        if (next < 0 || next >= _images.Count) return;
        try
        {
            SaveCurrent();
            _currentIndex = next;
            LoadCurrentImage();
        }
        catch (Exception error)
        {
            ShowError("切换图片失败", error);
        }
    }

    private void AddBox(Rectangle rectangle)
    {
        if (_currentIndex < 0) return;
        using var dialog = new CategoryDialog(
            _categoryStore.Categories, _category.Text, _reviewType.Text);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _categoryStore.Add(dialog.Category);
        _reviewType.Text = dialog.ReviewType;
        _category.Text = dialog.Category;
        RefreshCategoryEditor(dialog.Category);
        var annotation = new Annotation
        {
            ReviewType = dialog.ReviewType,
            Category = dialog.Category,
            Bounds = rectangle
        };
        _images[_currentIndex].Annotations.Add(annotation);
        _canvas.SelectedId = annotation.Id;
        RefreshAnnotationList(annotation.Id);
        SaveCurrent();
        UpdateStatistics();
    }

    private void DeleteSelected()
    {
        if (_currentIndex < 0 || _canvas.SelectedId is null) return;
        var item = _images[_currentIndex];
        var annotation = item.Annotations.FirstOrDefault(value => value.Id == _canvas.SelectedId);
        if (annotation is null) return;
        item.Annotations.Remove(annotation);
        _canvas.SelectedId = null;
        RefreshAnnotationList();
        _canvas.Invalidate();
        SaveCurrent();
        UpdateStatistics();
    }

    private void BeginEditSelected()
    {
        if (_canvas.BeginEditSelected()) return;
        MessageBox.Show(this, "请先在画布或右侧标注列表中选择一个检测框。",
            "尚未选择检测框", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void FinishBoxEdit(Guid id)
    {
        RefreshAnnotationList(id);
        SaveCurrent();
    }

    private void SaveCurrent()
    {
        if (_currentIndex < 0 || _repository is null) return;
        _repository.Save(_images[_currentIndex]);
        if (_outputRoot is not null) _categoryStore.Save(_outputRoot);
        _saveStatus.Text = $"已保存 {DateTime.Now:HH:mm:ss}";
    }

    private void AddCategoryFromEditor()
    {
        using var dialog = new CategoryInputDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var selected = dialog.Category;
        _categoryStore.Add(selected);
        RefreshCategoryEditor(selected);
        if (_outputRoot is not null) _categoryStore.Save(_outputRoot);
        _saveStatus.Text = $"已添加类别：{selected}";
    }

    private void RefreshCategoryEditor(string? selected = null)
    {
        selected ??= _category.Text;
        _category.BeginUpdate();
        _category.Items.Clear();
        _category.Items.AddRange(_categoryStore.Categories.Cast<object>().ToArray());
        _category.EndUpdate();
        _category.Text = selected;
    }

    private void RefreshAnnotationList(Guid? selectedId = null)
    {
        _annotationList.BeginUpdate();
        _annotationList.Items.Clear();
        if (_currentIndex >= 0)
        {
            foreach (var annotation in _images[_currentIndex].Annotations)
            {
                var index = _annotationList.Items.Add(annotation);
                if (annotation.Id == selectedId) _annotationList.SelectedIndex = index;
            }
        }
        _annotationList.EndUpdate();
        if (_currentIndex >= 0)
            _positionStatus.Text = $"第 {_currentIndex + 1} / {_images.Count} 张　当前 {_images[_currentIndex].Annotations.Count} 个框";
    }

    private void SelectAnnotationInList(Guid? id)
    {
        if (id is null)
        {
            _annotationList.ClearSelected();
            return;
        }
        for (var index = 0; index < _annotationList.Items.Count; index++)
        {
            if (_annotationList.Items[index] is Annotation annotation && annotation.Id == id)
            {
                _annotationList.SelectedIndex = index;
                break;
            }
        }
    }

    private void UpdateStatistics()
    {
        var counts = _images.SelectMany(image => image.Annotations)
            .GroupBy(annotation => new { annotation.ReviewType, annotation.Category })
            .Select(group => new
            {
                ReviewType = group.Key.ReviewType.Length == 0 ? "未指定" : group.Key.ReviewType,
                group.Key.Category,
                Count = group.Count()
            })
            .OrderBy(row => row.ReviewType, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.Category, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        _statistics.BeginUpdate();
        _statistics.Items.Clear();
        foreach (var row in counts)
            _statistics.Items.Add(new ListViewItem(
                [row.ReviewType, row.Category, row.Count.ToString()]));
        _statistics.EndUpdate();
        ResizeStatisticsColumns();
    }

    private void SetOutputRoot(string path)
    {
        _outputRoot = Path.GetFullPath(path);
        Directory.CreateDirectory(_outputRoot);
        _outputPath.Text = _outputRoot;
        _categoryStore.Load(_outputRoot);
        RefreshCategoryEditor();
        _saveStatus.Text = "自动保存已开启";
    }

    private static string GetDefaultOutputRoot(string inputRoot)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(inputRoot));
        var directoryName = Path.GetFileName(trimmed);
        var parent = Directory.GetParent(trimmed)?.FullName;
        if (directoryName.Length == 0 || parent is null)
            return Path.Combine(trimmed, "review");
        return Path.Combine(parent, directoryName + "_review");
    }

    private void ResizeStatisticsColumns()
    {
        if (_statistics.Columns.Count != 3) return;
        var available = Math.Max(150, _statistics.ClientSize.Width - 95 - 65);
        _statistics.Columns[0].Width = 95;
        _statistics.Columns[1].Width = available;
        _statistics.Columns[2].Width = 65;
    }

    private void UpdateNavigation()
    {
        _previousButton.Enabled = _currentIndex > 0;
        _nextButton.Enabled = _currentIndex >= 0 && _currentIndex < _images.Count - 1;
        if (_images.Count == 0) _positionStatus.Text = "共 0 张";
    }

    private void ClearCurrentImage()
    {
        _currentBitmap?.Dispose();
        _currentBitmap = null;
        _canvas.SetImage(null, Array.Empty<Annotation>());
        _annotationList.Items.Clear();
        _imageName.Text = "尚未加载图片";
        _imageStatus.Text = "未加载图像";
    }

    private void HandleShortcut(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.S)
        {
            SaveCurrent();
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.W && !IsLabelEditorFocused)
        {
            _canvas.BeginCreate(false);
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.E && !IsLabelEditorFocused)
        {
            BeginEditSelected();
            e.SuppressKeyPress = true;
        }
        else if ((e.KeyCode == Keys.A || e.KeyCode == Keys.Left) && !IsLabelEditorFocused)
        {
            Navigate(-1);
            e.SuppressKeyPress = true;
        }
        else if ((e.KeyCode == Keys.D || e.KeyCode == Keys.Right) && !IsLabelEditorFocused)
        {
            Navigate(1);
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Delete)
        {
            DeleteSelected();
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            _canvas.CancelCreate();
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.F && !IsLabelEditorFocused)
        {
            _canvas.FitToWindow();
            e.SuppressKeyPress = true;
        }
    }

    private bool IsLabelEditorFocused => _category.Focused || _reviewType.Focused;

    private static ToolStripButton MakeButton(string text, EventHandler handler)
    {
        var button = new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.Text };
        button.Click += handler;
        return button;
    }

    private static Label SectionLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
        Padding = new Padding(0, 7, 0, 3)
    };

    private static TextBox ReadOnlyTextBox() => new()
    {
        ReadOnly = true,
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.FixedSingle
    };

    private void ShowError(string title, Exception error) =>
        MessageBox.Show(this, $"{title}\n\n{error.Message}", title,
            MessageBoxButtons.OK, MessageBoxIcon.Error);
}
