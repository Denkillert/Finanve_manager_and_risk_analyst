using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace FinanveManager.Data.Services;

/// <summary>
/// Минимальный генератор XLSX (Office Open XML) без внешних NuGet-пакетов.
/// Поддерживает несколько листов, inline-строки, числа, даты, заголовки,
/// итоговые строки, ширины колонок и закрепление шапки.
/// </summary>
public sealed class XlsxWorkbook
{
    private readonly List<XlsxSheet> _sheets = new();

    public XlsxSheet AddSheet(string name)
    {
        var sheet = new XlsxSheet(SanitizeName(name, _sheets.Count + 1));
        _sheets.Add(sheet);
        return sheet;
    }

    public byte[] Build()
    {
        if (_sheets.Count == 0)
            AddSheet("Лист1");

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteText(zip, "[Content_Types].xml", BuildContentTypes());
            WriteText(zip, "_rels/.rels", BuildRootRels());
            WriteText(zip, "docProps/core.xml", BuildCoreProps());
            WriteText(zip, "docProps/app.xml", BuildAppProps());
            WriteText(zip, "xl/workbook.xml", BuildWorkbook());
            WriteText(zip, "xl/_rels/workbook.xml.rels", BuildWorkbookRels());
            WriteText(zip, "xl/styles.xml", BuildStyles());

            for (int i = 0; i < _sheets.Count; i++)
                WriteText(zip, $"xl/worksheets/sheet{i + 1}.xml", BuildSheet(_sheets[i]));
        }

