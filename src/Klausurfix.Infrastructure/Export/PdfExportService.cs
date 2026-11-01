namespace Klausurfix.Infrastructure.Export;

using System.Globalization;
using Klausurfix.Core.Calculations;
using Klausurfix.Core.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

public class PdfExportService
{
    public byte[] GenerateCoverSheet(Exam exam) => CoverSheetExports.Pdf(CoverSheetDocument.Create(exam));

    static PdfExportService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.ThrowOnMissingFontFamilies = false;
        QuestPDF.Settings.UseSystemFonts = true;
    }

    public byte[] GenerateExamReport(Exam exam)
    {
        var summary = ExamCalculator.CalculateExamSummary(exam);
        var deCulture = CultureInfo.GetCultureInfo("de-DE");

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(9).FontColor("#243D3D"));

                // Header
                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(titleCol =>
                        {
                            titleCol.Item().Text("KLAUSURFIX  /  AUSWERTUNG")
                                .FontSize(8).SemiBold().FontColor("#3C7773");
                            titleCol.Item().PaddingTop(3).Text(exam.Title)
                                .FontSize(19).Bold().FontColor("#124E4C");
                            titleCol.Item().Text($"Klasse: {exam.ClassName}  |  Schulform: {exam.SchulformName}  |  Datum: {exam.Date:dd.MM.yyyy}")
                                .FontSize(10).FontColor(Colors.Grey.Darken2);
                        });

                        row.ConstantItem(180).AlignRight().Column(statCol =>
                        {
                            statCol.Item().Text($"Durchschnitt: {(summary.ClassAverage.HasValue ? summary.ClassAverage.Value.ToString("0.00", deCulture) : "-")} {summary.Unit}")
                                .FontSize(11).Bold().FontColor("#126763");
                            statCol.Item().Text($"Gewertet: {summary.GradedCount} von {summary.TotalStudents} Schülern")
                                .FontSize(9).FontColor(Colors.Grey.Darken2);
                        });
                    });

                    col.Item().PaddingTop(8).LineHorizontal(1).LineColor("#B9D0CD");
                });

                // Content
                page.Content().PaddingTop(10).Column(col =>
                {
                    // Table of Results
                    col.Item().Table(table =>
                    {
                        // Column definitions
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(25); // Nr
                            columns.RelativeColumn(3);  // Name
                            foreach (var _ in exam.Tasks)
                            {
                                columns.RelativeColumn(1.2f); // Task points
                            }
                            columns.RelativeColumn(1.3f); // Gesamt
                            columns.RelativeColumn(1.2f); // %
                            columns.RelativeColumn(1.8f); // Ausgangsnote
                            columns.RelativeColumn(1.1f); // W / F
                            columns.RelativeColumn(1.1f); // FQ
                            columns.RelativeColumn(1.1f); // Abzug
                            columns.RelativeColumn(1.3f); // Endnote
                            columns.RelativeColumn(1.8f); // Status
                        });

                        // Header row (repeated on all pages)
                        table.Header(header =>
                        {
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("#").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Schüler/in").Bold();
                            foreach (var t in exam.Tasks)
                            {
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignCenter()
                                    .Text($"A{t.TaskNumber}\n({t.MaxPoints.ToString("G", deCulture)}P)").Bold().FontSize(8);
                            }
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignCenter()
                                .Text($"Gesamt\n({exam.TotalMaxPoints.ToString("G", deCulture)}P)").Bold().FontSize(8);
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignCenter().Text("%").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignCenter().Text("Ausgangsnote").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignCenter().Text("W / F").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignCenter().Text("FQ").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignCenter().Text("Abzug").Bold();
                            header.Cell().Background("#C8E6E1").Padding(4).AlignCenter().Text("Endnote").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Status").Bold();
                        });

                        // Data rows
                        int rowIdx = 1;
                        foreach (var res in summary.Results)
                        {
                            var bg = rowIdx % 2 == 0 ? "#F3F8F7" : "#FFFFFF";
                            var entry = exam.StudentEntries.FirstOrDefault(e => e.StudentId == res.StudentId);

                            table.Cell().Background(bg).Padding(4).Text(rowIdx.ToString());
                            table.Cell().Background(bg).Padding(4).Text(res.StudentName).Bold();

                            foreach (var task in exam.Tasks)
                            {
                                string ptsStr = "-";
                                if (entry != null && entry.TaskPoints.TryGetValue(task.TaskNumber, out var p) && p.HasValue)
                                {
                                    ptsStr = p.Value.ToString("G", deCulture);
                                }
                                table.Cell().Background(bg).Padding(4).AlignCenter().Text(ptsStr);
                            }

                            table.Cell().Background(bg).Padding(4).AlignCenter()
                                .Text(res.RawPoints.HasValue ? res.RawPoints.Value.ToString("0.#", deCulture) : "-");
                            table.Cell().Background(bg).Padding(4).AlignCenter()
                                .Text(res.Percentage.HasValue ? $"{res.Percentage.Value.ToString("0.0", deCulture)}%" : "-");
                            table.Cell().Background(bg).Padding(4).AlignCenter()
                                .Text(res.InitialGrade.HasValue ? res.InitialGrade.Value.ToString() : "-");

                            // Words / Errors
                            string wfStr = res.WordCount.HasValue && res.ErrorCount.HasValue
                                ? $"{res.WordCount}/{res.ErrorCount}"
                                : "-";
                            table.Cell().Background(bg).Padding(4).AlignCenter().Text(wfStr);

                            table.Cell().Background(bg).Padding(4).AlignCenter()
                                .Text(res.ErrorQuotient.HasValue ? res.ErrorQuotient.Value.ToString("0.00", deCulture) : "-");
                            table.Cell().Background(bg).Padding(4).AlignCenter()
                                .Text(res.DeductionPoints.HasValue
                                    ? $"{res.Deduction.ToString("G", deCulture)}% ({res.DeductionPoints.Value.ToString("G", deCulture)}P); verbleiben {res.AdjustedPoints!.Value.ToString("G", deCulture)}P"
                                    : res.Deduction > 0 ? $"-{res.Deduction.ToString("G", deCulture)}" : "0");
                            table.Cell().Background("#E2F2EF").Padding(4).AlignCenter()
                                .Text(res.FinalGrade.HasValue ? res.FinalGrade.Value.ToString() : "-").Bold();
                            table.Cell().Background(bg).Padding(4).Text(res.StatusDescription);

                            rowIdx++;
                        }
                    });

                    // Summary & Notenspiegel section
                    col.Item().PaddingTop(18).Column(summaryCol =>
                    {
                        summaryCol.Item().Text("Notenspiegel").Bold().FontSize(13).FontColor("#124E4C");
                        summaryCol.Item().PaddingTop(2).Text($"Häufigkeitsverteilung · {summary.GradedCount} gewertet").FontSize(8).FontColor("#637D7A");
                        summaryCol.Item().PaddingTop(7).Table(nspTable =>
                        {
                            nspTable.ColumnsDefinition(cols =>
                            {
                                cols.ConstantColumn(80);
                                foreach (var _ in summary.GradeDistribution) cols.RelativeColumn();
                            });
                            nspTable.Cell().Background("#DCECE9").Padding(5).Text("Note / Punkte").SemiBold();
                            foreach (var grade in summary.GradeDistribution.Keys)
                                nspTable.Cell().Background("#DCECE9").Padding(5).AlignCenter().Text(grade).SemiBold();
                            nspTable.Cell().Background("#F3F8F7").Padding(5).Text("Anzahl").SemiBold();
                            foreach (var count in summary.GradeDistribution.Values)
                                nspTable.Cell().Background("#F3F8F7").Padding(5).AlignCenter().Text(count.ToString()).Bold();
                        });
                        summaryCol.Item().PaddingTop(14).BorderTop(1).BorderColor("#D5E5E2").PaddingTop(9).Column(ruleCol =>
                        {
                            ruleCol.Item().Text("Angewandte Bewertungsregeln").Bold().FontSize(10).FontColor("#124E4C");
                            ruleCol.Item().PaddingTop(4).Text($"• Notensystem: {(exam.GradingProfileSnapshot.System == GradingSystem.Schulnoten ? "Schulnoten (1-6)" : "Notenpunkte (0-15)")}");
                            ruleCol.Item().Text($"• Abzugseinheit: {ReportFormatting.DeductionUnit(exam.GradingProfileSnapshot.DeductionUnit)}");
                            ruleCol.Item().Text($"• Fehlerquotient-Formel: (Rechtschreibfehler × 100) / Wortanzahl");
                            ruleCol.Item().Text($"• Abzugsstufen:");
                            foreach (var tier in exam.GradingProfileSnapshot.DeductionTiers)
                            {
                                ruleCol.Item().PaddingLeft(10).Text($"- ab FQ {tier.Threshold.ToString("G", deCulture)}: -{tier.Deduction.ToString("G", deCulture)} {ReportFormatting.DeductionUnit(exam.GradingProfileSnapshot.DeductionUnit)}");
                            }
                            ruleCol.Item().PaddingTop(5).Text($"• Ausgeschlossene Schüler (Krank / Nicht gewertet): {summary.ExcludedCount}");
                        });
                    });
                });

                // Footer
                page.Footer().Row(footer =>
                {
                    footer.RelativeItem().Text($"Erstellt mit Klausurfix am {DateTime.Now:dd.MM.yyyy HH:mm} Uhr | Lokale Offline-Auswertung")
                        .FontSize(8).FontColor(Colors.Grey.Medium);
                    footer.RelativeItem().AlignRight().Text(x =>
                    {
                        x.Span("Seite ");
                        x.CurrentPageNumber();
                        x.Span(" von ");
                        x.TotalPages();
                    });
                });
            });
        });

        return doc.GeneratePdf();
    }
}
