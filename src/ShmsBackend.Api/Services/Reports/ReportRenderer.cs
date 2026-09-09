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
            DateTime dt => dt.ToString("d MMM yyyy"),
            decimal dec => dec.ToString("N2"),
            double dbl => dbl.ToString("N2"),
            bool b => b ? "Yes" : "No",
            _ => value.ToString() ?? ""
        };
    }

    private static string? CellText(System.Collections.Generic.Dictionary<string, object?> row, string key)
    {
        return row.TryGetValue(key, out var v) ? FormatValue(v) : "";
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

            var section = document.AddSection();
            section.PageSetup = document.DefaultPageSetup.Clone();
            section.PageSetup.LeftMargin = Unit.FromCentimeter(1.5);
            section.PageSetup.RightMargin = Unit.FromCentimeter(1.5);
            section.PageSetup.TopMargin = Unit.FromCentimeter(1.2);
            section.PageSetup.BottomMargin = Unit.FromCentimeter(1.5);

            // ── Letterhead: logo left, company info stacked right ──
            var headerTable = section.AddTable();
            headerTable.Borders.Visible = false;
            headerTable.AddColumn(Unit.FromCentimeter(4));
            headerTable.AddColumn(Unit.FromCentimeter(13));
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

            // ── Data table ──
            var table = section.AddTable();
            table.Borders.Width = 0.5;
            table.Borders.Color = Colors.LightGray;
            foreach (var _ in data.Columns) table.AddColumn(Unit.FromCentimeter(16.0 / Math.Max(1, data.Columns.Count)));

            var tableHeaderRow = table.AddRow();
            tableHeaderRow.Shading.Color = Colors.LightGray;
            tableHeaderRow.Format.Font.Bold = true;
            tableHeaderRow.Format.Font.Size = Unit.FromPoint(9);
            for (var i = 0; i < data.Columns.Count; i++)
                tableHeaderRow.Cells[i].AddParagraph(data.Columns[i].Header);

            foreach (var row in data.Rows)
            {
                var dataRow = table.AddRow();
                dataRow.Format.Font.Size = Unit.FromPoint(9);
                for (var i = 0; i < data.Columns.Count; i++)
                    dataRow.Cells[i].AddParagraph(CellText(row, data.Columns[i].Key) ?? "");
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
        row += 2;

        var headerRowIndex = row;
        for (var i = 0; i < data.Columns.Count; i++)
        {
            var cell = ws.Cell(headerRowIndex, i + 1);
            cell.Value = data.Columns[i].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        }
        row++;

        foreach (var dataRow in data.Rows)
        {
            for (var i = 0; i < data.Columns.Count; i++)
                ws.Cell(row, i + 1).Value = CellText(dataRow, data.Columns[i].Key);
            row++;
        }

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

            body.AppendChild(new WordParagraph());

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
                new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" }
            );
            table.AppendChild(tableProps);

            var headerRow = new TableRow();
            foreach (var col in data.Columns)
            {
                var cell = new TableCell(
                    new TableCellProperties(new WordShading { Fill = "D9D9D9" }),
                    BuildParagraph(col.Header, bold: true, sizeHalfPoints: "18"));
                headerRow.Append(cell);
            }
            table.Append(headerRow);

            foreach (var dataRow in data.Rows)
            {
                var tr = new TableRow();
                foreach (var col in data.Columns)
                    tr.Append(new TableCell(BuildParagraph(CellText(dataRow, col.Key) ?? "", bold: false, sizeHalfPoints: "18")));
                table.Append(tr);
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
                new FooterReference { Type = HeaderFooterValues.Default, Id = mainPart.GetIdOfPart(footerPart) });
            body.Append(sectPr);

            mainPart.Document.Save();
        }

        return ms.ToArray();
    }

    private static WordParagraph BuildParagraph(string text, bool bold, string sizeHalfPoints, bool gray = false)
    {
        var runProps = new RunProperties { FontSize = new FontSize { Val = sizeHalfPoints } };
        if (bold) runProps.Append(new Bold());
        if (gray) runProps.Append(new WordColor { Val = "808080" });
        return new WordParagraph(new Run(runProps, new WordText(text) { Space = SpaceProcessingModeValues.Preserve }));
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
