namespace Klausurfix.Infrastructure.Export;

using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Klausurfix.Core.Calculations;
using Klausurfix.Core.Models;

public class DocxExportService
{
    public byte[] GenerateCoverSheet(Exam exam) => CoverSheetExports.Docx(CoverSheetDocument.Create(exam));

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
                    new RunProperties(new Bold(), new Color { Val = "124E4C" }, new FontSize { Val = "32" }),
                    new Text($"Klausurauswertung: {exam.Title}")
                )
            ));

            // Subtitle
            body.AppendChild(new Paragraph(
                new ParagraphProperties(new SpacingBetweenLines { After = "180" }),
                new Run(
                    new RunProperties(new Color { Val = "4A5568" }, new FontSize { Val = "20" }),
                    new Text($"Klasse: {exam.ClassName}   |   Schulform: {exam.SchulformName}   |   Datum: {exam.Date:dd.MM.yyyy}")
                )
            ));

            // Summary stats
            string avgStr = summary.ClassAverage.HasValue ? summary.ClassAverage.Value.ToString("0.00", deCulture) : "-";
            body.AppendChild(new Paragraph(
                new ParagraphProperties(new SpacingBetweenLines { After = "240" }),
                new Run(
                    new RunProperties(new Bold(), new Color { Val = "126763" }, new FontSize { Val = "22" }),
                    new Text($"Klassendurchschnitt: {avgStr} {summary.Unit}   |   Gewertet: {summary.GradedCount} von {summary.TotalStudents} Schülern (Ausgeschlossen: {summary.ExcludedCount})")
                )
            ));

            body.AppendChild(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "200" }),
                new Run(new RunProperties(new Bold()), new Text("Genehmigung der Klausur: ")),
                new Run(new Text(summary.ApprovalNotice))));

            // Results Table
            var table = new Table();

            // Table properties
            var tblPr = new TableProperties(
                new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
                new TableBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 4, Color = "CCCCCC" },
                    new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "CCCCCC" },
                    new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "E2E8F0" },
                    new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "E2E8F0" }
                )
            );
            table.AppendChild(tblPr);
            table.AppendChild(new TableGrid(Enumerable.Range(0, exam.Tasks.Count + 10).Select(_ => new GridColumn())));

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
                CreateCell("Ausgangsnote", isBold: true, isHeader: true),
                CreateCell("W / F", isBold: true, isHeader: true),
                CreateCell("FQ", isBold: true, isHeader: true),
                CreateCell("Abzug", isBold: true, isHeader: true),
                CreateCell("Endnote", isBold: true, isHeader: true, bgColor: "C8E6E1"),
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
                    string ptsStr = TaskPointExpression.Format(entry, task.TaskNumber);
                    row.Append(CreateCell(ptsStr));
                }

                string wfStr = res.WordCount.HasValue && res.ErrorCount.HasValue ? $"{res.WordCount}/{res.ErrorCount}" : "-";
                string fqStr = res.ErrorQuotient.HasValue ? res.ErrorQuotient.Value.ToString("0.00", deCulture) : "-";
                string abzugStr = res.DeductionPoints.HasValue
                    ? $"{res.Deduction.ToString("G", deCulture)}% ({res.DeductionPoints.Value.ToString("G", deCulture)} P), verbleiben {res.AdjustedPoints!.Value.ToString("G", deCulture)} P"
                    : res.Deduction > 0 ? $"-{res.Deduction.ToString("G", deCulture)}" : "0";

                row.Append(
                    CreateCell(res.RawPoints.HasValue ? res.RawPoints.Value.ToString("0.#", deCulture) : "-"),
                    CreateCell(res.Percentage.HasValue ? $"{res.Percentage.Value.ToString("0.0", deCulture)}%" : "-"),
                    CreateCell(res.InitialGrade.HasValue ? res.InitialGrade.Value.ToString() : "-"),
                    CreateCell(wfStr),
                    CreateCell(fqStr),
                    CreateCell(abzugStr),
                    CreateCell(res.FinalGrade.HasValue ? res.FinalGrade.Value.ToString() : "-", isBold: true, bgColor: "E2F2EF"),
                    CreateCell(res.StatusDescription)
                );

                table.AppendChild(row);
                rowIdx++;
            }

            body.AppendChild(table);

            body.AppendChild(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { Before = "300", After = "120" }),
                new Run(new RunProperties(new Bold(), new Color { Val = "124E4C" }, new FontSize { Val = "24" }), new Text("Fehlerschwellen"))));
            body.AppendChild(new Paragraph(new Run(new Text("Ab der angegebenen Fehlerzahl gilt der jeweilige Abzug. Prozentangaben beziehen sich auf die maximale Gesamtpunktzahl."))));
            var thresholdsTable = new Table(new TableProperties(new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" }),
                new TableGrid(new GridColumn(), new GridColumn(), new GridColumn(), new GridColumn()));
            thresholdsTable.AppendChild(new TableRow(new TableRowProperties(new TableHeader()),
                CreateCell("#", isBold: true, isHeader: true), CreateCell("Schüler/in", isBold: true, isHeader: true),
                CreateCell("Wörter", isBold: true, isHeader: true), CreateCell("Fehlerzahl: Abzug", isBold: true, isHeader: true)));
            for (var i = 0; i < exam.StudentEntries.Count; i++)
            {
                var entry = exam.StudentEntries[i];
                thresholdsTable.AppendChild(new TableRow(CreateCell((i + 1).ToString()), CreateCell(entry.StudentName),
                    CreateCell(entry.WordCount?.ToString() ?? "–"), CreateCell(ReportFormatting.ErrorThresholds(entry.WordCount, exam.GradingProfileSnapshot))));
            }
            body.AppendChild(thresholdsTable);

            // Spacing
            body.AppendChild(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { Before = "300", After = "120" }),
                new Run(new RunProperties(new Bold(), new Color { Val = "124E4C" }, new FontSize { Val = "24" }), new Text("Notenspiegel"))
            ));

            // Notenspiegel Table
            var nspTable = new Table();
            nspTable.AppendChild(new TableProperties(
                new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
                new TableBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 4, Color = "CCCCCC" },
                    new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "CCCCCC" },
                    new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "E2E8F0" },
                    new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "E2E8F0" }
                )
            ));
            nspTable.AppendChild(new TableGrid(Enumerable.Range(0, summary.GradeDistribution.Count + 1).Select(_ => new GridColumn())));

            var nspHeader = new TableRow(new TableRowProperties(new TableHeader()));
            nspHeader.Append(CreateCell("Note / Punkte", isBold: true, isHeader: true, bgColor: "DCECE9"));
            foreach (var grade in summary.GradeDistribution.Keys)
                nspHeader.Append(CreateCell(grade, isBold: true, isHeader: true, bgColor: "DCECE9"));
            nspTable.AppendChild(nspHeader);

            var nspRow = new TableRow();
            nspRow.Append(CreateCell("Anzahl", isBold: true, bgColor: "F3F8F7"));
            foreach (var count in summary.GradeDistribution.Values)
                nspRow.Append(CreateCell(count.ToString(), isBold: true, bgColor: "F3F8F7"));
            nspTable.AppendChild(nspRow);

            body.AppendChild(nspTable);

            body.AppendChild(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { Before = "240", After = "100" }),
                new Run(new RunProperties(new Bold(), new Color { Val = "124E4C" }, new FontSize { Val = "22" }), new Text("Angewandte Bewertungsregeln"))));
            var profile = exam.GradingProfileSnapshot;
            var unit = ReportFormatting.DeductionUnit(profile.DeductionUnit);
            var tiers = profile.DeductionTiers.Count == 0 ? "Keine Abzüge" : string.Join("; ", profile.DeductionTiers.Select(t =>
                $"ab FQ {t.Threshold.ToString("G", deCulture)}: -{t.Deduction.ToString("G", deCulture)} {unit}"));
            body.AppendChild(new Paragraph(new Run(new Text($"Notensystem: {(profile.System == GradingSystem.Schulnoten ? "Schulnoten (1–6)" : "Notenpunkte (0–15)")} · Abzugseinheit: {unit}"))));
            body.AppendChild(new Paragraph(new Run(new Text("Fehlerquotient = (Rechtschreibfehler × 100) / Wortanzahl"))));
            body.AppendChild(new Paragraph(new Run(new Text($"Abzugsstufen: {tiers}"))));
            body.AppendChild(new Paragraph(new Run(new Text($"Nicht gewertet: {summary.ExcludedCount}"))));

            // Append section properties at the end of body
            body.AppendChild(sectionProps);

            mainPart.Document.Save();
        }

        return memoryStream.ToArray();
    }

    private static TableCell CreateCell(string text, bool isBold = false, bool isHeader = false, string? bgColor = null)
    {
        var runProps = new RunProperties();
        if (isBold) runProps.Append(new Bold());
        runProps.Append(new FontSize { Val = isHeader ? "18" : "17" });

        var p = new Paragraph(
            new ParagraphProperties(new SpacingBetweenLines { Before = "40", After = "40" }),
            new Run(runProps, new Text(text))
        );

        var cellPr = new TableCellProperties(
            new TableCellMargin(
                new TopMargin { Width = "60", Type = TableWidthUnitValues.Dxa },
                new LeftMargin { Width = "100", Type = TableWidthUnitValues.Dxa },
                new BottomMargin { Width = "60", Type = TableWidthUnitValues.Dxa },
                new RightMargin { Width = "100", Type = TableWidthUnitValues.Dxa }
            )
        );

        if (isHeader && bgColor == null)
        {
            cellPr.PrependChild(new Shading { Val = ShadingPatternValues.Clear, Fill = "F1F5F9" });
        }
        else if (!string.IsNullOrEmpty(bgColor))
        {
            cellPr.PrependChild(new Shading { Val = ShadingPatternValues.Clear, Fill = bgColor });
        }

        return new TableCell(cellPr, p);
    }
}
