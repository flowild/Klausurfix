namespace Klausurfix.Infrastructure.Export;

using System.Globalization;
using Klausurfix.Core.Calculations;
using Klausurfix.Core.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

public class PdfExportService
{
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
                page.DefaultTextStyle(x => x.FontSize(9));

                // Header
                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(titleCol =>
                        {
                            titleCol.Item().Text($"Klausurauswertung: {exam.Title}")
                                .FontSize(16).Bold().FontColor(Colors.Blue.Darken3);
                            titleCol.Item().Text($"Klasse: {exam.ClassName}  |  Schulform: {exam.SchulformName}  |  Datum: {exam.Date:dd.MM.yyyy}")
                                .FontSize(10).FontColor(Colors.Grey.Darken2);
                        });

                        row.ConstantItem(180).AlignRight().Column(statCol =>
                        {
                            statCol.Item().Text($"Durchschnitt: {(summary.ClassAverage.HasValue ? summary.ClassAverage.Value.ToString("0.00", deCulture) : "-")} {summary.Unit}")
                                .FontSize(11).Bold().FontColor(Colors.Blue.Darken2);
                            statCol.Item().Text($"Gewertet: {summary.GradedCount} von {summary.TotalStudents} Schülern")
                                .FontSize(9).FontColor(Colors.Grey.Darken2);
                        });
                    });

                    col.Item().PaddingTop(5).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
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
                            columns.RelativeColumn(1.2f); // Note (vor Abzug)
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
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignCenter().Text("Ausg.").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignCenter().Text("W / F").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignCenter().Text("FQ").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignCenter().Text("Abzug").Bold();
                            header.Cell().Background(Colors.Blue.Lighten4).Padding(4).AlignCenter().Text("Endnote").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Status").Bold();
                        });

                        // Data rows
                        int rowIdx = 1;
                        foreach (var res in summary.Results)
                        {
                            var bg = rowIdx % 2 == 0 ? Colors.Grey.Lighten5 : Colors.White;
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
                                .Text(res.Deduction > 0 ? $"-{res.Deduction.ToString("G", deCulture)}" : "0");
                            table.Cell().Background(Colors.Blue.Lighten5).Padding(4).AlignCenter()
                                .Text(res.FinalGrade.HasValue ? res.FinalGrade.Value.ToString() : "-").Bold();
                            table.Cell().Background(bg).Padding(4).Text(res.StatusDescription);

                            rowIdx++;
                        }
                    });

                    // Summary & Notenspiegel section
                    col.Item().PaddingTop(15).Row(summaryRow =>
                    {
                        // Left: Notenspiegel Table
                        summaryRow.RelativeItem(1).Column(nspCol =>
                        {
                            nspCol.Item().Text("Notenspiegel (Häufigkeitsverteilung)").Bold().FontSize(11);
                            nspCol.Item().PaddingTop(4).Table(nspTable =>
                            {
                                nspTable.ColumnsDefinition(cols =>
                                {
                                    cols.RelativeColumn();
                                    cols.RelativeColumn();
                                });

                                nspTable.Header(h =>
                                {
                                    h.Cell().Background(Colors.Grey.Lighten3).Padding(3).Text("Note / Punkte").Bold();
                                    h.Cell().Background(Colors.Grey.Lighten3).Padding(3).AlignCenter().Text("Anzahl").Bold();
                                });

                                foreach (var kvp in summary.GradeDistribution)
                                {
                                    nspTable.Cell().Padding(3).Text(kvp.Key);
                                    nspTable.Cell().Padding(3).AlignCenter().Text(kvp.Value.ToString()).Bold();
                                }
                            });
                        });

                        // Right: Evaluation rules & notes
                        summaryRow.RelativeItem(1.5f).PaddingLeft(20).Column(ruleCol =>
                        {
                            ruleCol.Item().Text("Angewandte Bewertungsregeln").Bold().FontSize(11);
                            ruleCol.Item().PaddingTop(4).Text($"• Notensystem: {(exam.GradingProfileSnapshot.System == GradingSystem.Schulnoten ? "Schulnoten (1-6)" : "Notenpunkte (0-15)")}");
                            ruleCol.Item().Text($"• Abzugseinheit: {exam.GradingProfileSnapshot.DeductionUnit}");
                            ruleCol.Item().Text($"• Fehlerquotient-Formel: (Rechtschreibfehler × 100) / Wortanzahl");
                            ruleCol.Item().Text($"• Abzugsstufen:");
                            foreach (var tier in exam.GradingProfileSnapshot.DeductionTiers)
                            {
                                ruleCol.Item().PaddingLeft(10).Text($"- ab FQ {tier.Threshold.ToString("G", deCulture)}: -{tier.Deduction.ToString("G", deCulture)} {exam.GradingProfileSnapshot.DeductionUnit}");
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
