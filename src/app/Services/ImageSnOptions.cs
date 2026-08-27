using LabelReviewer.Models;

namespace LabelReviewer.Services;

public static class ImageSnExtractor
{
    public static string Extract(ImageItem image, ImageSnOptions? options = null)
    {
        options ??= new ImageSnOptions();
        var segments = image.FullPath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '\\', '/'],
            StringSplitOptions.RemoveEmptyEntries);
        var anchor = options.AnchorFolderName.Trim().Trim('\\', '/');
        var anchorIndex = Array.FindIndex(segments, segment =>
            string.Equals(segment, anchor, StringComparison.OrdinalIgnoreCase));
        var hasAnchoredSource = anchorIndex >= 0 && anchorIndex + 1 < segments.Length - 1;
        if (!hasAnchoredSource) return image.FullPath;

        var source = segments[anchorIndex + 1];
        if (source.Length == 0) return image.FullPath;
        var underscores = source.Select((character, index) => (character, index))
            .Where(value => value.character == '_')
            .Select(value => value.index)
            .ToArray();
        var start = ResolveStart(options.StartUnderscore, underscores);
        var end = ResolveEnd(options.EndUnderscore, underscores, source.Length);
        return start >= 0 && end > start && end <= source.Length
            ? source[start..end]
            : image.FullPath;
    }

    private static int ResolveStart(int? ordinal, IReadOnlyList<int> underscores)
    {
        if (ordinal is null) return 0;
        return ordinal > 0 && ordinal <= underscores.Count
            ? underscores[ordinal.Value - 1] + 1
            : -1;
    }

    private static int ResolveEnd(int? ordinal, IReadOnlyList<int> underscores, int length)
    {
        if (ordinal is null) return length;
        return ordinal > 0 && ordinal <= underscores.Count
            ? underscores[ordinal.Value - 1]
            : -1;
    }
}
