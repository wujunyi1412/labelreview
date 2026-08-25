using System.IO.Compression;
using System.Text;
using System.Xml;
using LabelReviewer.Models;

namespace LabelReviewer.Services;

public sealed class ExcelReportService
{
    public const string FileName = "review_results.xlsx";

    private const string SpreadsheetNamespace =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public string Save(string outputRoot, IReadOnlyList<ImageItem> images)
    {
        Directory.CreateDirectory(outputRoot);
        var destination = Path.Combine(outputRoot, FileName);
        var temporary = Path.Combine(outputRoot, $".{FileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew,
                       FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                WriteTextEntry(archive, "[Content_Types].xml", ContentTypesXml());
                WriteTextEntry(archive, "_rels/.rels", PackageRelationshipsXml());
                WriteTextEntry(archive, "docProps/core.xml", CorePropertiesXml());
                WriteTextEntry(archive, "docProps/app.xml", AppPropertiesXml());
                WriteTextEntry(archive, "xl/workbook.xml", WorkbookXml());
                WriteTextEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationshipsXml());
                WriteTextEntry(archive, "xl/styles.xml", StylesXml());
                WriteImageDetailsSheet(archive, images);
                WriteImageSummarySheet(archive, images);
                WriteBoxSummarySheet(archive, images);
            }

            File.Move(temporary, destination, overwrite: true);
            return destination;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void WriteImageDetailsSheet(ZipArchive archive,
        IReadOnlyList<ImageItem> images)
    {
        var rows = new List<RowData>
        {
            new(1, [Text("A", "图片检测明细", 1)]),
            new(2,
            [
                Text("A", "图片路径", 2), Text("B", "图片名称", 2),
                Text("C", "漏检标注信息", 2), Text("D", "误检标注信息", 2),
                Text("E", "判定", 2)
            ])
        };

        for (var index = 0; index < images.Count; index++)
        {
            var image = images[index];
            var missed = image.Annotations.Where(value => value.ReviewType == "漏检").ToList();
            var falsePositive = image.Annotations.Where(value => value.ReviewType == "误检").ToList();
            var status = GetStatus(missed.Count > 0, falsePositive.Count > 0);
            var statusStyle = status == "ok" ? 5u : 6u;
            var row = (uint)index + 3;
            rows.Add(new RowData(row,
            [
                Text("A", image.FullPath, 3),
                Text("B", Path.GetFileName(image.FullPath), 3),
                Text("C", FormatAnnotations(missed), 3),
                Text("D", FormatAnnotations(falsePositive), 3),
                Text("E", status, statusStyle)
            ]));
        }

        WriteWorksheet(archive, "xl/worksheets/sheet1.xml", rows,
            [(1, 56d), (2, 28d), (3, 34d), (4, 34d), (5, 22d)],
            mergeReference: "A1:E1", freezeRows: 2,
            autoFilterReference: $"A2:E{Math.Max(2, images.Count + 2)}");
    }

    private static void WriteImageSummarySheet(ZipArchive archive,
        IReadOnlyList<ImageItem> images)
    {
        var states = images.Select(image => new
        {
            HasMissed = image.Annotations.Any(value => value.ReviewType == "漏检"),
            HasFalse = image.Annotations.Any(value => value.ReviewType == "误检")
        }).ToList();
        var lastDetailRow = Math.Max(3, images.Count + 2);
        var detailStatusRange = $"'图片明细'!E3:E{lastDetailRow}";
        var detailsNameRange = $"'图片明细'!B3:B{lastDetailRow}";

        var rows = new List<RowData>
        {
            new(1, [Text("A", "图片级汇总", 1)]),
            new(3, [Text("A", "统计项目", 2), Text("B", "图片数量", 2)]),
            new(4, [Text("A", "总图片数", 3), Formula("B", $"COUNTA({detailsNameRange})", images.Count, 4)]),
            new(5, [Text("A", "有问题图片数", 3), Formula("B", $"COUNTIF({detailStatusRange},\"ng_*\")", states.Count(s => s.HasMissed || s.HasFalse), 4)]),
            new(6, [Text("A", "只有漏检", 3), Formula("B", $"COUNTIF({detailStatusRange},\"ng_漏检\")", states.Count(s => s.HasMissed && !s.HasFalse), 4)]),
            new(7, [Text("A", "只有误检", 3), Formula("B", $"COUNTIF({detailStatusRange},\"ng_误检\")", states.Count(s => !s.HasMissed && s.HasFalse), 4)]),
            new(8, [Text("A", "同时有漏检和误检", 3), Formula("B", $"COUNTIF({detailStatusRange},\"ng_漏检+误检\")", states.Count(s => s.HasMissed && s.HasFalse), 4)]),
            new(9, [Text("A", "没有问题", 3), Formula("B", $"COUNTIF({detailStatusRange},\"ok\")", states.Count(s => !s.HasMissed && !s.HasFalse), 4)])
        };

        WriteWorksheet(archive, "xl/worksheets/sheet2.xml", rows,
            [(1, 32d), (2, 18d)], mergeReference: "A1:B1", freezeRows: 3);
    }

