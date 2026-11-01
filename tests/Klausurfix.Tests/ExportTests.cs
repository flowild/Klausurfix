namespace Klausurfix.Tests;

using System.Text;
using Klausurfix.Core.Models;
using Klausurfix.Infrastructure.Export;
using Xunit;

public class ExportTests
{
    private Exam CreateSampleExam()
    {
        return new Exam
        {
            Title = "Mathematik Klausur #1",
            ClassName = "10b",
            SchulformName = "Realschule",
            Date = new DateOnly(2026, 11, 1),
            Tasks = new()
            {
                new TaskDefinition(1, "Aufgabe 1", 15m),
                new TaskDefinition(2, "Aufgabe 2", 25m)
            },
            GradingProfileSnapshot = GradingProfile.CreateDefaultSchulnoten(),
            StudentEntries = new()
            {
                new ExamStudentEntry
                {
                    StudentId = "s1",
                    StudentName = "Bauer, Lukas",
                    Status = StudentExamStatus.Graded,
                    TaskPoints = new() { { 1, 14m }, { 2, 22m } },
                    WordCount = 300,
                    ErrorCount = 3
                },
                new ExamStudentEntry
                {
                    StudentId = "s2",
                    StudentName = "Meier, Sophie",
                    Status = StudentExamStatus.Missing,
                    TaskPoints = new(),
                    WordCount = null,
                    ErrorCount = null
                }
            }
        };
    }

    [Fact]
    public void GeneratePdf_ShouldReturnValidPdfDocument()
    {
        var exam = CreateSampleExam();
        var pdfService = new PdfExportService();

        var pdfBytes = pdfService.GenerateExamReport(exam);

        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 100);

        // PDF signature starts with %PDF-
        var header = Encoding.ASCII.GetString(pdfBytes.Take(5).ToArray());
        Assert.Equal("%PDF-", header);
    }

    [Fact]
    public void GenerateDocx_ShouldReturnValidDocxArchive()
    {
        var exam = CreateSampleExam();
        var docxService = new DocxExportService();

        var docxBytes = docxService.GenerateExamDocument(exam);

        Assert.NotNull(docxBytes);
        Assert.True(docxBytes.Length > 100);

        // DOCX is a zip archive starting with PK (0x50, 0x4B)
        Assert.Equal(0x50, docxBytes[0]);
        Assert.Equal(0x4B, docxBytes[1]);
    }
}

