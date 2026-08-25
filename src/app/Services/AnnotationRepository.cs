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

    public void Save(ImageItem item)
    {
        var destinationImage = Path.Combine(_outputRoot, item.RelativePath);
        var directory = Path.GetDirectoryName(destinationImage)!;
        Directory.CreateDirectory(directory);

        var sourceFullPath = Path.GetFullPath(item.FullPath);
        var destinationFullPath = Path.GetFullPath(destinationImage);
        if (!string.Equals(sourceFullPath, destinationFullPath,
                StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourceFullPath, destinationFullPath, true);
        }

        var document = new AnnotationDocument
        {
            ImageName = Path.GetFileName(item.FullPath),
            RelativePath = item.RelativePath.Replace('\\', '/'),
            Width = item.Width,
            Height = item.Height,
            SourceBitDepth = item.SourceBitDepth,
            SourceChannels = item.SourceChannels,
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

    private string JsonPath(ImageItem item) =>
        Path.Combine(_outputRoot, item.RelativePath + ".json");

    private sealed class AnnotationDocument
    {
        public string ImageName { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public int SourceBitDepth { get; set; }
        public int SourceChannels { get; set; }
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
