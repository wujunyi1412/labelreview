using System.Drawing;
using System.IO.Compression;
using System.Text;
using LabelReviewer.Models;
using LabelReviewer.Services;

var outputRoot = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("report-smoke-output");
var first = new ImageItem
{
    FullPath = @"D:\samples\images\HUIQT_U02-3_CL1265T0005_20260815T085907\image001.png",
    RelativePath = @"images\HUIQT_U02-3_CL1265T0005_20260815T085907\image001.png",
    InputFolderName = "samples",
    ModelDecision = "NG", ManualDecision = "OK"
};
first.Annotations.Add(new Annotation { ReviewType = "漏检", Category = "类别1", Bounds = new Rectangle(1, 2, 30, 40) });
first.Annotations.Add(new Annotation { ReviewType = "漏检", Category = "类别1", Bounds = new Rectangle(10, 20, 30, 40) });
first.Annotations.Add(new Annotation { ReviewType = "误检", Category = "类别2", Bounds = new Rectangle(5, 6, 20, 20) });

var second = new ImageItem
{
    FullPath = @"D:\samples\line1\image002.jpg", RelativePath = @"line1\image002.jpg",
    ModelDecision = "OK", ManualDecision = "OK"
};
second.Annotations.Add(new Annotation { ReviewType = "误检", Category = "类别2", Bounds = new Rectangle(8, 9, 10, 11) });
second.Annotations.Add(new Annotation { ReviewType = "误检", Category = "类别3", Bounds = new Rectangle(18, 19, 20, 21) });

var third = new ImageItem
{
    FullPath = @"D:\samples\line2\image003.bmp", RelativePath = @"line2\image003.bmp",
    ModelDecision = "NG", ManualDecision = "NG"
};
var unreviewed = new ImageItem
{
    FullPath = @"D:\samples\line2\image004.png", RelativePath = @"line2\image004.png"
};
unreviewed.Annotations.Add(new Annotation
    { ReviewType = "漏检", Category = "未复判类别", Bounds = new Rectangle(1, 1, 2, 2) });
var partiallyReviewed = new ImageItem
{
    FullPath = @"D:\samples\line2\image005.png", RelativePath = @"line2\image005.png",
    ModelDecision = "OK"
};
var path = new ExcelReportService().Save(outputRoot,
    [first, second, third, unreviewed, partiallyReviewed]);
using (var archive = ZipFile.OpenRead(path))
{
    AssertContains(ReadEntry(archive, "xl/workbook.xml"), "图片明细", "工作表名称");
    var details = ReadEntry(archive, "xl/worksheets/sheet1.xml");
    AssertContains(details, "图片SN", "图片 SN 列标题");
    AssertContains(details, ">CL1265T0005<", "默认图片 SN 提取");
    AssertContains(details, "图片路径", "图片路径列标题");
    AssertContains(details, Path.Combine(first.InputFolderName, first.RelativePath),
        "从输入文件夹开始的图片路径");
    AssertNotContains(details, first.FullPath, "图片路径不包含输入文件夹之前的目录");
    AssertContains(details, "2*类别1", "漏检类别合并");
    AssertContains(details, "1*类别2+1*类别3", "误检类别合并");
    AssertContains(details, ">误检+漏检<", "混合问题判定");
    AssertContains(details, ">误检<", "误检图片判定");
    AssertContains(details, ">无漏检无误检<", "正常图片判定");
    if (details.Contains("ng_", StringComparison.Ordinal))
        throw new InvalidDataException("人工判定模型结果中仍包含旧 ng_ 前缀");
    AssertContains(details, "人工判定模型结果", "原判定列标题");
    AssertContains(details, "模型判定图片结果", "模型图片判定列标题");
    AssertContains(details, "人工判定图片结果", "人工图片判定列标题");
    AssertContains(details, "模型判断是否正确", "模型正确性列标题");
    AssertContains(details, "IF(F3=G3,\"正确\",\"错误\")", "模型正确性公式");
    AssertContains(details, ">错误<", "错误比较结果");
    AssertContains(details, ">正确<", "正确比较结果");
    AssertNotContains(details, "image004.png", "未复判图片过滤");
    AssertNotContains(details, "image005.png", "部分复判图片过滤");
    var imageSummary = ReadEntry(archive, "xl/worksheets/sheet2.xml");
    AssertContains(imageSummary, "有问题图片数", "图片级汇总");
    AssertContains(imageSummary, "扫描图片总数", "扫描总数");
    AssertContains(imageSummary, "已复判图片数", "已复判数量");
    AssertContains(imageSummary, "未复判图片数", "未复判数量");
    AssertContains(imageSummary, "复判完成率", "复判进度");
    AssertContains(imageSummary, "r=\"B4\" s=\"4\"><v>5</v>", "扫描总数数值");
    AssertContains(imageSummary, "r=\"B5\" s=\"4\"><f>COUNTA('图片明细'!B3:B5)</f><v>3</v>", "已复判数值");
    AssertContains(imageSummary, "r=\"B6\" s=\"4\"><f>B4-B5</f><v>2</v>", "未复判数值");
    AssertContains(imageSummary, "r=\"B7\" s=\"7\"><f>IF(B4=0,0,B5/B4)</f><v>0.6</v>", "完成率数值和格式");
    AssertContains(imageSummary, "IF(B4=0,0,B5/B4)", "完成率公式");
    AssertContains(imageSummary, "COUNTIF('图片明细'!E3:E5,\"误检+漏检\")", "图片级统计公式");
    AssertContains(imageSummary, "COUNTIF('图片明细'!E3:E5,\"无漏检无误检\")", "正常图片统计公式");
    var boxSummary = ReadEntry(archive, "xl/worksheets/sheet3.xml");
    AssertContains(boxSummary, "漏检框总数", "框级汇总");
    AssertContains(boxSummary, "漏检类别数", "框级类别统计");
    AssertNotContains(boxSummary, "未复判类别", "未复判标注过滤");
}

