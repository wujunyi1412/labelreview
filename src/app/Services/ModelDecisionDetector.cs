using System.Drawing;

namespace LabelReviewer.Services;

public static class ModelDecisionDetector
{
    private const int RegionWidth = 50;
    private const int RegionHeight = 30;

    public static string? Detect(Bitmap bitmap)
    {
        var width = Math.Min(RegionWidth, bitmap.Width);
        var height = Math.Min(RegionHeight, bitmap.Height);
        if (width <= 0 || height <= 0) return null;

        var redPixels = 0;
        var greenPixels = 0;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var color = bitmap.GetPixel(x, y);
            var max = Math.Max(color.R, Math.Max(color.G, color.B));
            var min = Math.Min(color.R, Math.Min(color.G, color.B));

            // Ignore the black background, gray text and weak compression noise.
            if (max < 70 || max - min < 35) continue;

            var hue = color.GetHue();
            if (hue <= 25f || hue >= 335f)
                redPixels++;
            else if (hue >= 70f && hue <= 170f)
                greenPixels++;
        }

        var minimumEvidence = Math.Max(6, width * height / 200);
        if (greenPixels >= minimumEvidence && greenPixels >= redPixels * 3 / 2 + 1)
            return "OK";
        if (redPixels >= minimumEvidence && redPixels >= greenPixels * 3 / 2 + 1)
            return "NG";
        return null;
    }
}