    private static void WriteBoxSummarySheet(ZipArchive archive,
        IReadOnlyList<ImageItem> images)
    {
        var annotations = images.SelectMany(image => image.Annotations).ToList();
        var missed = annotations.Where(value => value.ReviewType == "漏检").ToList();
        var falsePositive = annotations.Where(value => value.ReviewType == "误检").ToList();
        var missedGroups = GroupByCategory(missed);
        var falseGroups = GroupByCategory(falsePositive);

        var rows = new List<RowData>
        {
            new(1, [Text("A", "标注框级汇总", 1)]),
            new(3, [Text("A", "统计项目", 2), Text("B", "数量", 2)]),
            new(4, [Text("A", "漏检框总数", 3), Number("B", missed.Count, 4)]),
            new(5, [Text("A", "漏检类别数", 3), Number("B", missedGroups.Count, 4)]),
            new(6, [Text("A", "误检框总数", 3), Number("B", falsePositive.Count, 4)]),
            new(7, [Text("A", "误检类别数", 3), Number("B", falseGroups.Count, 4)]),
            new(9, [Text("A", "问题类型", 2), Text("B", "类别", 2), Text("C", "框数量", 2)])
        };

        uint row = 10;
        foreach (var group in missedGroups)
            rows.Add(new RowData(row++,
                [Text("A", "漏检", 3), Text("B", group.Category, 3), Number("C", group.Count, 4)]));
        foreach (var group in falseGroups)
            rows.Add(new RowData(row++,
                [Text("A", "误检", 3), Text("B", group.Category, 3), Number("C", group.Count, 4)]));
        if (row == 10)
            rows.Add(new RowData(row, [Text("A", "无", 3), Text("B", "0", 3), Number("C", 0, 4)]));

        WriteWorksheet(archive, "xl/worksheets/sheet3.xml", rows,
            [(1, 22d), (2, 30d), (3, 16d)], mergeReference: "A1:C1", freezeRows: 9,
            autoFilterReference: $"A9:C{Math.Max(10, row - 1)}");
    }

