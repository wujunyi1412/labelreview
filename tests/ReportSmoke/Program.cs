using System.Drawing;
using System.IO.Compression;
using System.Text;
using LabelReviewer.Models;
using LabelReviewer.Services;

var outputRoot = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("report-smoke-output");
var first = new ImageItem { FullPath = @"D:\samples\line1\image001.png", RelativePath = @"line1\image001.png" };
first.Annotations.Add(new Annotation { ReviewType = "漏检", Category = "类别1", Bounds = new Rectangle(1, 2, 30, 40) });
first.Annotations.Add(new Annotation { ReviewType = "漏检", Category = "类别1", Bounds = new Rectangle(10, 20, 30, 40) });
first.Annotations.Add(new Annotation { ReviewType = "误检", Category = "类别2", Bounds = new Rectangle(5, 6, 20, 20) });

var second = new ImageItem { FullPath = @"D:\samples\line1\image002.jpg", RelativePath = @"line1\image002.jpg" };
second.Annotations.Add(new Annotation { ReviewType = "误检", Category = "类别2", Bounds = new Rectangle(8, 9, 10, 11) });
second.Annotations.Add(new Annotation { ReviewType = "误检", Category = "类别3", Bounds = new Rectangle(18, 19, 20, 21) });

var third = new ImageItem { FullPath = @"D:\samples\line2\image003.bmp", RelativePath = @"line2\image003.bmp" };
var path = new ExcelReportService().Save(outputRoot, [first, second, third]);
using (var archive = ZipFile.OpenRead(path))
{
    AssertContains(ReadEntry(archive, "xl/workbook.xml"), "图片明细", "工作表名称");
    var details = ReadEntry(archive, "xl/worksheets/sheet1.xml");
    AssertContains(details, "2类别1", "漏检类别合并");
    AssertContains(details, "1类别2+1类别3", "误检类别合并");
    AssertContains(details, "ng_漏检+误检", "混合问题判定");
    AssertContains(details, ">ok<", "正常图片判定");
    var imageSummary = ReadEntry(archive, "xl/worksheets/sheet2.xml");
    AssertContains(imageSummary, "有问题图片数", "图片级汇总");
    AssertContains(imageSummary, "ng_*", "图片级统计公式");
    var boxSummary = ReadEntry(archive, "xl/worksheets/sheet3.xml");
    AssertContains(boxSummary, "漏检框总数", "框级汇总");
    AssertContains(boxSummary, "漏检类别数", "框级类别统计");
}
Console.WriteLine(path);

static string ReadEntry(ZipArchive archive, string name)
{
    var entry = archive.GetEntry(name) ?? throw new InvalidDataException($"缺少 {name}");
    using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
    return reader.ReadToEnd();
}

static void AssertContains(string value, string expected, string description)
{
    if (!value.Contains(expected, StringComparison.Ordinal))
        throw new InvalidDataException($"{description}验证失败：缺少 {expected}");
}
