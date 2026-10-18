using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using ClosedXML.Excel.Drawings;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using ShmsBackend.Api.Helpers;
using ShmsBackend.Api.Models.Reports;
using ShmsBackend.Data.Models.Entities.Portal;
using A = DocumentFormat.OpenXml.Drawing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using WP = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using WordParagraph = DocumentFormat.OpenXml.Wordprocessing.Paragraph;
using WordTable = DocumentFormat.OpenXml.Wordprocessing.Table;
using WordColor = DocumentFormat.OpenXml.Wordprocessing.Color;
using WordDocument = DocumentFormat.OpenXml.Wordprocessing.Document;
using MigraDocDocument = MigraDoc.DocumentObjectModel.Document;
using WordShading = DocumentFormat.OpenXml.Wordprocessing.Shading;
using WordText = DocumentFormat.OpenXml.Wordprocessing.Text;

namespace ShmsBackend.Api.Services.Reports;

/// <summary>
/// Renders a ReportData + CompanySettings pair into a letterhead-styled PDF, Excel, or Word file.
/// All three formats share the same visual contract: logo + company block, title/generated-at/filter
/// summary, a data table, and (PDF/Word) a page-numbered footer.
/// </summary>
public class ReportRenderer : IReportRenderer
{
    static ReportRenderer()
    {
        // PdfSharp/MigraDoc 6.x require an explicit font resolver even on Windows (no more implicit
        // GDI+ access) — must be set before the first RenderDocument() call, so a static constructor
        // (runs once per process, before any instance is used) is the right place, not per-request DI setup.
        if (PdfSharp.Fonts.GlobalFontSettings.FontResolver == null)
            PdfSharp.Fonts.GlobalFontSettings.FontResolver = new ReportFontResolver();
    }

