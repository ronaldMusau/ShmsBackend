using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MigraDoc.DocumentObjectModel;
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
using WordText = DocumentFormat.OpenXml.Wordprocessing.Text;

namespace ShmsBackend.Api.Services.Reports;

/// <summary>
/// Renders a single ReceiptData + CompanySettings pair into a letterhead-styled PDF, Excel, or Word
/// payment receipt — same letterhead visual style as ReportRenderer, but an invoice/receipt layout
/// (received-from/property blocks + a two-column key-value table) instead of a tabular report.
/// </summary>
public class ReceiptRenderer : IReceiptRenderer
{
    static ReceiptRenderer()
    {
        // Same reasoning as ReportRenderer's static constructor: PdfSharp/MigraDoc 6.x require an
        // explicit font resolver before the first RenderDocument() call. Guarded by the same null-check
        // so whichever renderer runs first in the process sets it — safe to set twice, never overwritten.
        if (PdfSharp.Fonts.GlobalFontSettings.FontResolver == null)
            PdfSharp.Fonts.GlobalFontSettings.FontResolver = new ReportFontResolver();
    }

    // ── Shared key-value row list — used identically by all three renderers so a given row reads the
    // same regardless of export format. Rent/Deposit/ServiceCharge/Credit only appear when set. ──
    private static List<(string Label, string Value)> BuildKeyValueRows(ReceiptData data)
    {
        var rows = new List<(string, string)>
        {
            ("Payment Type", data.PaymentType),
            ("Period", new DateTime(data.Year, data.Month, 1).ToString("MMMM yyyy"))
        };

        if (data.RentAmount.HasValue) rows.Add(("Rent Amount", data.RentAmount.Value.ToString("N2")));
        if (data.DepositAmount.HasValue) rows.Add(("Deposit Amount", data.DepositAmount.Value.ToString("N2")));
        if (data.ServiceChargeAmount.HasValue) rows.Add(("Service Charge Amount", data.ServiceChargeAmount.Value.ToString("N2")));
        if (data.CreditApplied.HasValue) rows.Add(("Credit Applied", data.CreditApplied.Value.ToString("N2")));

        rows.Add(("Amount Paid", data.AmountPaid.ToString("N2")));
        rows.Add(("Balance", data.Balance.ToString("N2")));
        rows.Add(("Payment Method", data.PaymentMethod ?? ""));
        rows.Add(("Mpesa Receipt Number", data.MpesaReceiptNumber ?? ""));
        rows.Add(("Status", data.Status));

        return rows;
    }

    private static string PaidAtText(ReceiptData data) =>
        data.PaidAt.HasValue ? data.PaidAt.Value.ToString("d MMM yyyy") : "—";

    // ═══════════════════════════════════════════════════════════════════
    // PDF — PdfSharp + MigraDoc
    // ═══════════════════════════════════════════════════════════════════
    public async Task<byte[]> RenderPdfAsync(ReceiptData data, CompanySettings company)
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
            document.Info.Title = $"Receipt {data.ReceiptNumber}";

            var section = document.AddSection();
            section.PageSetup = document.DefaultPageSetup.Clone();
            section.PageSetup.LeftMargin = Unit.FromCentimeter(1.5);
            section.PageSetup.RightMargin = Unit.FromCentimeter(1.5);
            section.PageSetup.TopMargin = Unit.FromCentimeter(1.2);
            section.PageSetup.BottomMargin = Unit.FromCentimeter(1.5);

            // ── Letterhead: logo left, company info stacked right — same visual style as ReportRenderer ──
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

            section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(6);

            // ── Title ──
            var titlePara = section.AddParagraph("PAYMENT RECEIPT");
            titlePara.Format.Alignment = ParagraphAlignment.Center;
            titlePara.Format.Font.Size = Unit.FromPoint(18);
            titlePara.Format.Font.Bold = true;
            titlePara.Format.SpaceAfter = Unit.FromPoint(2);

