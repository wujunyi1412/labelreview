using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LabelReviewer.Models;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingRectangle = System.Drawing.Rectangle;

namespace LabelReviewer.WpfControls;

public sealed class ImageCanvas : FrameworkElement
{
    private const double MaximumPixelScale = 32d;
    private const double PixelGridScale = 8d;
    private const double PixelValueScale = 22d;
    private enum CreationState { Idle, Armed, Drawing }

    private BitmapSource? _image;
    private int _imageWidth;
    private int _imageHeight;
    private int _sourceBitDepth;
    private int _sourceChannels;
    private byte[]? _pixelBytes;
    private int _pixelStride;
    private IList<Annotation> _annotations = Array.Empty<Annotation>();
    private CreationState _creationState;
    private Point _startImagePoint;
    private Point _currentImagePoint;
    private double _zoomMultiplier = 1d;
    private Vector _pan;
    private bool _panning;
    private bool _leftPanCandidate;
    private MouseButton? _panButton;
    private Point _leftDownPoint;
    private Point _lastPanPoint;
    private Guid? _selectedId;
    private Guid? _editingId;

    public event Action<DrawingRectangle>? BoxCompleted;
    public event Action<Guid>? BoxEdited;
    public event Action<Guid?>? SelectionChanged;
    public event Action? EditLabelRequested;
    public event Action? DeleteRequested;

    public Guid? SelectedId
    {
        get => _selectedId;
        set
        {
            if (_selectedId == value) return;
            _selectedId = value;
            InvalidateVisual();
        }
    }

    public ImageCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        Cursor = Cursors.Arrow;