    private static List<CategoryCount> GroupByCategory(IEnumerable<Annotation> annotations) =>
        annotations.GroupBy(value => string.IsNullOrWhiteSpace(value.Category) ? "未指定" : value.Category)
            .Select(group => new CategoryCount(group.Key, group.Count()))
            .OrderBy(group => group.Category, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static string FormatAnnotations(IReadOnlyCollection<Annotation> annotations)
    {
        if (annotations.Count == 0) return "0";
        return string.Join("+", GroupByCategory(annotations)
            .Select(group => $"{group.Count}{group.Category}"));
    }

    private static string GetStatus(bool hasMissed, bool hasFalse) =>
        (hasMissed, hasFalse) switch
        {
            (true, true) => "ng_漏检+误检",
            (true, false) => "ng_漏检",
            (false, true) => "ng_误检",
            _ => "ok"
        };

    private static void WriteWorksheet(ZipArchive archive, string path,
        IReadOnlyList<RowData> rows, IReadOnlyList<(int Index, double Width)> columns,
        string? mergeReference = null, int freezeRows = 0, string? autoFilterReference = null)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = XmlWriter.Create(stream, XmlSettings());
        writer.WriteStartDocument(true);
        writer.WriteStartElement("worksheet", SpreadsheetNamespace);

        writer.WriteStartElement("sheetViews");
        writer.WriteStartElement("sheetView");
        writer.WriteAttributeString("showGridLines", "0");
        writer.WriteAttributeString("workbookViewId", "0");
        if (freezeRows > 0)
        {
            writer.WriteStartElement("pane");
            writer.WriteAttributeString("ySplit", freezeRows.ToString());
            writer.WriteAttributeString("topLeftCell", $"A{freezeRows + 1}");
            writer.WriteAttributeString("activePane", "bottomLeft");
            writer.WriteAttributeString("state", "frozen");
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteEndElement();

        writer.WriteStartElement("sheetFormatPr");
        writer.WriteAttributeString("defaultRowHeight", "18");
        writer.WriteEndElement();
        writer.WriteStartElement("cols");
        foreach (var column in columns)
        {
            writer.WriteStartElement("col");
            writer.WriteAttributeString("min", column.Index.ToString());
            writer.WriteAttributeString("max", column.Index.ToString());
            writer.WriteAttributeString("width", column.Width.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteAttributeString("customWidth", "1");
            writer.WriteEndElement();
        }
        writer.WriteEndElement();

        writer.WriteStartElement("sheetData");
        foreach (var row in rows.OrderBy(value => value.Index))
        {
            writer.WriteStartElement("row");
            writer.WriteAttributeString("r", row.Index.ToString());
            writer.WriteAttributeString("ht", row.Index == 1 ? "28" : "20");
            writer.WriteAttributeString("customHeight", "1");
            foreach (var cell in row.Cells) WriteCell(writer, cell, row.Index);
            writer.WriteEndElement();
        }
        writer.WriteEndElement();

        if (autoFilterReference is not null)
        {
            writer.WriteStartElement("autoFilter");
            writer.WriteAttributeString("ref", autoFilterReference);
            writer.WriteEndElement();
        }
        if (mergeReference is not null)
        {
            writer.WriteStartElement("mergeCells");
            writer.WriteAttributeString("count", "1");
            writer.WriteStartElement("mergeCell");
            writer.WriteAttributeString("ref", mergeReference);
            writer.WriteEndElement();
            writer.WriteEndElement();
        }
        writer.WriteStartElement("pageMargins");
        writer.WriteAttributeString("left", "0.4");
        writer.WriteAttributeString("right", "0.4");
        writer.WriteAttributeString("top", "0.6");
        writer.WriteAttributeString("bottom", "0.6");
        writer.WriteAttributeString("header", "0.3");
        writer.WriteAttributeString("footer", "0.3");
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteCell(XmlWriter writer, CellData cell, uint row)
    {
        writer.WriteStartElement("c");
        writer.WriteAttributeString("r", $"{cell.Column}{row}");
        writer.WriteAttributeString("s", cell.Style.ToString());
        if (cell.Kind == CellKind.Text)
        {
            writer.WriteAttributeString("t", "inlineStr");
            writer.WriteStartElement("is");
            writer.WriteStartElement("t");
            writer.WriteAttributeString("xml", "space", null, "preserve");
            writer.WriteString(cell.Value);
            writer.WriteEndElement();
            writer.WriteEndElement();
        }
        else if (cell.Kind == CellKind.Formula)
        {
            writer.WriteElementString("f", cell.Value);
            writer.WriteElementString("v", cell.CachedValue ?? "0");
        }
        else
        {
            writer.WriteElementString("v", cell.Value);
        }
        writer.WriteEndElement();
    }

    private static CellData Text(string column, string value, uint style) =>
        new(column, CellKind.Text, value, style);

    private static CellData Number(string column, int value, uint style) =>
        new(column, CellKind.Number, value.ToString(), style);

    private static CellData Formula(string column, string formula, int cachedValue, uint style) =>
        new(column, CellKind.Formula, formula, style, cachedValue.ToString());

    private static void WriteTextEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static XmlWriterSettings XmlSettings() => new()
    {
        Encoding = new UTF8Encoding(false),
        Indent = false,
        CloseOutput = false
    };

    private static string ContentTypesXml() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
          <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
          <Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
          <Override PartName="/xl/worksheets/sheet3.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
          <Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
          <Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>
        </Types>
        """;

    private static string PackageRelationshipsXml() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>
          <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/>
        </Relationships>
        """;

    private static string WorkbookXml() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <bookViews><workbookView/></bookViews>
          <sheets>
            <sheet name="图片明细" sheetId="1" r:id="rId1"/>
            <sheet name="图片级汇总" sheetId="2" r:id="rId2"/>
            <sheet name="标注框汇总" sheetId="3" r:id="rId3"/>
          </sheets>
          <calcPr calcId="191029" fullCalcOnLoad="1" forceFullCalc="1"/>
        </workbook>
        """;

    private static string WorkbookRelationshipsXml() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/>
          <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet3.xml"/>
          <Relationship Id="rId4" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
        </Relationships>
        """;

    private static string StylesXml() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <fonts count="3">
            <font><sz val="11"/><name val="Microsoft YaHei"/><family val="2"/></font>
            <font><b/><color rgb="FFFFFFFF"/><sz val="15"/><name val="Microsoft YaHei"/></font>
            <font><b/><color rgb="FFFFFFFF"/><sz val="11"/><name val="Microsoft YaHei"/></font>
          </fonts>
          <fills count="6">
            <fill><patternFill patternType="none"/></fill>
            <fill><patternFill patternType="gray125"/></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FF174E5C"/><bgColor indexed="64"/></patternFill></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FF287C8E"/><bgColor indexed="64"/></patternFill></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FFE8F5E9"/><bgColor indexed="64"/></patternFill></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FFFFF3CD"/><bgColor indexed="64"/></patternFill></fill>
          </fills>
          <borders count="2">
            <border><left/><right/><top/><bottom/><diagonal/></border>
            <border><left style="thin"><color rgb="FFD7E1E5"/></left><right style="thin"><color rgb="FFD7E1E5"/></right><top style="thin"><color rgb="FFD7E1E5"/></top><bottom style="thin"><color rgb="FFD7E1E5"/></bottom><diagonal/></border>
          </borders>
          <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
          <cellXfs count="7">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
            <xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>
            <xf numFmtId="0" fontId="2" fillId="3" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="center" vertical="center" wrapText="1"/></xf>
            <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyAlignment="1"><alignment vertical="center" wrapText="1"/></xf>
            <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>
            <xf numFmtId="0" fontId="0" fillId="4" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>
            <xf numFmtId="0" fontId="0" fillId="5" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>
          </cellXfs>
          <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
        </styleSheet>
        """;

    private static string CorePropertiesXml()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <dc:creator>LabelReviewer</dc:creator><cp:lastModifiedBy>LabelReviewer</cp:lastModifiedBy>
              <dcterms:created xsi:type="dcterms:W3CDTF">{timestamp}</dcterms:created>
              <dcterms:modified xsi:type="dcterms:W3CDTF">{timestamp}</dcterms:modified>
            </cp:coreProperties>
            """;
    }

    private static string AppPropertiesXml() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties" xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
          <Application>LabelReviewer</Application><AppVersion>1.0</AppVersion>
          <TitlesOfParts><vt:vector size="3" baseType="lpstr"><vt:lpstr>图片明细</vt:lpstr><vt:lpstr>图片级汇总</vt:lpstr><vt:lpstr>标注框汇总</vt:lpstr></vt:vector></TitlesOfParts>
        </Properties>
        """;

    private enum CellKind { Text, Number, Formula }
    private sealed record CellData(string Column, CellKind Kind, string Value, uint Style,
        string? CachedValue = null);
    private sealed record RowData(uint Index, IReadOnlyList<CellData> Cells);
    private sealed record CategoryCount(string Category, int Count);
}