            var metaPara = section.AddParagraph($"Receipt No: {data.ReceiptNumber}    Paid: {PaidAtText(data)}");
            metaPara.Format.Alignment = ParagraphAlignment.Center;
            metaPara.Format.Font.Size = Unit.FromPoint(9);
            metaPara.Format.Font.Color = Colors.Gray;
            metaPara.Format.SpaceAfter = Unit.FromPoint(14);

            // ── Received From ──
            var receivedFromHeading = section.AddParagraph("Received From");
            receivedFromHeading.Format.Font.Bold = true;
            receivedFromHeading.Format.Font.Size = Unit.FromPoint(10);

            var tenantNamePara = section.AddParagraph(data.TenantName);
            tenantNamePara.Format.Font.Size = Unit.FromPoint(9);
            if (!string.IsNullOrWhiteSpace(data.TenantPhone))
                section.AddParagraph(data.TenantPhone).Format.Font.Size = Unit.FromPoint(9);
            if (!string.IsNullOrWhiteSpace(data.TenantEmail))
                section.AddParagraph(data.TenantEmail).Format.Font.Size = Unit.FromPoint(9);

            section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(6);

            // ── Property ──
            var propertyHeading = section.AddParagraph("Property");
            propertyHeading.Format.Font.Bold = true;
            propertyHeading.Format.Font.Size = Unit.FromPoint(10);
            section.AddParagraph(data.FlatName).Format.Font.Size = Unit.FromPoint(9);
            section.AddParagraph($"House {data.HouseNumber}").Format.Font.Size = Unit.FromPoint(9);

            section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(10);

            // ── Key-value table — label bold left, value right, NOT the multi-row tabular report layout ──
            var table = section.AddTable();
            table.Borders.Width = 0.5;
            table.Borders.Color = Colors.LightGray;
            table.AddColumn(Unit.FromCentimeter(7));
            table.AddColumn(Unit.FromCentimeter(8.5));

            foreach (var (label, value) in BuildKeyValueRows(data))
            {
                var tableRow = table.AddRow();
                tableRow.Format.Font.Size = Unit.FromPoint(9);

                var labelCell = tableRow.Cells[0];
                labelCell.AddParagraph(label).Format.Font.Bold = true;

                var valueCell = tableRow.Cells[1];
                valueCell.Format.Alignment = ParagraphAlignment.Right;
                valueCell.AddParagraph(value);
            }

            // ── Total Paid — visually distinct ──
            var totalPara = section.AddParagraph($"Total Paid: {data.AmountPaid:N2}");
            totalPara.Format.Alignment = ParagraphAlignment.Right;
            totalPara.Format.Font.Size = Unit.FromPoint(14);
            totalPara.Format.Font.Bold = true;
            totalPara.Format.SpaceBefore = Unit.FromPoint(10);

            // ── Footer ──
            var thankYouFooter = section.Footers.Primary.AddParagraph("Thank you for your payment");
            thankYouFooter.Format.Alignment = ParagraphAlignment.Center;
            thankYouFooter.Format.Font.Size = Unit.FromPoint(8);
            thankYouFooter.Format.Font.Color = Colors.Gray;

