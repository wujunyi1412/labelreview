using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;
using LabelReviewer.Models;

namespace LabelReviewer.Services;

public sealed class AnnotationRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _outputRoot;

    public AnnotationRepository(string outputRoot)
    {
        _outputRoot = outputRoot;
    }

    public void Load(ImageItem item)
    {
        if (item.AnnotationDataLoaded) return;
        item.AnnotationDataLoaded = true;
        var path = JsonPath(item);
        if (!File.Exists(path)) return;

        try
        {
            var document = JsonSerializer.Deserialize<AnnotationDocument>(
                File.ReadAllText(path), JsonOptions);
            if (document is null) return;
            item.Width = document.Width;
            item.Height = document.Height;
            item.SourceBitDepth = document.SourceBitDepth;
            item.SourceChannels = document.SourceChannels;
            item.ModelDecision = document.ModelDecision is "OK" or "NG"
                ? document.ModelDecision
                : "无";
            item.ManualDecision = document.ManualDecision is "OK" or "NG"
                ? document.ManualDecision
                : "无";
            item.SnOptions = NormalizeSnOptions(document.SnOptions);
            item.Annotations.Clear();
            foreach (var box in document.Annotations)
            {
                item.Annotations.Add(new Annotation
                {
                    Id = box.Id == Guid.Empty ? Guid.NewGuid() : box.Id,
                    ReviewType = box.ReviewType ?? string.Empty,
                    Category = box.Category,
                    Bounds = new Rectangle(box.X, box.Y, box.Width, box.Height)
                });
            }
        }
        catch (Exception) when (File.Exists(path))
        {
            // Keep the application usable; Save() will replace malformed JSON.
        }
    }

    public void Save(ImageItem item, Bitmap? displayBitmap = null)
    {
        var destinationImage = Path.Combine(_outputRoot, item.RelativePath);
        var directory = Path.GetDirectoryName(destinationImage)!;
        Directory.CreateDirectory(directory);

        var sourceFullPath = Path.GetFullPath(item.FullPath);
        var destinationFullPath = Path.GetFullPath(destinationImage);
        if (string.Equals(sourceFullPath, destinationFullPath,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "输出文件夹不能与输入文件夹相同，否则会覆盖原图。");

        if (item.Annotations.Count == 0)
        {
            File.Copy(sourceFullPath, destinationFullPath, true);
        }
        else if (displayBitmap is not null)
        {
            SaveAnnotatedImage(displayBitmap, item.Annotations, destinationFullPath);
        }
        else
        {
            using var loaded = NativeImageLoader.Load(sourceFullPath).Bitmap;
            SaveAnnotatedImage(loaded, item.Annotations, destinationFullPath);
        }

        var document = new AnnotationDocument
        {
            ImageName = Path.GetFileName(item.FullPath),
            RelativePath = item.RelativePath.Replace('\\', '/'),
            Width = item.Width,
            Height = item.Height,
            SourceBitDepth = item.SourceBitDepth,
            SourceChannels = item.SourceChannels,
            ModelDecision = item.ModelDecision,
            ManualDecision = item.ManualDecision,
            SnOptions = item.SnOptions,
            Annotations = item.Annotations.Select(annotation => new AnnotationData
            {
                Id = annotation.Id,
                ReviewType = annotation.ReviewType,
                Category = annotation.Category,
                X = annotation.Bounds.X,
                Y = annotation.Bounds.Y,
                Width = annotation.Bounds.Width,
                Height = annotation.Bounds.Height,
                Left = annotation.Bounds.Left,
                Top = annotation.Bounds.Top,
                Right = annotation.Bounds.Right,
                Bottom = annotation.Bounds.Bottom
            }).ToList()
        };

        File.WriteAllText(JsonPath(item), JsonSerializer.Serialize(document, JsonOptions));
    }

    private static void SaveAnnotatedImage(
        Bitmap source, IEnumerable<Annotation> annotations, string destinationPath)
    {
        using var output = new Bitmap(source);
        using (var graphics = Graphics.FromImage(output))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            var shortSide = Math.Max(1, Math.Min(output.Width, output.Height));
            var lineWidth = Math.Clamp(shortSide / 500f, 2f, 8f);
            var fontSize = Math.Clamp(shortSide / 55f, 14f, 34f);
            using var font = new Font(
                "Microsoft YaHei UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);

            foreach (var annotation in annotations)
            {
                var borderColor = annotation.ReviewType switch
                {
                    "误检" => Color.LimeGreen,
                    "漏检" => Color.Gold,
                    _ => Color.Red
                };
                var backgroundColor = annotation.ReviewType switch
                {
                    "误检" => Color.DarkGreen,
                    "漏检" => Color.DarkGoldenrod,
                    _ => Color.DarkRed
                };
                using var pen = new Pen(borderColor, lineWidth);
                graphics.DrawRectangle(pen, annotation.Bounds);

                var reviewType = annotation.ReviewType.Length == 0
                    ? "未指定"
                    : annotation.ReviewType;
                var text = $"{reviewType} / {annotation.Category}";
                var textSize = graphics.MeasureString(text, font);
                const float padding = 4f;
                var labelY = annotation.Bounds.Top - textSize.Height - padding * 2;
                if (labelY < 0) labelY = annotation.Bounds.Top;
                var labelWidth = Math.Min(
                    output.Width, textSize.Width + padding * 2);
                var labelX = Math.Clamp(
                    annotation.Bounds.Left, 0f, Math.Max(0f, output.Width - labelWidth));
                var label = new RectangleF(
                    labelX,
                    labelY,
                    labelWidth,
                    textSize.Height + padding * 2);
                using var background = new SolidBrush(Color.FromArgb(220, backgroundColor));
                graphics.FillRectangle(background, label);
                graphics.DrawString(text, font, Brushes.White,
                    label.X + padding, label.Y + padding);
            }
        }

        var format = Path.GetExtension(destinationPath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => ImageFormat.Jpeg,
            ".tif" or ".tiff" => ImageFormat.Tiff,
            _ => ImageFormat.Png
        };
        var directory = Path.GetDirectoryName(destinationPath)!;
        var temporaryPath = Path.Combine(directory,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            output.Save(temporaryPath, format);
            File.Move(temporaryPath, destinationPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private string JsonPath(ImageItem item) =>
        Path.Combine(_outputRoot, item.RelativePath + ".json");

    private static ImageSnOptions? NormalizeSnOptions(ImageSnOptions? options)
    {
        if (options is null) return null;
        return new ImageSnOptions(
            string.IsNullOrWhiteSpace(options.AnchorFolderName)
                ? "images"
                : options.AnchorFolderName.Trim(),
            options.StartUnderscore is null or > 0 ? options.StartUnderscore : 2,
            options.EndUnderscore is null or > 0 ? options.EndUnderscore : 3);
    }

    private sealed class AnnotationDocument
    {
        public string ImageName { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public int SourceBitDepth { get; set; }
        public int SourceChannels { get; set; }
        public string ModelDecision { get; set; } = "无";
        public string ManualDecision { get; set; } = "无";
        public ImageSnOptions? SnOptions { get; set; }
        public List<AnnotationData> Annotations { get; set; } = [];
    }

    private sealed class AnnotationData
    {
        public Guid Id { get; set; }
        public string ReviewType { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Left { get; set; }
        public int Top { get; set; }
        public int Right { get; set; }
        public int Bottom { get; set; }
    }
}
