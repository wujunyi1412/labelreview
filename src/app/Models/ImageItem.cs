namespace LabelReviewer.Models;

public sealed class ImageItem
{
    public required string FullPath { get; init; }
    public required string RelativePath { get; init; }
    public string InputFolderName { get; init; } = string.Empty;
    public string ModelDecision { get; set; } = "无";
    public string ManualDecision { get; set; } = "无";
    public ImageSnOptions? SnOptions { get; set; }
    public List<Annotation> Annotations { get; } = [];
    public bool AnnotationDataLoaded { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int SourceBitDepth { get; set; }
    public int SourceChannels { get; set; }

    public override string ToString() => RelativePath;
}
