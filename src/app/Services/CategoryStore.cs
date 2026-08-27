using System.Text.Json;

namespace LabelReviewer.Services;

public sealed class CategoryStore
{
    private static readonly string[] DefaultCategories =
    [
        "异色", "颗粒", "脏污", "暗边", "成像异常", "亮线", "背景"
    ];

    private readonly List<string> _categories = [];
    public IReadOnlyList<string> Categories => _categories;

    public void Add(string category)
    {
        category = category.Trim();
        if (category.Length == 0 || _categories.Contains(category, StringComparer.CurrentCultureIgnoreCase))
            return;
        _categories.Add(category);
        _categories.Sort(StringComparer.CurrentCultureIgnoreCase);
    }

    public void Load(string outputRoot)
    {
        _categories.Clear();
        foreach (var category in DefaultCategories) Add(category);
        var path = Path.Combine(outputRoot, "categories.json");
        if (!File.Exists(path)) return;
        try
        {
            var values = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path));
            if (values is not null)
                foreach (var value in values) Add(value);
        }
        catch (JsonException)
        {
            // A damaged category cache must never prevent image review.
        }
    }

    public void Save(string outputRoot)
    {
        Directory.CreateDirectory(outputRoot);
        var json = JsonSerializer.Serialize(_categories,
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(outputRoot, "categories.json"), json);
    }
}
