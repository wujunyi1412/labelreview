using LabelReviewer.Models;

namespace LabelReviewer.Services;

public static class ImageCatalog
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".tif", ".tiff"
    };

    public static List<ImageItem> Scan(string root, string? excludedRoot = null)
        => Scan(root, [root], excludedRoot);

    public static List<ImageItem> Scan(
        string root, IEnumerable<string> includedRoots, string? excludedRoot = null)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var candidateExclusion = excludedRoot is null ? null
            : Path.GetFullPath(excludedRoot).TrimEnd(Path.DirectorySeparatorChar);
        var normalizedExclusion = candidateExclusion is not null &&
                                  !string.Equals(candidateExclusion, normalizedRoot,
                                      StringComparison.OrdinalIgnoreCase)
            ? candidateExclusion + Path.DirectorySeparatorChar
            : null;
        return includedRoots.SelectMany(scanRoot =>
                Directory.EnumerateFiles(scanRoot, "*", SearchOption.AllDirectories))
            .Where(path => Extensions.Contains(Path.GetExtension(path)))
            .Where(path => normalizedExclusion is null ||
                !Path.GetFullPath(path).StartsWith(normalizedExclusion, StringComparison.OrdinalIgnoreCase))
            .Select(path => new ImageItem
            {
                FullPath = path,
                RelativePath = Path.GetRelativePath(root, path)
            })
            .DistinctBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item.RelativePath, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
