using System.Drawing;

namespace LabelReviewer.Models;

public sealed class Annotation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ReviewType { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public Rectangle Bounds { get; set; }

    public override string ToString() =>
        $"{(ReviewType.Length == 0 ? "未指定" : ReviewType)} / {Category}  " +
        $"({Bounds.X}, {Bounds.Y}, {Bounds.Width}, {Bounds.Height})";
}