    // ── Shared value formatting — used identically across all three renderers so a given cell reads
    // the same regardless of export format. ──
    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "",
            DateTime dt => dt.Year < 1900 ? "" : dt.ToString("d MMM yyyy"),   // DateTime.MinValue = "not set"
            decimal dec => dec.ToString("N2"),
            double dbl => dbl.ToString("N2"),
            bool b => b ? "Yes" : "No",
            _ => value.ToString() ?? ""
        };
    }

    /// <summary>One-line summary shown above the table: "12 records | Amount: 1,000.00 | Paid: 900.00".</summary>
    private static string? TotalsSummaryLine(ReportData data)
    {
        var totals = data.Totals;
        if (totals.Count == 0) return null;
        var parts = new System.Collections.Generic.List<string>
        {
            $"{data.RecordCount} record{(data.RecordCount == 1 ? "" : "s")}"
        };
        foreach (var col in data.Columns.Where(c => c.SumTotal))
            parts.Add($"Total {col.Header}: {totals[col.Key]:N2}");
        return string.Join("   |   ", parts);
    }

    private static string TotalCellText(ReportData data, int index, System.Collections.Generic.IReadOnlyDictionary<string, decimal> totals)
    {
        var col = data.Columns[index];
        if (col.SumTotal && totals.TryGetValue(col.Key, out var t)) return t.ToString("N2");
        if (index == 0) return $"TOTAL ({data.RecordCount})";
        return "";
    }

    private static string? CellText(System.Collections.Generic.Dictionary<string, object?> row, string key)
    {
        return row.TryGetValue(key, out var v) ? FormatValue(v) : "";
    }

    // ── Column-width weighting — shared by the PDF and Word renderers so a wide table lays out
    // identically in both. Wide-report threshold and per-column weight are both header-driven so a
    // report builder never has to declare its own layout hints. ──
    private const int WideReportColumnThreshold = 5;

    private static readonly string[] LongHeaderKeywords = { "email", "name", "flat", "unit", "address" };
    private static readonly string[] ShortHeaderKeywords = { "status", "cycle", "amount", "rent", "deposit", "balance", "type", "method" };

    private static double ColumnWeight(string header)
    {
        var h = header.ToLowerInvariant();
        if (LongHeaderKeywords.Any(k => h.Contains(k))) return 2.0;
        if (ShortHeaderKeywords.Any(k => h.Contains(k))) return 1.0;
        return 1.5;
    }

    private static System.Collections.Generic.List<double> DistributeColumnWidths(
        System.Collections.Generic.IReadOnlyList<ReportColumn> columns, double availableWidth,
        System.Collections.Generic.IReadOnlyList<double>? minWidths = null)
    {
        var weights = columns.Select(c => ColumnWeight(c.Header)).ToList();
        var totalWeight = weights.Sum();
        var widths = weights.Select(w => availableWidth * w / totalWeight).ToList();
        if (minWidths == null) return widths;

        // Money columns must never wrap or clip when figures grow (millions / billions): give each one
        // at least the width its widest formatted value (or the total) needs, and take the extra
        // space proportionally from the text columns.
        var lacking = 0.0;
        var elastic = 0.0;
        for (var i = 0; i < widths.Count; i++)
        {
            if (minWidths[i] > widths[i]) { lacking += minWidths[i] - widths[i]; widths[i] = minWidths[i]; }
            else if (minWidths[i] <= 0) elastic += widths[i];
        }
        if (lacking > 0 && elastic > 0)
        {
            var factor = Math.Max(0.35, (elastic - lacking) / elastic);
            for (var i = 0; i < widths.Count; i++)
                if (minWidths[i] <= 0) widths[i] *= factor;
            var sum = widths.Sum();
            if (sum > availableWidth) { var f = availableWidth / sum; for (var i = 0; i < widths.Count; i++) widths[i] *= f; }
        }
        return widths;
    }

    /// <summary>Minimum width per column (same unit as <paramref name="perChar"/>): only money columns, sized to the widest formatted value or total.</summary>
    private static System.Collections.Generic.List<double> NumericMinWidths(ReportData data, double perChar, double padding)
    {
        var totals = data.Totals;
        var result = new System.Collections.Generic.List<double>();
        foreach (var col in data.Columns)
        {
            if (!col.SumTotal) { result.Add(0); continue; }
            var maxLen = col.Header.Split(' ').Max(w => w.Length);   // header may wrap on spaces, not within a word
            if (totals.TryGetValue(col.Key, out var t)) maxLen = Math.Max(maxLen, t.ToString("N2").Length);
            foreach (var row in data.Rows)
                maxLen = Math.Max(maxLen, (CellText(row, col.Key) ?? "").Length);
            result.Add(maxLen * perChar + padding);
        }
        return result;
    }

    // ═══════════════════════════════════════════════════════════════════
    // PDF — PdfSharp + MigraDoc
    // ═══════════════════════════════════════════════════════════════════
    public async Task<byte[]> RenderPdfAsync(ReportData data, CompanySettings company)
    {
        var logo = await PrivateFileStorageHelper.ReadPrivateFileAsync(company.LogoPath);
        string? tempLogoPath = null;

        try
        {
            if (logo != null)
            {
                var ext = logo.Value.ContentType switch
                {
                    "image/png" => ".png",
                    "image/jpeg" => ".jpg",
                    "image/webp" => ".webp",
                    _ => ".png"
                };
                tempLogoPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{ext}");
                await File.WriteAllBytesAsync(tempLogoPath, logo.Value.Bytes);
            }

            var document = new MigraDocDocument();
            document.Info.Title = data.Title;

            var isWideReport = data.Columns.Count > WideReportColumnThreshold;

            var section = document.AddSection();
            section.PageSetup = document.DefaultPageSetup.Clone();
            section.PageSetup.LeftMargin = Unit.FromCentimeter(1.5);
            section.PageSetup.RightMargin = Unit.FromCentimeter(1.5);
            section.PageSetup.TopMargin = Unit.FromCentimeter(1.2);
            section.PageSetup.BottomMargin = Unit.FromCentimeter(1.5);

            if (isWideReport)
            {
                // MigraDoc/PdfSharp do NOT swap PageWidth/PageHeight when Orientation is set — confirmed
                // by direct render-time test (setting only Orientation left the rendered PDF page
                // portrait-shaped). Both must be set explicitly for the page to actually turn landscape.
                section.PageSetup.Orientation = Orientation.Landscape;
                var portraitWidth = section.PageSetup.PageWidth;
                var portraitHeight = section.PageSetup.PageHeight;
                section.PageSetup.PageWidth = portraitHeight;
                section.PageSetup.PageHeight = portraitWidth;
            }

            // ── Letterhead: logo left, company info stacked right ──
            var headerTable = section.AddTable();
            headerTable.Borders.Visible = false;
            var letterheadWidth = section.PageSetup.PageWidth - section.PageSetup.LeftMargin - section.PageSetup.RightMargin;
            headerTable.AddColumn(Unit.FromCentimeter(4));
            headerTable.AddColumn(letterheadWidth - Unit.FromCentimeter(4));
            var headerRow = headerTable.AddRow();

            if (tempLogoPath != null)
            {
                var image = headerRow.Cells[0].AddImage(tempLogoPath);
                image.Width = Unit.FromCentimeter(3);
                image.LockAspectRatio = true;
            }

            var infoCell = headerRow.Cells[1];
            infoCell.Format.Alignment = ParagraphAlignment.Right;
            if (!string.IsNullOrWhiteSpace(company.CompanyName))
            {
                var namePara = infoCell.AddParagraph(company.CompanyName);
                namePara.Format.Font.Bold = true;
                namePara.Format.Font.Size = Unit.FromPoint(16);
            }
            foreach (var line in new[] { company.Address, company.Email, company.Phone, company.Website, company.RegistrationNumber })
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    var p = infoCell.AddParagraph(line);
                    p.Format.Font.Size = Unit.FromPoint(9);
                }
            }

            section.AddParagraph();

            var titlePara = section.AddParagraph(data.Title);
            titlePara.Format.Font.Size = Unit.FromPoint(14);
            titlePara.Format.Font.Bold = true;
            titlePara.Format.SpaceAfter = Unit.FromPoint(2);

            var metaPara = section.AddParagraph($"Generated: {data.GeneratedAt:d MMM yyyy HH:mm} UTC");
            metaPara.Format.Font.Size = Unit.FromPoint(8);
            metaPara.Format.Font.Color = Colors.Gray;

            if (!string.IsNullOrWhiteSpace(data.FilterSummary))
            {
                var filterPara = section.AddParagraph(data.FilterSummary);
                filterPara.Format.Font.Size = Unit.FromPoint(8);
                filterPara.Format.Font.Color = Colors.Gray;
                filterPara.Format.SpaceAfter = Unit.FromPoint(8);
            }
            else
            {
                metaPara.Format.SpaceAfter = Unit.FromPoint(8);
            }

            var totalsLine = TotalsSummaryLine(data);
            if (totalsLine != null)
            {
                var totalsPara = section.AddParagraph(totalsLine);
                totalsPara.Format.Font.Size = Unit.FromPoint(10);
                totalsPara.Format.Font.Bold = true;
                totalsPara.Format.Shading.Color = Color.FromRgb(238, 238, 238);
                totalsPara.Format.Borders.Distance = Unit.FromPoint(4);
                totalsPara.Format.Borders.Width = 0.5;
                totalsPara.Format.Borders.Color = Colors.Gray;
                totalsPara.Format.SpaceBefore = Unit.FromPoint(4);
                totalsPara.Format.SpaceAfter = Unit.FromPoint(10);
            }

            // ── Data table — column widths are weighted by header content (see ColumnWeight) and
            // distributed across the actual available content width, not an assumed page size, so this
            // is correct in both portrait and the landscape switch above. MigraDoc wraps paragraph text
            // by default within a properly-widthed cell — nothing here disables that. ──
            var availableWidth = (section.PageSetup.PageWidth - section.PageSetup.LeftMargin - section.PageSetup.RightMargin).Point;
            var columnWidths = DistributeColumnWidths(data.Columns, availableWidth, NumericMinWidths(data, 5.6, 10));

            var table = section.AddTable();
            table.Borders.Width = 0.5;
            table.Borders.Color = Colors.LightGray;
            for (var i = 0; i < data.Columns.Count; i++)
                table.AddColumn(Unit.FromPoint(columnWidths[i]));

            var tableHeaderRow = table.AddRow();
            tableHeaderRow.Shading.Color = Colors.LightGray;
            tableHeaderRow.Format.Font.Bold = true;
            tableHeaderRow.Format.Font.Size = Unit.FromPoint(9);
            tableHeaderRow.HeadingFormat = true; // repeat header on every page of a long list
            for (var i = 0; i < data.Columns.Count; i++)
            {
                tableHeaderRow.Cells[i].AddParagraph(data.Columns[i].Header);
                if (data.Columns[i].SumTotal) tableHeaderRow.Cells[i].Format.Alignment = ParagraphAlignment.Right;
            }

            foreach (var row in data.Rows)
            {
                var dataRow = table.AddRow();
                dataRow.Format.Font.Size = Unit.FromPoint(8);
                for (var i = 0; i < data.Columns.Count; i++)
                {
                    dataRow.Cells[i].AddParagraph(CellText(row, data.Columns[i].Key) ?? "");
                    if (data.Columns[i].SumTotal) dataRow.Cells[i].Format.Alignment = ParagraphAlignment.Right;
                }
            }

            var pdfTotals = data.Totals;
            if (pdfTotals.Count > 0)
            {
                var totalRow = table.AddRow();
                totalRow.Format.Font.Size = Unit.FromPoint(9);
                totalRow.Format.Font.Bold = true;
                totalRow.Shading.Color = Colors.LightGray;
                totalRow.Borders.Top.Width = 1.25;
                totalRow.Borders.Top.Color = Colors.Black;
                for (var i = 0; i < data.Columns.Count; i++)
                {
                    totalRow.Cells[i].AddParagraph(TotalCellText(data, i, pdfTotals));
                    if (data.Columns[i].SumTotal) totalRow.Cells[i].Format.Alignment = ParagraphAlignment.Right;
                }
            }

            // ── Footer: page number + generated-by ──
            var footerPara = section.Footers.Primary.AddParagraph();
            footerPara.Format.Alignment = ParagraphAlignment.Center;
            footerPara.Format.Font.Size = Unit.FromPoint(7);
            footerPara.Format.Font.Color = Colors.Gray;
            footerPara.AddText("Generated by SHMS — Page ");
            footerPara.AddPageField();
            footerPara.AddText(" of ");
            footerPara.AddNumPagesField();

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();

            using var ms = new MemoryStream();
            renderer.PdfDocument.Save(ms, false);
            return ms.ToArray();
        }
        finally
        {
            if (tempLogoPath != null && File.Exists(tempLogoPath))
            {
                try { File.Delete(tempLogoPath); } catch { /* best-effort cleanup */ }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Excel — ClosedXML
    // ═══════════════════════════════════════════════════════════════════
    public async Task<byte[]> RenderExcelAsync(ReportData data, CompanySettings company)
    {
        var logo = await PrivateFileStorageHelper.ReadPrivateFileAsync(company.LogoPath);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Report");
        var colCount = Math.Max(1, data.Columns.Count);

        var row = 1;

        if (logo != null)
        {
            var format = logo.Value.ContentType == "image/jpeg" ? XLPictureFormat.Jpeg : XLPictureFormat.Png;
            using var imgStream = new MemoryStream(logo.Value.Bytes);
            ws.AddPicture(imgStream, format)
                .MoveTo(ws.Cell(row, 1), 2, 2)
                .WithSize(140, 70);
            row += 4;
        }

        if (!string.IsNullOrWhiteSpace(company.CompanyName))
        {
            var r = ws.Range(row, 1, row, colCount).Merge();
            r.Value = company.CompanyName;
            r.Style.Font.Bold = true;
            r.Style.Font.FontSize = 14;
            row++;
        }
        foreach (var line in new[] { company.Address, company.Email, company.Phone, company.Website, company.RegistrationNumber })
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                var r = ws.Range(row, 1, row, colCount).Merge();
                r.Value = line;
                r.Style.Font.FontSize = 9;
                row++;
            }
        }

        row++;

        var titleRange = ws.Range(row, 1, row, colCount).Merge();
        titleRange.Value = data.Title;
        titleRange.Style.Font.Bold = true;
        titleRange.Style.Font.FontSize = 12;
        row++;

        var metaText = $"Generated: {data.GeneratedAt:d MMM yyyy HH:mm} UTC";
        if (!string.IsNullOrWhiteSpace(data.FilterSummary)) metaText += "    " + data.FilterSummary;
        var metaRange = ws.Range(row, 1, row, colCount).Merge();
        metaRange.Value = metaText;
        metaRange.Style.Font.FontSize = 8;
        metaRange.Style.Font.FontColor = XLColor.Gray;
        row++;

        var excelTotalsLine = TotalsSummaryLine(data);
        if (excelTotalsLine != null)
        {
            var sumRange = ws.Range(row, 1, row, colCount).Merge();
            sumRange.Value = excelTotalsLine;
            sumRange.Style.Font.Bold = true;
            sumRange.Style.Font.FontSize = 10;
            row++;
        }
        row++;

        var headerRowIndex = row;
        for (var i = 0; i < data.Columns.Count; i++)
        {
            var cell = ws.Cell(headerRowIndex, i + 1);
            cell.Value = data.Columns[i].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            if (data.Columns[i].SumTotal) cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        }
        row++;

        foreach (var dataRow in data.Rows)
        {
            for (var i = 0; i < data.Columns.Count; i++)
            {
                var dc = ws.Cell(row, i + 1);
                var dcol = data.Columns[i];
                if (dcol.SumTotal && dataRow.TryGetValue(dcol.Key, out var raw) && raw != null
                    && decimal.TryParse(Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture),
                        System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var num))
                {
                    dc.Value = num;                       // real number so Excel can sum/filter it
                    dc.Style.NumberFormat.Format = "#,##0.00";
                    dc.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                }
                else
                {
                    dc.Value = CellText(dataRow, dcol.Key);
                }
            }
            row++;
        }

        var excelTotals = data.Totals;
        if (excelTotals.Count > 0)
        {
            for (var i = 0; i < data.Columns.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                var col = data.Columns[i];
                if (col.SumTotal && excelTotals.TryGetValue(col.Key, out var tv))
                {
                    cell.Value = tv;
                    cell.Style.NumberFormat.Format = "#,##0.00";
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                }
                else if (i == 0)
                {
                    cell.Value = $"TOTAL ({data.RecordCount})";
                }
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.LightGray;
                cell.Style.Border.TopBorder = XLBorderStyleValues.Thin;
            }
            row++;
        }

        // Confirmed: this runs after every data row is written above, not before — auto-fit sizes
        // against the actual populated sheet, not an empty one.
        ws.Columns(1, colCount).AdjustToContents();

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    // ═══════════════════════════════════════════════════════════════════
    // Word — DocumentFormat.OpenXml
    // ═══════════════════════════════════════════════════════════════════
    public async Task<byte[]> RenderWordAsync(ReportData data, CompanySettings company)
    {
        var logo = await PrivateFileStorageHelper.ReadPrivateFileAsync(company.LogoPath);

        using var ms = new MemoryStream();
        using (var wordDoc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var mainPart = wordDoc.AddMainDocumentPart();
            mainPart.Document = new WordDocument();
            var body = mainPart.Document.AppendChild(new Body());

            if (logo != null)
            {
                var imagePartType = logo.Value.ContentType == "image/jpeg" ? ImagePartType.Jpeg : ImagePartType.Png;
                var imagePart = mainPart.AddImagePart(imagePartType);
                using (var imgStream = new MemoryStream(logo.Value.Bytes))
                {
                    imagePart.FeedData(imgStream);
                }
                var relationshipId = mainPart.GetIdOfPart(imagePart);
                body.AppendChild(BuildImageParagraph(relationshipId, 1200000, 600000));
            }

            if (!string.IsNullOrWhiteSpace(company.CompanyName))
                body.AppendChild(BuildParagraph(company.CompanyName, bold: true, sizeHalfPoints: "32"));
            foreach (var line in new[] { company.Address, company.Email, company.Phone, company.Website, company.RegistrationNumber })
            {
                if (!string.IsNullOrWhiteSpace(line))
                    body.AppendChild(BuildParagraph(line, bold: false, sizeHalfPoints: "18"));
            }

            body.AppendChild(new WordParagraph());

            body.AppendChild(BuildParagraph(data.Title, bold: true, sizeHalfPoints: "28"));
            body.AppendChild(BuildParagraph($"Generated: {data.GeneratedAt:d MMM yyyy HH:mm} UTC", bold: false, sizeHalfPoints: "16", gray: true));
            if (!string.IsNullOrWhiteSpace(data.FilterSummary))
                body.AppendChild(BuildParagraph(data.FilterSummary, bold: false, sizeHalfPoints: "16", gray: true));

            var wordTotalsLine = TotalsSummaryLine(data);
            if (wordTotalsLine != null)
                body.AppendChild(BuildParagraph(wordTotalsLine, bold: true, sizeHalfPoints: "18"));

            body.AppendChild(new WordParagraph());

            // A4 in dxa (1/20 pt): 11906 x 16838 portrait. Word does not auto-swap these when Orient is
            // set (same underlying issue as MigraDoc) — both are swapped explicitly here for a wide report.
            const int a4WidthDxa = 11906;
            const int a4HeightDxa = 16838;
            const int leftMarginDxa = 850;   // ≈ 1.5cm, matching the PDF renderer's margins
            const int rightMarginDxa = 850;
            const int topMarginDxa = 680;    // ≈ 1.2cm
            const int bottomMarginDxa = 850;

            var isWideReport = data.Columns.Count > WideReportColumnThreshold;
            var pageWidthDxa = isWideReport ? a4HeightDxa : a4WidthDxa;
            var pageHeightDxa = isWideReport ? a4WidthDxa : a4HeightDxa;
            var availableWidthDxa = pageWidthDxa - leftMarginDxa - rightMarginDxa;
            var columnWidthsDxa = DistributeColumnWidths(data.Columns, availableWidthDxa, NumericMinWidths(data, 5.6 * 20, 10 * 20))
                .Select(w => (int)Math.Round(w))
                .ToList();

            var table = new WordTable();
            var tableProps = new TableProperties(
                new TableBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 4 },
                    new BottomBorder { Val = BorderValues.Single, Size = 4 },
                    new LeftBorder { Val = BorderValues.Single, Size = 4 },
                    new RightBorder { Val = BorderValues.Single, Size = 4 },
                    new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                    new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 }
                ),
                // Fixed layout + an explicit Dxa table width are both required for Word to actually
                // honor the per-column widths below instead of silently auto-fitting to content.
                new TableLayout { Type = TableLayoutValues.Fixed },
                new TableWidth { Type = TableWidthUnitValues.Dxa, Width = availableWidthDxa.ToString() }
            );
            table.AppendChild(tableProps);
            table.AppendChild(new TableGrid(columnWidthsDxa.Select(w => new GridColumn { Width = w.ToString() })));

            var headerRow = new TableRow(new TableRowProperties(new TableHeader())); // repeats on each page
            for (var i = 0; i < data.Columns.Count; i++)
            {
                var cell = new TableCell(
                    new TableCellProperties(
                        new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = columnWidthsDxa[i].ToString() },
                        new WordShading { Fill = "D9D9D9" }),
                    BuildParagraph(data.Columns[i].Header, bold: true, sizeHalfPoints: "18", right: data.Columns[i].SumTotal));
                headerRow.Append(cell);
            }
            table.Append(headerRow);

            foreach (var dataRow in data.Rows)
            {
                var tr = new TableRow();
                for (var i = 0; i < data.Columns.Count; i++)
                {
                    tr.Append(new TableCell(
                        new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = columnWidthsDxa[i].ToString() }),
                        BuildParagraph(CellText(dataRow, data.Columns[i].Key) ?? "", bold: false, sizeHalfPoints: "16", right: data.Columns[i].SumTotal)));
                }
                table.Append(tr);
            }

            var wordTotals = data.Totals;
            if (wordTotals.Count > 0)
            {
                var totalTr = new TableRow();
                for (var i = 0; i < data.Columns.Count; i++)
                {
                    totalTr.Append(new TableCell(
                        new TableCellProperties(
                            new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = columnWidthsDxa[i].ToString() },
                            new WordShading { Fill = "D9D9D9" }),
                        BuildParagraph(TotalCellText(data, i, wordTotals), bold: true, sizeHalfPoints: "18", right: data.Columns[i].SumTotal)));
                }
                table.Append(totalTr);
            }

            body.AppendChild(table);
            body.AppendChild(new WordParagraph());

            // ── Footer: page number + generated-by ──
            var footerPart = mainPart.AddNewPart<FooterPart>();
            var footer = new Footer();
            var footerPara = new WordParagraph(new ParagraphProperties(new Justification { Val = JustificationValues.Center }));

            footerPara.Append(new Run(
                new RunProperties(new FontSize { Val = "14" }, new WordColor { Val = "808080" }),
                new WordText("Generated by SHMS — Page ") { Space = SpaceProcessingModeValues.Preserve }));
            AppendFieldRuns(footerPara, "PAGE", "14");
            footerPara.Append(new Run(
                new RunProperties(new FontSize { Val = "14" }, new WordColor { Val = "808080" }),
                new WordText(" of ") { Space = SpaceProcessingModeValues.Preserve }));
            AppendFieldRuns(footerPara, "NUMPAGES", "14");

            footer.Append(footerPara);
            footerPart.Footer = footer;

            var sectPr = new SectionProperties(
                new FooterReference { Type = HeaderFooterValues.Default, Id = mainPart.GetIdOfPart(footerPart) },
                new PageSize
                {
                    Width = (uint)pageWidthDxa,
                    Height = (uint)pageHeightDxa,
                    Orient = isWideReport ? PageOrientationValues.Landscape : PageOrientationValues.Portrait
                },
                new PageMargin
                {
                    Top = topMarginDxa,
                    Right = (uint)rightMarginDxa,
                    Bottom = bottomMarginDxa,
                    Left = (uint)leftMarginDxa
                });
            body.Append(sectPr);

            mainPart.Document.Save();
        }

        return ms.ToArray();
    }

    private static WordParagraph BuildParagraph(string text, bool bold, string sizeHalfPoints, bool gray = false, bool right = false)
    {
        var runProps = new RunProperties { FontSize = new FontSize { Val = sizeHalfPoints } };
        if (bold) runProps.Append(new Bold());
        if (gray) runProps.Append(new WordColor { Val = "808080" });
        var para = new WordParagraph(new Run(runProps, new WordText(text) { Space = SpaceProcessingModeValues.Preserve }));
        if (right) para.PrependChild(new ParagraphProperties(new Justification { Val = JustificationValues.Right }));
        return para;
    }

    /// <summary>
    /// A Word field (PAGE / NUMPAGES) is three separate runs sharing one paragraph — begin char,
    /// field-code instruction, end char — Word recalculates the displayed value at open/print time.
    /// </summary>
    private static void AppendFieldRuns(WordParagraph paragraph, string fieldCode, string sizeHalfPoints)
    {
        var props = () => new RunProperties(new FontSize { Val = sizeHalfPoints }, new WordColor { Val = "808080" });
        paragraph.Append(new Run(props(), new FieldChar { FieldCharType = FieldCharValues.Begin }));
        paragraph.Append(new Run(props(), new FieldCode($" {fieldCode} ") { Space = SpaceProcessingModeValues.Preserve }));
        paragraph.Append(new Run(props(), new FieldChar { FieldCharType = FieldCharValues.End }));
    }

    private static WordParagraph BuildImageParagraph(string relationshipId, long widthEmu, long heightEmu)
    {
        var element = new Drawing(
            new WP.Inline(
                new WP.Extent { Cx = widthEmu, Cy = heightEmu },
                new WP.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
                new WP.DocProperties { Id = 1U, Name = "Logo" },
                new WP.NonVisualGraphicFrameDrawingProperties(
                    new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = 0U, Name = "Logo.png" },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip { Embed = relationshipId },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0L, Y = 0L },
                                    new A.Extents { Cx = widthEmu, Cy = heightEmu }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })
                        )
                    ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }
                )
            ) { DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U }
        );

        return new WordParagraph(new Run(element));
    }
}
