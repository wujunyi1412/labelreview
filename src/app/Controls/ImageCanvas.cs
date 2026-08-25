using System.Drawing.Drawing2D;
using System.ComponentModel;
using LabelReviewer.Models;

namespace LabelReviewer.Controls;

public sealed class ImageCanvas : Control
{
    private enum CreationState { Idle, Armed, Drawing }

    private Bitmap? _image;
    private IList<Annotation> _annotations = Array.Empty<Annotation>();
    private CreationState _creationState;
    private PointF _startImagePoint;
    private PointF _currentImagePoint;
    private float _zoomMultiplier = 1f;
    private PointF _pan;
    private bool _panning;
    private Point _lastPanPoint;
    private Point _lastMousePoint;
    private Guid? _selectedId;
    private readonly ContextMenuStrip _menu;

    public event Action<Rectangle>? BoxCompleted;
    public event Action<Guid?>? SelectionChanged;
    public event Action? DeleteRequested;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Guid? SelectedId
    {
        get => _selectedId;
        set
        {
            if (_selectedId == value) return;
            _selectedId = value;
            Invalidate();
        }
    }

    public ImageCanvas()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(28, 30, 34);
        Dock = DockStyle.Fill;
        TabStop = true;
        Cursor = Cursors.Default;

        _menu = new ContextMenuStrip();
        _menu.Items.Add("创建矩形框 (W)", null, (_, _) => BeginCreate(false));
        _menu.Items.Add("删除选中框 (Delete)", null, (_, _) => DeleteRequested?.Invoke());
    }

    public void SetImage(Bitmap? image, IList<Annotation> annotations)
    {
        _image = image;
        _annotations = annotations;
        _creationState = CreationState.Idle;
        SelectedId = null;
        FitToWindow();
    }

    public void FitToWindow()
    {
        _zoomMultiplier = 1f;
        _pan = PointF.Empty;
        Invalidate();
    }

    public void BeginCreate(bool startAtPointer = true)
    {
        if (_image is null) return;
        Focus();
        if (startAtPointer && TryScreenToImage(_lastMousePoint, out var point))
        {
            _startImagePoint = point;
            _currentImagePoint = point;
            _creationState = CreationState.Drawing;
        }
        else
        {
            _creationState = CreationState.Armed;
        }
        Cursor = Cursors.Cross;
        Invalidate();
    }

    public void CancelCreate()
    {
        _creationState = CreationState.Idle;
        Cursor = Cursors.Default;
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        Focus();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _lastMousePoint = e.Location;
        if (_panning)
        {
            _pan.X += e.X - _lastPanPoint.X;
            _pan.Y += e.Y - _lastPanPoint.Y;
            _lastPanPoint = e.Location;
            Invalidate();
            return;
        }

        if (_creationState == CreationState.Drawing && TryScreenToImage(e.Location, out var point))
        {
            _currentImagePoint = point;
            Invalidate();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        _lastMousePoint = e.Location;

        if (e.Button == MouseButtons.Middle)
        {
            _panning = true;
            _lastPanPoint = e.Location;
            Cursor = Cursors.SizeAll;
            return;
        }

        if (e.Button == MouseButtons.Right)
        {
            if (_creationState != CreationState.Idle)
                CancelCreate();
            _menu.Show(this, e.Location);
            return;
        }

        if (e.Button != MouseButtons.Left || _image is null) return;

        if (_creationState == CreationState.Armed)
        {
            if (TryScreenToImage(e.Location, out var start))
            {
                _startImagePoint = start;
                _currentImagePoint = start;
                _creationState = CreationState.Drawing;
            }
            return;
        }

        if (_creationState == CreationState.Drawing)
        {
            if (TryScreenToImage(e.Location, out var end))
                CompleteBox(end);
            return;
        }

        var selected = HitTest(e.Location);
        SelectedId = selected?.Id;
        SelectionChanged?.Invoke(SelectedId);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Middle)
        {
            _panning = false;
            Cursor = _creationState == CreationState.Idle ? Cursors.Default : Cursors.Cross;
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_image is null) return;

        var before = ScreenToImageUnclamped(e.Location);
        var factor = e.Delta > 0 ? 1.2f : 1f / 1.2f;
        _zoomMultiplier = Math.Clamp(_zoomMultiplier * factor, 0.1f, 30f);
        var transform = GetTransform();
        _pan.X += e.X - (transform.OffsetX + before.X * transform.Scale);
        _pan.Y += e.Y - (transform.OffsetY + before.Y * transform.Scale);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        if (_image is null)
        {
            DrawCenteredMessage(e.Graphics, "请选择输入文件夹");
            return;
        }

        var t = GetTransform();
        var destination = new RectangleF(t.OffsetX, t.OffsetY,
            _image.Width * t.Scale, _image.Height * t.Scale);
        e.Graphics.InterpolationMode = t.Scale >= 1f
            ? InterpolationMode.NearestNeighbor
            : InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.DrawImage(_image, destination);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var annotation in _annotations)
            DrawAnnotation(e.Graphics, annotation, t);

        if (_creationState == CreationState.Drawing)
        {
            var preview = Normalize(_startImagePoint, _currentImagePoint);
            var screen = ToScreen(preview, t);
            using var pen = new Pen(Color.Lime, 2f) { DashStyle = DashStyle.Dash };
            e.Graphics.DrawRectangle(pen, screen.X, screen.Y, screen.Width, screen.Height);
        }

        if (_creationState == CreationState.Armed)
            DrawHint(e.Graphics, "单击确定矩形第一个角，再单击完成");
        else if (_creationState == CreationState.Drawing)
            DrawHint(e.Graphics, "移动鼠标并单击完成；右键或 Esc 取消");
    }

    private void CompleteBox(PointF end)
    {
        var bounds = Normalize(_startImagePoint, end);
        _creationState = CreationState.Idle;
        Cursor = Cursors.Default;
        Invalidate();
        var rectangle = Rectangle.FromLTRB(
            (int)Math.Floor(bounds.Left), (int)Math.Floor(bounds.Top),
            (int)Math.Ceiling(bounds.Right), (int)Math.Ceiling(bounds.Bottom));
        rectangle.Intersect(new Rectangle(0, 0, _image!.Width, _image.Height));
        if (rectangle.Width >= 2 && rectangle.Height >= 2)
            BoxCompleted?.Invoke(rectangle);
    }

    private Annotation? HitTest(Point screenPoint)
    {
        if (!TryScreenToImage(screenPoint, out var imagePoint)) return null;
        var tolerance = Math.Max(3f, 6f / GetTransform().Scale);
        return _annotations.LastOrDefault(annotation =>
        {
            var expanded = RectangleF.Inflate(annotation.Bounds, tolerance, tolerance);
            return expanded.Contains(imagePoint);
        });
    }

    private void DrawAnnotation(Graphics graphics, Annotation annotation, Transform t)
    {
        var rectangle = ToScreen(annotation.Bounds, t);
        var selected = annotation.Id == SelectedId;
        using var pen = new Pen(selected ? Color.Yellow : Color.Red, selected ? 3f : 2f);
        graphics.DrawRectangle(pen, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);

        var text = annotation.Category;
        var size = graphics.MeasureString(text, Font);
        var label = new RectangleF(rectangle.X, Math.Max(0, rectangle.Y - size.Height - 2),
            size.Width + 6, size.Height + 2);
        using var background = new SolidBrush(Color.FromArgb(210,
            selected ? Color.Goldenrod : Color.DarkRed));
        graphics.FillRectangle(background, label);
        graphics.DrawString(text, Font, Brushes.White, label.X + 3, label.Y + 1);
    }

    private void DrawCenteredMessage(Graphics graphics, string text)
    {
        using var font = new Font(Font.FontFamily, 16f);
        var size = graphics.MeasureString(text, font);
        graphics.DrawString(text, font, Brushes.LightGray,
            (Width - size.Width) / 2f, (Height - size.Height) / 2f);
    }

    private void DrawHint(Graphics graphics, string text)
    {
        var size = graphics.MeasureString(text, Font);
        var rectangle = new RectangleF(10, 10, size.Width + 16, size.Height + 10);
        using var brush = new SolidBrush(Color.FromArgb(210, 20, 20, 20));
        graphics.FillRectangle(brush, rectangle);
        graphics.DrawString(text, Font, Brushes.White, 18, 15);
    }

    private Transform GetTransform()
    {
        if (_image is null) return new Transform(1f, 0f, 0f);
        var fit = Math.Min(ClientSize.Width / (float)_image.Width,
            ClientSize.Height / (float)_image.Height);
        var scale = Math.Max(0.0001f, fit * _zoomMultiplier);
        var offsetX = (ClientSize.Width - _image.Width * scale) / 2f + _pan.X;
        var offsetY = (ClientSize.Height - _image.Height * scale) / 2f + _pan.Y;
        return new Transform(scale, offsetX, offsetY);
    }

    private bool TryScreenToImage(Point point, out PointF imagePoint)
    {
        imagePoint = ScreenToImageUnclamped(point);
        if (_image is null || imagePoint.X < 0 || imagePoint.Y < 0 ||
            imagePoint.X > _image.Width || imagePoint.Y > _image.Height)
            return false;
        imagePoint.X = Math.Clamp(imagePoint.X, 0, _image.Width);
        imagePoint.Y = Math.Clamp(imagePoint.Y, 0, _image.Height);
        return true;
    }

    private PointF ScreenToImageUnclamped(Point point)
    {
        var t = GetTransform();
        return new PointF((point.X - t.OffsetX) / t.Scale,
            (point.Y - t.OffsetY) / t.Scale);
    }

    private static RectangleF Normalize(PointF first, PointF second) =>
        RectangleF.FromLTRB(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y),
            Math.Max(first.X, second.X), Math.Max(first.Y, second.Y));

    private static RectangleF ToScreen(RectangleF rectangle, Transform t) =>
        new(t.OffsetX + rectangle.X * t.Scale, t.OffsetY + rectangle.Y * t.Scale,
            rectangle.Width * t.Scale, rectangle.Height * t.Scale);

    private readonly record struct Transform(float Scale, float OffsetX, float OffsetY);
}
