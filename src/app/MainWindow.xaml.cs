using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using LabelReviewer.Models;
using LabelReviewer.Services;
using LabelReviewer.WpfDialogs;
using Microsoft.Win32;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingRectangle = System.Drawing.Rectangle;

namespace LabelReviewer;

public partial class MainWindow : Window
{
    private readonly CategoryStore _categoryStore = new();
    private readonly ExcelReportService _excelReportService = new();
    private List<ImageItem> _images = [];
    private AnnotationRepository? _repository;
    private string? _inputRoot;
    private string? _outputRoot;
    private int _currentIndex = -1;
    private DrawingBitmap? _currentBitmap;
    private string? _currentBitmapPath;
    private bool _loadingModelDecision;
    private bool _loadingManualDecision;

    public MainWindow()
    {
        InitializeComponent();
        using (var applicationIcon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!))
        {
            if (applicationIcon is not null)
            {
                var iconSource = Imaging.CreateBitmapSourceFromHIcon(applicationIcon.Handle,
                    Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                iconSource.Freeze();
                Icon = iconSource;
            }
        }
        Canvas.BoxCompleted += AddBox;
        Canvas.BoxEdited += FinishBoxEdit;
        Canvas.SelectionChanged += SelectAnnotationInList;
        Canvas.EditLabelRequested += EditSelectedLabel;
        Canvas.DeleteRequested += DeleteSelected;
        UpdateNavigation();
    }

    private void ChooseInputFolder_Click(object sender, RoutedEventArgs e) => ChooseInputFolder();
    private void Previous_Click(object sender, RoutedEventArgs e) => Navigate(-1);
    private void Next_Click(object sender, RoutedEventArgs e) => Navigate(1);
    private void CreateBox_Click(object sender, RoutedEventArgs e) => Canvas.BeginCreate();
    private void EditBox_Click(object sender, RoutedEventArgs e) => BeginEditSelected();
    private void EditLabel_Click(object sender, RoutedEventArgs e) => EditSelectedLabel();
    private void DeleteBox_Click(object sender, RoutedEventArgs e) => DeleteSelected();
    private void Fit_Click(object sender, RoutedEventArgs e) => Canvas.FitToWindow();
    private void Save_Click(object sender, RoutedEventArgs e) => SaveCurrent();
    private void AddCategory_Click(object sender, RoutedEventArgs e) => AddCategory();

    private void ChooseInputFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择包含待复判图片的根文件夹",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        var selectedRoot = Path.GetFullPath(dialog.FolderName);
        var scopeDialog = new ScanScopeDialog(selectedRoot) { Owner = this };
        if (scopeDialog.ShowDialog() != true) return;