            var pageFooter = section.Footers.Primary.AddParagraph();
            pageFooter.Format.Alignment = ParagraphAlignment.Center;
            pageFooter.Format.Font.Size = Unit.FromPoint(7);
            pageFooter.Format.Font.Color = Colors.Gray;
            pageFooter.AddText("Generated by SHMS — Page ");
            pageFooter.AddPageField();
            pageFooter.AddText(" of ");
            pageFooter.AddNumPagesField();

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
    public async Task<byte[]> RenderExcelAsync(ReceiptData data, CompanySettings company)
    {
        var logo = await PrivateFileStorageHelper.ReadPrivateFileAsync(company.LogoPath);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Receipt");
        const int colCount = 2;

        var row = 1;

        if (logo != null)
        {
            var format = logo.Value.ContentType == "image/jpeg" ? ClosedXML.Excel.Drawings.XLPictureFormat.Jpeg : ClosedXML.Excel.Drawings.XLPictureFormat.Png;
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
        titleRange.Value = "PAYMENT RECEIPT";
        titleRange.Style.Font.Bold = true;
        titleRange.Style.Font.FontSize = 16;
        titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        row++;

        var metaRange = ws.Range(row, 1, row, colCount).Merge();
        metaRange.Value = $"Receipt No: {data.ReceiptNumber}    Paid: {PaidAtText(data)}";
        metaRange.Style.Font.FontSize = 9;
        metaRange.Style.Font.FontColor = XLColor.Gray;
        metaRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        row += 2;

        ws.Cell(row, 1).Value = "Received From";
        ws.Cell(row, 1).Style.Font.Bold = true;
        row++;
        ws.Cell(row, 1).Value = data.TenantName;
        row++;
        if (!string.IsNullOrWhiteSpace(data.TenantPhone)) { ws.Cell(row, 1).Value = data.TenantPhone; row++; }
        if (!string.IsNullOrWhiteSpace(data.TenantEmail)) { ws.Cell(row, 1).Value = data.TenantEmail; row++; }

        row++;
        ws.Cell(row, 1).Value = "Property";
        ws.Cell(row, 1).Style.Font.Bold = true;
        row++;
        ws.Cell(row, 1).Value = data.FlatName;
        row++;
        ws.Cell(row, 1).Value = $"House {data.HouseNumber}";
        row++;

        row++;

        foreach (var (label, value) in BuildKeyValueRows(data))
        {
            ws.Cell(row, 1).Value = label;
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 2).Value = value;
            ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            row++;
        }

        row++;
        ws.Cell(row, 1).Value = "Total Paid";
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Font.FontSize = 13;
        ws.Cell(row, 2).Value = data.AmountPaid.ToString("N2");
        ws.Cell(row, 2).Style.Font.Bold = true;
        ws.Cell(row, 2).Style.Font.FontSize = 13;
        ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

        ws.Columns(1, colCount).AdjustToContents();

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    // ═══════════════════════════════════════════════════════════════════
    // Word — DocumentFormat.OpenXml
    // ═══════════════════════════════════════════════════════════════════
    public async Task<byte[]> RenderWordAsync(ReceiptData data, CompanySettings company)
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

            body.AppendChild(BuildParagraph("PAYMENT RECEIPT", bold: true, sizeHalfPoints: "36", center: true));
            body.AppendChild(BuildParagraph(
                $"Receipt No: {data.ReceiptNumber}    Paid: {PaidAtText(data)}",
                bold: false, sizeHalfPoints: "18", gray: true, center: true));

            body.AppendChild(new WordParagraph());

            body.AppendChild(BuildParagraph("Received From", bold: true, sizeHalfPoints: "20"));
            body.AppendChild(BuildParagraph(data.TenantName, bold: false, sizeHalfPoints: "18"));
            if (!string.IsNullOrWhiteSpace(data.TenantPhone))
                body.AppendChild(BuildParagraph(data.TenantPhone, bold: false, sizeHalfPoints: "18"));
            if (!string.IsNullOrWhiteSpace(data.TenantEmail))
                body.AppendChild(BuildParagraph(data.TenantEmail, bold: false, sizeHalfPoints: "18"));

            body.AppendChild(new WordParagraph());

            body.AppendChild(BuildParagraph("Property", bold: true, sizeHalfPoints: "20"));
            body.AppendChild(BuildParagraph(data.FlatName, bold: false, sizeHalfPoints: "18"));
            body.AppendChild(BuildParagraph($"House {data.HouseNumber}", bold: false, sizeHalfPoints: "18"));

            body.AppendChild(new WordParagraph());

            // ── Key-value table — two fixed columns, NOT the dynamic multi-row report table ──
            const int labelWidthDxa = 4500;
            const int valueWidthDxa = 5000;
            const int tableWidthDxa = labelWidthDxa + valueWidthDxa;

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
                new TableLayout { Type = TableLayoutValues.Fixed },
                new TableWidth { Type = TableWidthUnitValues.Dxa, Width = tableWidthDxa.ToString() }
            );
            table.AppendChild(tableProps);
            table.AppendChild(new TableGrid(
                new GridColumn { Width = labelWidthDxa.ToString() },
                new GridColumn { Width = valueWidthDxa.ToString() }));