        var create = new MenuItem { Header = "创建矩形框 (W)" };
        create.Click += (_, _) => BeginCreate();
        var edit = new MenuItem { Header = "编辑选中框 (E)" };
        edit.Click += (_, _) => BeginEditSelected();
        var editLabel = new MenuItem { Header = "编辑选中框标签 (R)" };
        editLabel.Click += (_, _) => EditLabelRequested?.Invoke();
        var delete = new MenuItem { Header = "删除选中框 (Delete)" };
        delete.Click += (_, _) => DeleteRequested?.Invoke();
        ContextMenu = new ContextMenu { Items = { create, edit, editLabel, delete } };
    }

    public void SetImage(DrawingBitmap? bitmap, IList<Annotation> annotations,
        int sourceBitDepth = 0, int sourceChannels = 0)
    {
        _annotations = annotations;
        _creationState = CreationState.Idle;
        _editingId = null;
        _sourceBitDepth = sourceBitDepth;
        _sourceChannels = sourceChannels;
        SelectedId = null;
        if (bitmap is null)
        {
            _image = null;
            _imageWidth = 0;
            _imageHeight = 0;
            _pixelBytes = null;
            _pixelStride = 0;
        }
        else
        {
            _image = ConvertBitmap(bitmap);
            _imageWidth = bitmap.Width;
            _imageHeight = bitmap.Height;
            _pixelStride = bitmap.Width * 4;
            _pixelBytes = null;
        }
        FitToWindow();
    }

    public void FitToWindow()
    {
        _zoomMultiplier = 1d;
        _pan = default;
        InvalidateVisual();
    }

    public void BeginCreate()
    {
        if (_image is null) return;
        _editingId = null;
        _creationState = CreationState.Armed;
        Focus();
        Cursor = Cursors.Cross;
        InvalidateVisual();
    }

    public bool BeginEditSelected()
    {
        if (_image is null || SelectedId is null ||
            !_annotations.Any(annotation => annotation.Id == SelectedId))
            return false;
        _editingId = SelectedId;
        _creationState = CreationState.Armed;
        Focus();
        Cursor = Cursors.Cross;
        InvalidateVisual();
        return true;
    }

    public void CancelCreate()
    {
        _creationState = CreationState.Idle;
        _editingId = null;
        Cursor = Cursors.Arrow;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(28, 30, 34)), null,
            new Rect(0, 0, ActualWidth, ActualHeight));
        if (_image is null)
        {
            DrawCenteredText(context, "请选择输入文件夹", 22, Brushes.LightGray);
            return;
        }

        var transform = GetTransform();
        context.DrawImage(_image, new Rect(transform.OffsetX, transform.OffsetY,
            _imageWidth * transform.Scale, _imageHeight * transform.Scale));
        DrawPixelOverlay(context, transform);
        foreach (var annotation in _annotations)
            DrawAnnotation(context, annotation, transform);

        if (_creationState == CreationState.Drawing)
        {
            var preview = Normalize(_startImagePoint, _currentImagePoint);
            var screen = ToScreen(preview, transform);
            var pen = new Pen(Brushes.Lime, 2) { DashStyle = DashStyles.Dash };
            context.DrawRectangle(null, pen, screen);
        }

        if (_creationState == CreationState.Armed)
            DrawHint(context, _editingId is null
                ? "单击确定矩形第一个角，再单击完成"
                : "编辑选中框：单击确定新的第一个角，再单击完成");
        else if (_creationState == CreationState.Drawing)
            DrawHint(context, _editingId is null
                ? "移动鼠标并单击完成；右键或 Esc 取消"
                : "编辑选中框：单击确定新的第二个角；右键或 Esc 取消");
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        var point = e.GetPosition(this);

        if (e.ChangedButton == MouseButton.Middle)
        {
            _panning = true;
            _panButton = MouseButton.Middle;
            _lastPanPoint = point;
            CaptureMouse();
            Cursor = Cursors.SizeAll;
            e.Handled = true;
            return;
        }
        if (e.ChangedButton == MouseButton.Right)
        {
            if (_creationState != CreationState.Idle) CancelCreate();
            return;
        }
        if (e.ChangedButton != MouseButton.Left || _image is null) return;

        if (_creationState == CreationState.Armed)
        {
            if (TryScreenToImage(point, out var start))
            {
                _startImagePoint = start;
                _currentImagePoint = start;
                _creationState = CreationState.Drawing;
                InvalidateVisual();
            }
            e.Handled = true;
            return;
        }
        if (_creationState == CreationState.Drawing)
        {
            if (TryScreenToImage(point, out var end)) CompleteBox(end);
            e.Handled = true;
            return;
        }

        _leftPanCandidate = true;
        _panning = false;
        _panButton = MouseButton.Left;
        _leftDownPoint = point;
        _lastPanPoint = point;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point = e.GetPosition(this);
        if (_leftPanCandidate && !_panning)
        {
            var movement = point - _leftDownPoint;
            if (Math.Abs(movement.X) >= 4 || Math.Abs(movement.Y) >= 4)
            {
                _panning = true;
                Cursor = Cursors.SizeAll;
            }
        }
        if (_panning)
        {
            _pan += point - _lastPanPoint;
            _lastPanPoint = point;
            InvalidateVisual();
            return;
        }
        if (_creationState == CreationState.Drawing &&
            TryScreenToImage(point, out var imagePoint))
        {
            _currentImagePoint = imagePoint;
            InvalidateVisual();
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_panButton != e.ChangedButton) return;
        var wasPanning = _panning;
        var wasLeftCandidate = _leftPanCandidate;
        _panning = false;
        _leftPanCandidate = false;
        _panButton = null;
        ReleaseMouseCapture();
        Cursor = _creationState == CreationState.Idle ? Cursors.Arrow : Cursors.Cross;
        if (e.ChangedButton == MouseButton.Left && wasLeftCandidate && !wasPanning)
        {
            var selected = HitTestAnnotation(e.GetPosition(this));
            SelectedId = selected?.Id;
            SelectionChanged?.Invoke(SelectedId);
        }
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_image is null) return;
        var mouse = e.GetPosition(this);
        var before = ScreenToImageUnclamped(mouse);
        var fitScale = GetFitScale();
        var maximumMultiplier = Math.Max(1d, MaximumPixelScale / fitScale);
        _zoomMultiplier = Math.Clamp(
            _zoomMultiplier * (e.Delta > 0 ? 1.2d : 1d / 1.2d),
            0.1d, maximumMultiplier);
        var transform = GetTransform();
        _pan.X += mouse.X - (transform.OffsetX + before.X * transform.Scale);
        _pan.Y += mouse.Y - (transform.OffsetY + before.Y * transform.Scale);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
    }

    private void CompleteBox(Point end)
    {
        var bounds = Normalize(_startImagePoint, end);
        var editingId = _editingId;
        _creationState = CreationState.Idle;
        _editingId = null;
        Cursor = Cursors.Arrow;
        var rectangle = DrawingRectangle.FromLTRB(
            (int)Math.Floor(bounds.Left), (int)Math.Floor(bounds.Top),
            (int)Math.Ceiling(bounds.Right), (int)Math.Ceiling(bounds.Bottom));
        rectangle.Intersect(new DrawingRectangle(0, 0, _imageWidth, _imageHeight));
        if (rectangle.Width < 2 || rectangle.Height < 2)
        {
            InvalidateVisual();
            return;
        }

        if (editingId is Guid id)
        {
            var annotation = _annotations.FirstOrDefault(value => value.Id == id);
            if (annotation is not null)
            {
                annotation.Bounds = rectangle;
                BoxEdited?.Invoke(id);
            }
        }
        else
        {
            BoxCompleted?.Invoke(rectangle);
        }
        InvalidateVisual();
    }

    private Annotation? HitTestAnnotation(Point screenPoint)
    {
        if (!TryScreenToImage(screenPoint, out var imagePoint)) return null;
        var tolerance = Math.Max(3d, 6d / GetTransform().Scale);
        return _annotations.LastOrDefault(annotation =>
        {
            var expanded = new Rect(annotation.Bounds.X - tolerance,
                annotation.Bounds.Y - tolerance,
                annotation.Bounds.Width + tolerance * 2,
                annotation.Bounds.Height + tolerance * 2);
            return expanded.Contains(imagePoint);
        });
    }

    private void DrawAnnotation(DrawingContext context, Annotation annotation, Transform transform)
    {
        var rectangle = ToScreen(new Rect(annotation.Bounds.X, annotation.Bounds.Y,
            annotation.Bounds.Width, annotation.Bounds.Height), transform);
        var selected = annotation.Id == SelectedId;
        var border = annotation.ReviewType switch
        {
            "误检" => Brushes.LimeGreen,
            "漏检" => Brushes.Gold,
            _ => Brushes.Red
        };
        var background = annotation.ReviewType switch
        {
            "误检" => Color.FromArgb(220, 0, 100, 0),
            "漏检" => Color.FromArgb(220, 184, 134, 11),
            _ => Color.FromArgb(220, 139, 0, 0)
        };
        context.DrawRectangle(null, new Pen(border, selected ? 3 : 2), rectangle);
        if (selected)
        {
            const double size = 7;
            foreach (var point in new[] { rectangle.TopLeft, rectangle.TopRight,
                         rectangle.BottomLeft, rectangle.BottomRight })
                context.DrawRectangle(Brushes.White, null,
                    new Rect(point.X - size / 2, point.Y - size / 2, size, size));
        }

        var reviewType = annotation.ReviewType.Length == 0 ? "未指定" : annotation.ReviewType;
        var text = CreateText($"{reviewType} / {annotation.Category}", 13, Brushes.White);
        var labelY = Math.Max(0, rectangle.Top - text.Height - 4);
        var label = new Rect(rectangle.Left, labelY, text.Width + 8, text.Height + 4);
        context.DrawRectangle(new SolidColorBrush(background), null, label);
        context.DrawText(text, new Point(label.X + 4, label.Y + 2));
    }

    private void DrawPixelOverlay(DrawingContext context, Transform transform)
    {
        if (_image is null || transform.Scale < PixelGridScale) return;

        var firstX = Math.Clamp((int)Math.Floor(-transform.OffsetX / transform.Scale),
            0, Math.Max(0, _imageWidth - 1));
        var firstY = Math.Clamp((int)Math.Floor(-transform.OffsetY / transform.Scale),
            0, Math.Max(0, _imageHeight - 1));
        var lastX = Math.Clamp((int)Math.Ceiling(
            (ActualWidth - transform.OffsetX) / transform.Scale), 0, _imageWidth);
        var lastY = Math.Clamp((int)Math.Ceiling(
            (ActualHeight - transform.OffsetY) / transform.Scale), 0, _imageHeight);

        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), 0.6);
        for (var x = firstX; x <= lastX; x++)
        {
            var screenX = transform.OffsetX + x * transform.Scale;
            context.DrawLine(gridPen, new Point(screenX, transform.OffsetY + firstY * transform.Scale),
                new Point(screenX, transform.OffsetY + lastY * transform.Scale));
        }
        for (var y = firstY; y <= lastY; y++)
        {
            var screenY = transform.OffsetY + y * transform.Scale;
            context.DrawLine(gridPen, new Point(transform.OffsetX + firstX * transform.Scale, screenY),
                new Point(transform.OffsetX + lastX * transform.Scale, screenY));
        }

        if (transform.Scale < PixelValueScale) return;
        if (_pixelBytes is null)
        {
            _pixelBytes = new byte[_pixelStride * _imageHeight];
            _image.CopyPixels(_pixelBytes, _pixelStride, 0);
        }
        for (var y = firstY; y < lastY; y++)
        {
            for (var x = firstX; x < lastX; x++)
            {
                var offset = y * _pixelStride + x * 4;
                var blue = _pixelBytes[offset];
                var green = _pixelBytes[offset + 1];
                var red = _pixelBytes[offset + 2];
                var luminance = red * 0.299 + green * 0.587 + blue * 0.114;
                var brush = luminance > 145 ? Brushes.Black : Brushes.White;
                var value = _sourceChannels == 1
                    ? red.ToString()
                    : $"R{red}\nG{green}\nB{blue}";
                var fontSize = _sourceChannels == 1 ? 9d : 8d;
                var text = CreateText(value, fontSize, brush);
                var cell = new Rect(transform.OffsetX + x * transform.Scale,
                    transform.OffsetY + y * transform.Scale,
                    transform.Scale, transform.Scale);
                context.PushClip(new RectangleGeometry(cell));
                context.DrawText(text, new Point(
                    cell.X + Math.Max(1, (cell.Width - text.Width) / 2),
                    cell.Y + Math.Max(1, (cell.Height - text.Height) / 2)));
                context.Pop();
            }
        }

        var note = _sourceBitDepth > 8 ? "像素值：8 位显示值" : "像素值";
        var noteText = CreateText(note, 12, Brushes.White);
        var noteRect = new Rect(Math.Max(10, ActualWidth - noteText.Width - 26),
            10, noteText.Width + 16, noteText.Height + 8);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(210, 20, 20, 20)), null, noteRect);
        context.DrawText(noteText, new Point(noteRect.X + 8, noteRect.Y + 4));
    }

    private void DrawCenteredText(DrawingContext context, string text, double size, Brush brush)
    {
        var formatted = CreateText(text, size, brush);
        context.DrawText(formatted,
            new Point((ActualWidth - formatted.Width) / 2, (ActualHeight - formatted.Height) / 2));
    }

    private void DrawHint(DrawingContext context, string text)
    {
        var formatted = CreateText(text, 13, Brushes.White);
        var rectangle = new Rect(10, 10, formatted.Width + 16, formatted.Height + 10);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(220, 20, 20, 20)), null, rectangle);
        context.DrawText(formatted, new Point(18, 15));
    }

    private FormattedText CreateText(string text, double size, Brush brush) => new(
        text, System.Globalization.CultureInfo.CurrentUICulture,
        FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), size, brush,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private Transform GetTransform()
    {
        if (_image is null || _imageWidth == 0 || _imageHeight == 0)
            return new Transform(1, 0, 0);
        var fit = GetFitScale();
        var scale = Math.Max(0.0001, fit * _zoomMultiplier);
        return new Transform(scale,
            (ActualWidth - _imageWidth * scale) / 2 + _pan.X,
            (ActualHeight - _imageHeight * scale) / 2 + _pan.Y);
    }

    private double GetFitScale()
    {
        if (_imageWidth == 0 || _imageHeight == 0) return 1d;
        return Math.Max(0.0001,
            Math.Min(ActualWidth / _imageWidth, ActualHeight / _imageHeight));
    }

    private bool TryScreenToImage(Point point, out Point imagePoint)
    {
        imagePoint = ScreenToImageUnclamped(point);
        if (_image is null || imagePoint.X < 0 || imagePoint.Y < 0 ||
            imagePoint.X > _imageWidth || imagePoint.Y > _imageHeight)
            return false;
        imagePoint = new Point(Math.Clamp(imagePoint.X, 0, _imageWidth),
            Math.Clamp(imagePoint.Y, 0, _imageHeight));
        return true;
    }

    private Point ScreenToImageUnclamped(Point point)
    {
        var transform = GetTransform();
        return new Point((point.X - transform.OffsetX) / transform.Scale,
            (point.Y - transform.OffsetY) / transform.Scale);
    }

    private static Rect Normalize(Point first, Point second) => new(
        Math.Min(first.X, second.X), Math.Min(first.Y, second.Y),
        Math.Abs(first.X - second.X), Math.Abs(first.Y - second.Y));

    private static Rect ToScreen(Rect rectangle, Transform transform) => new(
        transform.OffsetX + rectangle.X * transform.Scale,
        transform.OffsetY + rectangle.Y * transform.Scale,
        rectangle.Width * transform.Scale,
        rectangle.Height * transform.Scale);

    private static BitmapSource ConvertBitmap(DrawingBitmap bitmap)
    {
        var rectangle = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var source = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96,
                PixelFormats.Bgra32, null, data.Scan0,
                Math.Abs(data.Stride) * bitmap.Height, data.Stride);
            source.Freeze();
            return source;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private readonly record struct Transform(double Scale, double OffsetX, double OffsetY);
}