        try
        {
            SaveCurrent();
            _inputRoot = selectedRoot;
            InputPath.Text = _inputRoot;
            ScanScopeText.Text = scopeDialog.SelectedLevel == 0
                ? "整个输入根目录"
                : $"第 {scopeDialog.SelectedLevel} 级：已选 {scopeDialog.SelectedFolders.Count} 个目录";
            SetOutputRoot(GetDefaultOutputRoot(_inputRoot));

            _images = ImageCatalog.Scan(
                _inputRoot, scopeDialog.SelectedFolders, _outputRoot);
            _currentIndex = _images.Count > 0 ? 0 : -1;
            RecreateRepository();
            if (_images.Count == 0)
            {
                ClearCurrentImage();
                MessageBox.Show(this, "所选文件夹及其子文件夹中没有支持的图片。",
                    "没有图片", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                LoadCurrentImage();
            }
            UpdateStatistics();
            TrySaveExcelReport();
            UpdateNavigation();
        }
        catch (Exception error)
        {
            ShowError("读取输入文件夹失败", error);
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
            _currentBitmapPath = item.FullPath;
            item.Width = loaded.Bitmap.Width;
            item.Height = loaded.Bitmap.Height;
            item.SourceBitDepth = loaded.BitDepth;
            item.SourceChannels = loaded.Channels;
            Canvas.SetImage(_currentBitmap, item.Annotations,
                item.SourceBitDepth, item.SourceChannels);
            SelectModelDecision(item.ModelDecision);
            SelectManualDecision(item.ManualDecision);
            RefreshAnnotationList();
            ImageName.Text = item.RelativePath;
            ImageName.ToolTip = item.RelativePath;
            ImageStatus.Text = $"{item.Width} × {item.Height}  {item.SourceBitDepth} 位  {item.SourceChannels} 通道";
            PositionStatus.Text =
                $"第 {_currentIndex + 1} / {_images.Count} 张　当前 {item.Annotations.Count} 个框";
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
        if (offset > 0 && !HasCurrentModelDecision())
        {
            MessageBox.Show(this, "请先选择当前图片的模型判定结果（OK 或 NG），再查看下一张。",
                "未填写模型判定结果", MessageBoxButton.OK, MessageBoxImage.Warning);
            ModelDecisionCombo.Focus();
            ModelDecisionCombo.IsDropDownOpen = true;
            return;
        }
        if (offset > 0 && !HasCurrentManualDecision())
        {
            MessageBox.Show(this, "请先选择当前图片的人工判定结果（OK 或 NG），再查看下一张。",
                "未填写人工判定结果", MessageBoxButton.OK, MessageBoxImage.Warning);
            ManualDecisionCombo.Focus();
            ManualDecisionCombo.IsDropDownOpen = true;
            return;
        }
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

    private void AddBox(DrawingRectangle rectangle)
    {
        if (_currentIndex < 0) return;
        var dialog = new LabelDialog(_categoryStore.Categories,
            CategoryCombo.Text, CurrentReviewType) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        _categoryStore.Add(dialog.Category);
        SelectReviewType(dialog.ReviewType);
        RefreshCategoryEditor(dialog.Category);
        var annotation = new Annotation
        {
            ReviewType = dialog.ReviewType,
            Category = dialog.Category,
            Bounds = rectangle
        };
        _images[_currentIndex].Annotations.Add(annotation);
        Canvas.SelectedId = annotation.Id;
        RefreshAnnotationList(annotation.Id);
        SaveCurrent();
        UpdateStatistics();
    }

    private void DeleteSelected()
    {
        if (_currentIndex < 0 || Canvas.SelectedId is null) return;
        var item = _images[_currentIndex];
        var annotation = item.Annotations.FirstOrDefault(value => value.Id == Canvas.SelectedId);
        if (annotation is null) return;
        item.Annotations.Remove(annotation);
        Canvas.SelectedId = null;
        RefreshAnnotationList();
        Canvas.InvalidateVisual();
        SaveCurrent();
        UpdateStatistics();
    }

    private void BeginEditSelected()
    {
        if (Canvas.BeginEditSelected()) return;
        MessageBox.Show(this, "请先在画布或右侧标注列表中选择一个检测框。",
            "尚未选择检测框", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void FinishBoxEdit(Guid id)
    {
        RefreshAnnotationList(id);
        SaveCurrent();
    }

    private void EditSelectedLabel()
    {
        if (_currentIndex < 0 || Canvas.SelectedId is null)
        {
            MessageBox.Show(this, "请先在画布或右侧标注列表中选择一个检测框。",
                "尚未选择检测框", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var annotation = _images[_currentIndex].Annotations
            .FirstOrDefault(value => value.Id == Canvas.SelectedId);
        if (annotation is null) return;

        var dialog = new LabelDialog(_categoryStore.Categories,
            annotation.Category, annotation.ReviewType) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        annotation.ReviewType = dialog.ReviewType;
        annotation.Category = dialog.Category;
        _categoryStore.Add(dialog.Category);
        SelectReviewType(dialog.ReviewType);
        RefreshCategoryEditor(dialog.Category);
        RefreshAnnotationList(annotation.Id);
        Canvas.SelectedId = annotation.Id;
        Canvas.InvalidateVisual();
        SaveCurrent();
        UpdateStatistics();
    }

    private void SaveCurrent()
    {
        if (_currentIndex < 0 || _repository is null) return;
        var item = _images[_currentIndex];
        var displayBitmap = string.Equals(_currentBitmapPath, item.FullPath,
            StringComparison.OrdinalIgnoreCase)
            ? _currentBitmap
            : null;
        _repository.Save(item, displayBitmap);
        if (_outputRoot is not null) _categoryStore.Save(_outputRoot);
        SaveStatus.Text = TrySaveExcelReport()
            ? $"已保存（含 Excel） {DateTime.Now:HH:mm:ss}"
            : "标注已保存；Excel 正被占用，请关闭后再次保存";
    }

    private bool TrySaveExcelReport()
    {
        if (_outputRoot is null) return false;
        try
        {
            _excelReportService.Save(_outputRoot, _images);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void AddCategory()
    {
        var dialog = new CategoryInputDialog { Owner = this };
        if (dialog.ShowDialog() != true) return;
        _categoryStore.Add(dialog.Category);
        RefreshCategoryEditor(dialog.Category);
        if (_outputRoot is not null) _categoryStore.Save(_outputRoot);
        SaveStatus.Text = $"已添加类别：{dialog.Category}";
    }

    private void RefreshCategoryEditor(string? selected = null)
    {
        selected ??= CategoryCombo.Text;
        CategoryCombo.ItemsSource = _categoryStore.Categories.ToList();
        CategoryCombo.Text = selected;
    }

    private void RefreshAnnotationList(Guid? selectedId = null)
    {
        AnnotationList.ItemsSource = null;
        if (_currentIndex >= 0)
        {
            AnnotationList.ItemsSource = _images[_currentIndex].Annotations;
            if (selectedId is Guid id)
                AnnotationList.SelectedItem = _images[_currentIndex].Annotations
                    .FirstOrDefault(annotation => annotation.Id == id);
            PositionStatus.Text =
                $"第 {_currentIndex + 1} / {_images.Count} 张　当前 {_images[_currentIndex].Annotations.Count} 个框";
        }
    }

    private void AnnotationList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AnnotationList.SelectedItem is Annotation annotation)
            Canvas.SelectedId = annotation.Id;
    }

    private void SelectAnnotationInList(Guid? id)
    {
        AnnotationList.SelectedItem = id is null || _currentIndex < 0
            ? null
            : _images[_currentIndex].Annotations.FirstOrDefault(value => value.Id == id);
        if (AnnotationList.SelectedItem is not null)
            AnnotationList.ScrollIntoView(AnnotationList.SelectedItem);
    }

    private void UpdateStatistics()
    {
        var imageStates = _images.Select(image => new
        {
            HasAnnotations = image.Annotations.Count > 0,
            HasMissed = image.Annotations.Any(annotation => annotation.ReviewType == "漏检"),
            HasFalse = image.Annotations.Any(annotation => annotation.ReviewType == "误检")
        }).ToList();
        ProblemImageCount.Text = imageStates.Count(state => state.HasAnnotations).ToString();
        CleanImageCount.Text = imageStates.Count(state => !state.HasAnnotations).ToString();
        OnlyMissedImageCount.Text = imageStates.Count(
            state => state.HasMissed && !state.HasFalse).ToString();
        OnlyFalseImageCount.Text = imageStates.Count(
            state => state.HasFalse && !state.HasMissed).ToString();
        BothProblemImageCount.Text = imageStates.Count(
            state => state.HasMissed && state.HasFalse).ToString();

        StatisticsList.ItemsSource = _images.SelectMany(image => image.Annotations)
            .GroupBy(annotation => new { annotation.ReviewType, annotation.Category })
            .Select(group => new StatisticsRow(
                group.Key.ReviewType.Length == 0 ? "未指定" : group.Key.ReviewType,
                group.Key.Category, group.Count()))
            .OrderBy(row => row.ReviewType, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.Category, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void SetOutputRoot(string path)
    {
        var fullPath = Path.GetFullPath(path);
        EnsureDistinctRoots(_inputRoot, fullPath);
        _outputRoot = fullPath;
        Directory.CreateDirectory(_outputRoot);
        OutputPath.Text = _outputRoot;
        _categoryStore.Load(_outputRoot);
        RefreshCategoryEditor();
        SaveStatus.Text = "自动保存已开启";
    }

    private static void EnsureDistinctRoots(string? inputRoot, string? outputRoot)
    {
        if (inputRoot is null || outputRoot is null) return;
        if (string.Equals(Path.TrimEndingDirectorySeparator(inputRoot),
                Path.TrimEndingDirectorySeparator(outputRoot),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "输出文件夹不能与输入文件夹相同，否则会覆盖原图。");
    }

    private static string GetDefaultOutputRoot(string inputRoot)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(inputRoot));
        var directoryName = Path.GetFileName(trimmed);
        var parent = Directory.GetParent(trimmed)?.FullName;
        return directoryName.Length == 0 || parent is null
            ? Path.Combine(trimmed, "review")
            : Path.Combine(parent, directoryName + "_review");
    }

    private void UpdateNavigation()
    {
        PreviousButton.IsEnabled = _currentIndex > 0;
        NextButton.IsEnabled = _currentIndex >= 0 && _currentIndex < _images.Count - 1;
        if (_images.Count == 0) PositionStatus.Text = "共 0 张";
    }

    private void ClearCurrentImage()
    {
        _currentBitmap?.Dispose();
        _currentBitmap = null;
        _currentBitmapPath = null;
        Canvas.SetImage(null, Array.Empty<Annotation>());
        AnnotationList.ItemsSource = null;
        ImageName.Text = "尚未加载图片";
        ImageStatus.Text = "未加载图像";
        SelectModelDecision("无");
        SelectManualDecision("无");
    }

    private void ModelDecisionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingModelDecision || _currentIndex < 0 || _currentIndex >= _images.Count) return;
        var selected = (ModelDecisionCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
        _images[_currentIndex].ModelDecision = selected is "OK" or "NG" ? selected : "无";
        try
        {
            SaveCurrent();
        }
        catch (Exception error)
        {
            ShowError("保存模型判定结果失败", error);
        }
    }

    private void SelectModelDecision(string? value)
    {
        _loadingModelDecision = true;
        try
        {
            ModelDecisionCombo.SelectedIndex = value switch
            {
                "OK" => 1,
                "NG" => 2,
                _ => 0
            };
        }
        finally
        {
            _loadingModelDecision = false;
        }
    }

    private bool HasCurrentModelDecision() =>
        _currentIndex >= 0 && _currentIndex < _images.Count &&
        _images[_currentIndex].ModelDecision is "OK" or "NG";

    private void ManualDecisionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingManualDecision || _currentIndex < 0 || _currentIndex >= _images.Count) return;
        var selected = (ManualDecisionCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
        _images[_currentIndex].ManualDecision = selected is "OK" or "NG" ? selected : "无";
        try
        {
            SaveCurrent();
        }
        catch (Exception error)
        {
            ShowError("保存人工判定结果失败", error);
        }
    }

    private void SelectManualDecision(string? value)
    {
        _loadingManualDecision = true;
        try
        {
            ManualDecisionCombo.SelectedIndex = value switch
            {
                "OK" => 1,
                "NG" => 2,
                _ => 0
            };
        }
        finally
        {
            _loadingManualDecision = false;
        }
    }

    private bool HasCurrentManualDecision() =>
        _currentIndex >= 0 && _currentIndex < _images.Count &&
        _images[_currentIndex].ManualDecision is "OK" or "NG";

    private string CurrentReviewType =>
        (ReviewTypeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "误检";

    private void SelectReviewType(string value)
    {
        ReviewTypeCombo.SelectedIndex = value == "漏检" ? 1 : 0;
    }

    private bool IsLabelEditorFocused =>
        CategoryCombo.IsKeyboardFocusWithin || ReviewTypeCombo.IsKeyboardFocusWithin ||
        ModelDecisionCombo.IsKeyboardFocusWithin || ManualDecisionCombo.IsKeyboardFocusWithin;

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key == Key.S)
        {
            SaveCurrent();
            e.Handled = true;
        }
        else if (e.Key == Key.W && !IsLabelEditorFocused)
        {
            Canvas.BeginCreate();
            e.Handled = true;
        }
        else if (e.Key == Key.E && !IsLabelEditorFocused)
        {
            BeginEditSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.R && !IsLabelEditorFocused)
        {
            EditSelectedLabel();
            e.Handled = true;
        }
        else if ((e.Key == Key.A || e.Key == Key.Left) && !IsLabelEditorFocused)
        {
            Navigate(-1);
            e.Handled = true;
        }
        else if ((e.Key == Key.D || e.Key == Key.Right) && !IsLabelEditorFocused)
        {
            Navigate(1);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            DeleteSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Canvas.CancelCreate();
            e.Handled = true;
        }
        else if (e.Key == Key.F && !IsLabelEditorFocused)
        {
            Canvas.FitToWindow();
            e.Handled = true;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        try
        {
            SaveCurrent();
        }
        catch (Exception error)
        {
            if (MessageBox.Show(this, $"保存失败：{error.Message}\n仍要退出吗？", "保存失败",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
                e.Cancel = true;
        }
    }

    private void ShowError(string title, Exception error) =>
        MessageBox.Show(this, $"{title}\n\n{error.Message}", title,
            MessageBoxButton.OK, MessageBoxImage.Error);

    private sealed record StatisticsRow(string ReviewType, string Category, int Count);
}