        return ms.ToArray();
    }

    private static void WriteText(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string SanitizeName(string name, int index)
    {
        if (string.IsNullOrWhiteSpace(name))
            name = $"Лист{index}";
        foreach (char c in new[] { ':', '\\', '/', '?', '*', '[', ']' })
            name = name.Replace(c, ' ');
        name = name.Trim();
        return name.Length > 31 ? name[..31] : name;
    }

    private string BuildContentTypes()
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
        sb.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
        sb.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
        sb.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
        sb.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
        sb.Append("<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>");
        sb.Append("<Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/>");
        for (int i = 0; i < _sheets.Count; i++)
            sb.Append($"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        sb.Append("</Types>");
        return sb.ToString();
    }

    private static string BuildRootRels() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
        "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>" +
        "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties\" Target=\"docProps/app.xml\"/>" +
        "</Relationships>";

    private static string BuildCoreProps()
    {
        string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
               "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" " +
               "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" " +
               "xmlns:dcmitype=\"http://purl.org/dc/dcmitype/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
               "<dc:creator>ОАО «МАПИД»</dc:creator>" +
               "<cp:lastModifiedBy>ОАО «МАПИД»</cp:lastModifiedBy>" +
               $"<dcterms:created xsi:type=\"dcterms:W3CDTF\">{now}</dcterms:created>" +
               $"<dcterms:modified xsi:type=\"dcterms:W3CDTF\">{now}</dcterms:modified>" +
               "</cp:coreProperties>";
    }

    private static string BuildAppProps() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\" " +
        "xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\">" +
        "<Application>FinanveManager</Application><DocSecurity>0</DocSecurity><ScaleCrop>false</ScaleCrop>" +
        "<Company>ОАО «МАПИД»</Company><AppVersion>1.0</AppVersion></Properties>";

    private string BuildWorkbook()
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" ");
        sb.Append("xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
        sb.Append("<bookViews><workbookView activeTab=\"0\"/></bookViews><sheets>");
        for (int i = 0; i < _sheets.Count; i++)
            sb.Append($"<sheet name=\"{Escape(_sheets[i].Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
        sb.Append("</sheets></workbook>");
        return sb.ToString();
    }

    private string BuildWorkbookRels()
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        for (int i = 0; i < _sheets.Count; i++)
            sb.Append($"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>");
        sb.Append($"<Relationship Id=\"rId{_sheets.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
        sb.Append("</Relationships>");
        return sb.ToString();
    }

    private static string BuildStyles() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <numFmts count="3">
            <numFmt numFmtId="164" formatCode="#,##0.00"/>
            <numFmt numFmtId="165" formatCode="dd.mm.yyyy"/>
            <numFmt numFmtId="166" formatCode="#,##0.0000"/>
          </numFmts>
          <fonts count="5">
            <font><sz val="10"/><name val="Calibri"/><family val="2"/></font>
            <font><b/><sz val="10"/><color rgb="FFFFFFFF"/><name val="Calibri"/><family val="2"/></font>
            <font><b/><sz val="14"/><color rgb="FF0E2A28"/><name val="Calibri"/><family val="2"/></font>
            <font><b/><sz val="10"/><color rgb="FF10201E"/><name val="Calibri"/><family val="2"/></font>
            <font><sz val="9"/><color rgb="FF64706D"/><name val="Calibri"/><family val="2"/></font>
          </fonts>
          <fills count="5">
            <fill><patternFill patternType="none"/></fill>
            <fill><patternFill patternType="gray125"/></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FF0E7C66"/><bgColor indexed="64"/></patternFill></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FFE7F1EE"/><bgColor indexed="64"/></patternFill></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FFFDF3E2"/><bgColor indexed="64"/></patternFill></fill>
          </fills>
          <borders count="3">
            <border><left/><right/><top/><bottom/><diagonal/></border>
            <border>
              <left style="thin"><color rgb="FFD5DDD9"/></left>
              <right style="thin"><color rgb="FFD5DDD9"/></right>
              <top style="thin"><color rgb="FFD5DDD9"/></top>
              <bottom style="thin"><color rgb="FFD5DDD9"/></bottom>
              <diagonal/>
            </border>
            <border>
              <left/><right/>
              <top style="thin"><color rgb="FF0E2A28"/></top>
              <bottom style="double"><color rgb="FF0E2A28"/></bottom>
              <diagonal/>
            </border>
          </borders>
          <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
          <cellXfs count="9">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
            <xf numFmtId="0" fontId="1" fillId="2" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1">
              <alignment horizontal="center" vertical="center" wrapText="1"/>
            </xf>
            <xf numFmtId="0" fontId="2" fillId="0" borderId="0" xfId="0" applyFont="1"/>
            <xf numFmtId="164" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyBorder="1"/>
            <xf numFmtId="165" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyBorder="1"/>
            <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1" applyAlignment="1">
              <alignment wrapText="1" vertical="top"/>
            </xf>
            <xf numFmtId="0" fontId="3" fillId="3" borderId="2" xfId="0" applyFont="1" applyFill="1" applyBorder="1"/>
            <xf numFmtId="164" fontId="3" fillId="3" borderId="2" xfId="0" applyNumberFormat="1" applyFont="1" applyFill="1" applyBorder="1"/>
            <xf numFmtId="1" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyBorder="1"/>
          </cellXfs>
          <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
        </styleSheet>
        """;

    private static string BuildSheet(XlsxSheet sheet)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" ");
        sb.Append("xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");

        sb.Append("<sheetViews><sheetView workbookViewId=\"0\"");
        if (sheet.FrozenRows > 0)
            sb.Append("><pane ySplit=\"").Append(sheet.FrozenRows)
              .Append("\" topLeftCell=\"A").Append(sheet.FrozenRows + 1)
              .Append("\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView>");
        else
            sb.Append("/>");
        sb.Append("</sheetViews>");

        sb.Append("<sheetFormatPr defaultRowHeight=\"15\"/>");

        if (sheet.ColumnWidths.Count > 0)
        {
            sb.Append("<cols>");
            for (int i = 0; i < sheet.ColumnWidths.Count; i++)
            {
                double width = Math.Clamp(sheet.ColumnWidths[i], 6, 80);
                sb.Append($"<col min=\"{i + 1}\" max=\"{i + 1}\" width=\"{width.ToString(CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
            }
            sb.Append("</cols>");
        }

        sb.Append("<sheetData>");
        for (int rowIndex = 0; rowIndex < sheet.Rows.Count; rowIndex++)
        {
            var row = sheet.Rows[rowIndex];
            int r = rowIndex + 1;
            sb.Append($"<row r=\"{r}\"");
            if (row.Height.HasValue)
                sb.Append($" ht=\"{row.Height.Value.ToString(CultureInfo.InvariantCulture)}\" customHeight=\"1\"");
            sb.Append('>');

            for (int colIndex = 0; colIndex < row.Cells.Count; colIndex++)
            {
                var cell = row.Cells[colIndex];
                if (cell is null || cell.Value is null || cell.Value is string s && s.Length == 0)
                    continue;

                string reference = ColumnLetter(colIndex) + r;
                sb.Append($"<c r=\"{reference}\" s=\"{(int)cell.Style}\"");
                switch (cell.Value)
                {
                    case DateTime dt:
                        sb.Append($" t=\"n\"><v>{dt.ToOADate().ToString("R", CultureInfo.InvariantCulture)}</v></c>");
                        break;
                    case decimal dec:
                        sb.Append($" t=\"n\"><v>{dec.ToString(CultureInfo.InvariantCulture)}</v></c>");
                        break;
                    case double dbl:
                        sb.Append($" t=\"n\"><v>{dbl.ToString("R", CultureInfo.InvariantCulture)}</v></c>");
                        break;
                    case int or long or short:
                        sb.Append($" t=\"n\"><v>{Convert.ToString(cell.Value, CultureInfo.InvariantCulture)}</v></c>");
                        break;
                    case bool flag:
                        sb.Append($" t=\"inlineStr\"><is><t>{Escape(flag ? "Да" : "Нет")}</t></is></c>");
                        break;
                    default:
                        sb.Append($" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Escape(cell.Value.ToString() ?? string.Empty)}</t></is></c>");
                        break;
                }
            }
            sb.Append("</row>");
        }
        sb.Append("</sheetData>");

        sb.Append("<pageMargins left=\"0.4\" right=\"0.4\" top=\"0.5\" bottom=\"0.5\" header=\"0.3\" footer=\"0.3\"/>");
        sb.Append("</worksheet>");
        return sb.ToString();
    }

    internal static string ColumnLetter(int index)
    {
        var sb = new StringBuilder();
        int value = index + 1;
        while (value > 0)
        {
            int remainder = (value - 1) % 26;
            sb.Insert(0, (char)('A' + remainder));
            value = (value - 1) / 26;
        }
        return sb.ToString();
    }

    internal static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
             .Replace("\"", "&quot;").Replace("'", "&apos;");
}

public enum XlsxStyle
{
    Default = 0,
    Header = 1,
    Title = 2,
    Money = 3,
    Date = 4,
    Text = 5,
    TotalText = 6,
    TotalMoney = 7,
    Integer = 8
}

public sealed class XlsxCell
{
    public object? Value { get; }
    public XlsxStyle Style { get; }

    public XlsxCell(object? value, XlsxStyle style = XlsxStyle.Text)
    {
        Value = value;
        Style = style;
    }
}

public sealed class XlsxRow
{
    public List<XlsxCell?> Cells { get; } = new();
    public double? Height { get; set; }
}

public sealed class XlsxSheet
{
    public string Name { get; }
    internal List<XlsxRow> Rows { get; } = new();
    internal List<double> ColumnWidths { get; } = new();
    public int FrozenRows { get; private set; }

    internal XlsxSheet(string name) => Name = name;

    public int RowCount => Rows.Count;

    public XlsxSheet SetWidths(params double[] widths)
    {
        ColumnWidths.Clear();
        ColumnWidths.AddRange(widths);
        return this;
    }

    public XlsxSheet FreezeRows(int count)
    {
        FrozenRows = Math.Max(0, count);
        return this;
    }

    public XlsxSheet AddTitle(string text, int columnSpan = 0)
    {
        var row = new XlsxRow { Height = 22 };
        row.Cells.Add(new XlsxCell(text, XlsxStyle.Title));
        for (int i = 1; i < Math.Max(1, columnSpan); i++)
            row.Cells.Add(new XlsxCell(null, XlsxStyle.Default));
        Rows.Add(row);
        return this;
    }

    public XlsxSheet AddSubtitle(string text)
    {
        Rows.Add(new XlsxRow { Cells = { new XlsxCell(text, XlsxStyle.Default) } });
        return this;
    }

    public XlsxSheet AddBlank()
    {
        Rows.Add(new XlsxRow());
        return this;
    }

    public XlsxSheet AddHeader(params object?[] cells)
    {
        var row = new XlsxRow { Height = 30 };
        foreach (var cell in cells)
            row.Cells.Add(new XlsxCell(cell, XlsxStyle.Header));
        Rows.Add(row);
        return this;
    }

    public XlsxSheet AddRow(params object?[] cells)
    {
        var row = new XlsxRow();
        foreach (var cell in cells)
            row.Cells.Add(new XlsxCell(cell, InferStyle(cell, false)));
        Rows.Add(row);
        return this;
    }

    public XlsxSheet AddTotalRow(params object?[] cells)
    {
        var row = new XlsxRow { Height = 20 };
        foreach (var cell in cells)
            row.Cells.Add(new XlsxCell(cell, cell is decimal or double or int
                ? XlsxStyle.TotalMoney
                : XlsxStyle.TotalText));
        Rows.Add(row);
        return this;
    }

    private static XlsxStyle InferStyle(object? value, bool total) => value switch
    {
        null => XlsxStyle.Text,
        DateTime => XlsxStyle.Date,
        decimal or double => total ? XlsxStyle.TotalMoney : XlsxStyle.Money,
        int or long or short => total ? XlsxStyle.TotalMoney : XlsxStyle.Integer,
        _ => total ? XlsxStyle.TotalText : XlsxStyle.Text
    };
}
