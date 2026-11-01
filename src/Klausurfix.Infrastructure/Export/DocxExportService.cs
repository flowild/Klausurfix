namespace Klausurfix.Infrastructure.Export;

using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Klausurfix.Core.Calculations;
using Klausurfix.Core.Models;

public class DocxExportService
{
    public byte[] GenerateExamDocument(Exam exam)
    {
        var summary = ExamCalculator.CalculateExamSummary(exam);
        var deCulture = CultureInfo.GetCultureInfo("de-DE");

        using var memoryStream = new MemoryStream();
        using (var wordDoc = WordprocessingDocument.Create(memoryStream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = wordDoc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            // Set section properties to Landscape
            var sectionProps = new SectionProperties();
            var pageSize = new PageSize { Width = 16838U, Height = 11906U, Orient = PageOrientationValues.Landscape }; // A4 landscape
            var pageMargin = new PageMargin { Top = 1000, Bottom = 1000, Left = 1000, Right = 1000 };
            sectionProps.Append(pageSize, pageMargin);

            // Title
            var titlePara = body.AppendChild(new Paragraph(
                new ParagraphProperties(new SpacingBetweenLines { After = "120" }),
                new Run(
                    new RunProperties(new Bold(), new FontSize { Val = "32" }, new Color { Val = "1A365D" }),
                    new Text($"Klausurauswertung: {exam.Title}")
                )
            ));

            // Subtitle
            body.AppendChild(new Paragraph(
                new ParagraphProperties(new SpacingBetweenLines { After = "180" }),
                new Run(
                    new RunProperties(new FontSize { Val = "20" }, new Color { Val = "4A5568" }),
                    new Text($"Klasse: {exam.ClassName}   |   Schulform: {exam.SchulformName}   |   Datum: {exam.Date:dd.MM.yyyy}")
                )
            ));

            // Summary stats
            string avgStr = summary.ClassAverage.HasValue ? summary.ClassAverage.Value.ToString("0.00", deCulture) : "-";
            body.AppendChild(new Paragraph(
                new ParagraphProperties(new SpacingBetweenLines { After = "240" }),
                new Run(
                    new RunProperties(new Bold(), new FontSize { Val = "22" }, new Color { Val = "2B6CB0" }),
                    new Text($"Klassendurchschnitt: {avgStr} {summary.Unit}   |   Gewertet: {summary.GradedCount} von {summary.TotalStudents} Schülern (Ausgeschlossen: {summary.ExcludedCount})")
                )
            ));

            // Results Table
            var table = new Table();

            // Table properties
            var tblPr = new TableProperties(
                new TableBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 4, Color = "CCCCCC" },
                    new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "CCCCCC" },
                    new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "E2E8F0" },
                    new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "E2E8F0" }
                ),
                new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" } // 100%
            );
            table.AppendChild(tblPr);

            // Header row
            var headerRow = new TableRow(new TableRowProperties(new TableHeader()));
            headerRow.Append(
                CreateCell("#", isBold: true, isHeader: true),
                CreateCell("Schüler/in", isBold: true, isHeader: true)
            );

            foreach (var t in exam.Tasks)
            {
                headerRow.Append(CreateCell($"A{t.TaskNumber} ({t.MaxPoints.ToString("G", deCulture)}P)", isBold: true, isHeader: true));
            }

            headerRow.Append(
                CreateCell($"Gesamt ({exam.TotalMaxPoints.ToString("G", deCulture)}P)", isBold: true, isHeader: true),
                CreateCell("%", isBold: true, isHeader: true),
                CreateCell("Ausg.", isBold: true, isHeader: true),
                CreateCell("W / F", isBold: true, isHeader: true),
                CreateCell("FQ", isBold: true, isHeader: true),
                CreateCell("Abzug", isBold: true, isHeader: true),
                CreateCell("Endnote", isBold: true, isHeader: true, bgColor: "DBEAFE"),
                CreateCell("Status", isBold: true, isHeader: true)
            );
            table.AppendChild(headerRow);

            // Data rows
            int rowIdx = 1;
            foreach (var res in summary.Results)
            {
                var entry = exam.StudentEntries.FirstOrDefault(e => e.StudentId == res.StudentId);
                var row = new TableRow();

                row.Append(
                    CreateCell(rowIdx.ToString()),
                    CreateCell(res.StudentName, isBold: true)
                );

                foreach (var task in exam.Tasks)
                {
                    string ptsStr = "-";
                    if (entry != null && entry.TaskPoints.TryGetValue(task.TaskNumber, out var p) && p.HasValue)
                    {
                        ptsStr = p.Value.ToString("G", deCulture);
                    }
                    row.Append(CreateCell(ptsStr));
                }

                string wfStr = res.WordCount.HasValue && res.ErrorCount.HasValue ? $"{res.WordCount}/{res.ErrorCount}" : "-";
                string fqStr = res.ErrorQuotient.HasValue ? res.ErrorQuotient.Value.ToString("0.00", deCulture) : "-";
                string abzugStr = res.Deduction > 0 ? $"-{res.Deduction.ToString("G", deCulture)}" : "0";

                row.Append(
                    CreateCell(res.RawPoints.HasValue ? res.RawPoints.Value.ToString("0.#", deCulture) : "-"),
                    CreateCell(res.Percentage.HasValue ? $"{res.Percentage.Value.ToString("0.0", deCulture)}%" : "-"),
                    CreateCell(res.InitialGrade.HasValue ? res.InitialGrade.Value.ToString() : "-"),
                    CreateCell(wfStr),
                    CreateCell(fqStr),
                    CreateCell(abzugStr),
                    CreateCell(res.FinalGrade.HasValue ? res.FinalGrade.Value.ToString() : "-", isBold: true, bgColor: "EFF6FF"),
                    CreateCell(res.StatusDescription)
                );

                table.AppendChild(row);
                rowIdx++;
            }

            body.AppendChild(table);

            // Spacing
            body.AppendChild(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { Before = "300", After = "120" }),
                new Run(new RunProperties(new Bold(), new FontSize { Val = "24" }), new Text("Notenspiegel"))
            ));

            // Notenspiegel Table
            var nspTable = new Table();
            nspTable.AppendChild(new TableProperties(
                new TableBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 4, Color = "CCCCCC" },
                    new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "CCCCCC" },
                    new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "E2E8F0" },
                    new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "E2E8F0" }
                ),
                new TableWidth { Type = TableWidthUnitValues.Pct, Width = "2500" }
            ));

            var nspHeader = new TableRow(new TableRowProperties(new TableHeader()));
            nspHeader.Append(
                CreateCell("Note / Punkte", isBold: true, isHeader: true),
                CreateCell("Anzahl", isBold: true, isHeader: true)
            );
            nspTable.AppendChild(nspHeader);

            foreach (var kvp in summary.GradeDistribution)
            {
                var nspRow = new TableRow();
                nspRow.Append(
                    CreateCell(kvp.Key),
                    CreateCell(kvp.Value.ToString(), isBold: true)
                );
                nspTable.AppendChild(nspRow);
            }

            body.AppendChild(nspTable);

            // Append section properties at the end of body
            body.AppendChild(sectionProps);

            mainPart.Document.Save();
        }

        return memoryStream.ToArray();
    }

    private static TableCell CreateCell(string text, bool isBold = false, bool isHeader = false, string? bgColor = null)
    {
        var runProps = new RunProperties(new FontSize { Val = isHeader ? "18" : "17" });
        if (isBold) runProps.Append(new Bold());

        var p = new Paragraph(
            new ParagraphProperties(new SpacingBetweenLines { Before = "40", After = "40" }),
            new Run(runProps, new Text(text))
        );

        var cellPr = new TableCellProperties(
            new TableCellMargin(
                new TopMargin { Width = "60" },
                new BottomMargin { Width = "60" },
                new LeftMargin { Width = "100" },
                new RightMargin { Width = "100" }
            )
        );

        if (isHeader && bgColor == null)
        {
            cellPr.Append(new Shading { Val = ShadingPatternValues.Clear, Fill = "F1F5F9" });
        }
        else if (!string.IsNullOrEmpty(bgColor))
        {
            cellPr.Append(new Shading { Val = ShadingPatternValues.Clear, Fill = bgColor });
        }

        return new TableCell(cellPr, p);
    }
}