            foreach (var (label, value) in BuildKeyValueRows(data))
            {
                var tr = new TableRow();
                tr.Append(new TableCell(
                    new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = labelWidthDxa.ToString() }),
                    BuildParagraph(label, bold: true, sizeHalfPoints: "18")));
                tr.Append(new TableCell(
                    new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = valueWidthDxa.ToString() }),
                    BuildParagraph(value, bold: false, sizeHalfPoints: "18", rightAlign: true)));
                table.Append(tr);
            }

            body.AppendChild(table);
            body.AppendChild(new WordParagraph());

            body.AppendChild(BuildParagraph($"Total Paid: {data.AmountPaid:N2}", bold: true, sizeHalfPoints: "28", rightAlign: true));
            body.AppendChild(new WordParagraph());

            // ── Footer: thank-you line + page number ──
            var footerPart = mainPart.AddNewPart<FooterPart>();
            var footer = new Footer();

            var thankYouPara = new WordParagraph { ParagraphProperties = new ParagraphProperties(new Justification { Val = JustificationValues.Center }) };
            thankYouPara.Append(new Run(
                new RunProperties(new FontSize { Val = "16" }, new WordColor { Val = "808080" }),
                new WordText("Thank you for your payment") { Space = SpaceProcessingModeValues.Preserve }));
            footer.Append(thankYouPara);

            var pageFooterPara = new WordParagraph { ParagraphProperties = new ParagraphProperties(new Justification { Val = JustificationValues.Center }) };
            pageFooterPara.Append(new Run(
                new RunProperties(new FontSize { Val = "14" }, new WordColor { Val = "808080" }),
                new WordText("Generated by SHMS — Page ") { Space = SpaceProcessingModeValues.Preserve }));
            AppendFieldRuns(pageFooterPara, "PAGE", "14");
            pageFooterPara.Append(new Run(
                new RunProperties(new FontSize { Val = "14" }, new WordColor { Val = "808080" }),
                new WordText(" of ") { Space = SpaceProcessingModeValues.Preserve }));
            AppendFieldRuns(pageFooterPara, "NUMPAGES", "14");
            footer.Append(pageFooterPara);

            footerPart.Footer = footer;

            var sectPr = new SectionProperties(
                new FooterReference { Type = HeaderFooterValues.Default, Id = mainPart.GetIdOfPart(footerPart) },
                new PageSize { Width = 11906, Height = 16838 },
                new PageMargin { Top = 680, Right = 850, Bottom = 850, Left = 850 });
            body.Append(sectPr);

            mainPart.Document.Save();
        }

        return ms.ToArray();
    }

    private static WordParagraph BuildParagraph(string text, bool bold, string sizeHalfPoints, bool gray = false, bool center = false, bool rightAlign = false)
    {
        var runProps = new RunProperties { FontSize = new FontSize { Val = sizeHalfPoints } };
        if (bold) runProps.Append(new Bold());
        if (gray) runProps.Append(new WordColor { Val = "808080" });

        var para = new WordParagraph();
        if (center)
            para.ParagraphProperties = new ParagraphProperties(new Justification { Val = JustificationValues.Center });
        else if (rightAlign)
            para.ParagraphProperties = new ParagraphProperties(new Justification { Val = JustificationValues.Right });

        para.Append(new Run(runProps, new WordText(text) { Space = SpaceProcessingModeValues.Preserve }));
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