var snSource = new ImageItem
{
    FullPath = @"D:\samples\custom\HUIQT_U02-3_CL1265T0005_20260815T085907\image.png",
    RelativePath = @"custom\HUIQT_U02-3_CL1265T0005_20260815T085907\image.png"
};
if (ImageSnExtractor.Extract(snSource, new ImageSnOptions("custom", null, 1)) != "HUIQT")
    throw new InvalidDataException("从开头提取图片 SN 验证失败");
if (ImageSnExtractor.Extract(snSource, new ImageSnOptions("custom", 3, null)) != "20260815T085907")
    throw new InvalidDataException("提取图片 SN 到末尾验证失败");
if (ImageSnExtractor.Extract(snSource, new ImageSnOptions("missing", 2, 3)) !=
    snSource.FullPath)
    throw new InvalidDataException("图片 SN 定位文件夹缺失时的绝对路径回退验证失败");
if (ImageSnExtractor.Extract(snSource, new ImageSnOptions("custom", 8, 9)) !=
    snSource.FullPath)
    throw new InvalidDataException("图片 SN 下划线不足时的绝对路径回退验证失败");

var earlierSnItem = new ImageItem
{
    FullPath = snSource.FullPath,
    RelativePath = snSource.RelativePath,
    ModelDecision = "OK",
    ManualDecision = "OK",
    SnOptions = new ImageSnOptions("custom", null, 1)
};
var laterSnItem = new ImageItem
{
    FullPath = snSource.FullPath.Replace("image.png", "image2.png"),
    RelativePath = snSource.RelativePath.Replace("image.png", "image2.png"),
    ModelDecision = "OK",
    ManualDecision = "OK",
    SnOptions = new ImageSnOptions("custom", 3, null)
};
var perImageReportPath = new ExcelReportService().Save(
    Path.Combine(outputRoot, "per-image-sn"), [earlierSnItem, laterSnItem],
    new ImageSnOptions("missing", 8, 9));
using (var archive = ZipFile.OpenRead(perImageReportPath))
{
    var details = ReadEntry(archive, "xl/worksheets/sheet1.xml");
    AssertContains(details, ">HUIQT<", "较早图片保留自身 SN 规则");
    AssertContains(details, ">20260815T085907<", "后续图片使用更新后的 SN 规则");
}

var sourcePath = Path.Combine(outputRoot, "source-for-decision-test.png");
File.WriteAllBytes(sourcePath, [1, 2, 3]);
var repositoryRoot = Path.Combine(outputRoot, "decision-persistence");
var repository = new AnnotationRepository(repositoryRoot);
var decisionItem = new ImageItem
{
    FullPath = sourcePath,
    RelativePath = "decision-test.png",
    ModelDecision = "NG",
    ManualDecision = "OK",
    SnOptions = new ImageSnOptions("custom", null, 1)
};
repository.Save(decisionItem);
var reloadedDecisionItem = new ImageItem
{
    FullPath = sourcePath,
    RelativePath = "decision-test.png"
};
repository.Load(reloadedDecisionItem);
if (reloadedDecisionItem.ModelDecision != "NG")
    throw new InvalidDataException("模型判定结果持久化验证失败");
if (reloadedDecisionItem.ManualDecision != "OK")
    throw new InvalidDataException("人工判定结果持久化验证失败");
if (reloadedDecisionItem.SnOptions != decisionItem.SnOptions)
    throw new InvalidDataException("图片 SN 规则持久化验证失败");

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

static void AssertNotContains(string value, string unexpected, string description)
{
    if (value.Contains(unexpected, StringComparison.Ordinal))
        throw new InvalidDataException($"{description}验证失败：不应包含 {unexpected}");
}
