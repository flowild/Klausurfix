namespace Klausurfix.Tests;

using System.Text;
using Klausurfix.Core.Models;
using Klausurfix.Infrastructure.Export;
using Xunit;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;

public class ExportTests
{
    [Fact]
    public void ReportsIncludeIndividualThresholdsEvenForIncompleteEntries()
    {
        var sample = CreateSampleExam();
        var exam = sample with
        {
            GradingProfileSnapshot = GradingProfile.BeruflichesGymnasium() with { MaxDeduction = 1m },
            StudentEntries = new() {
                sample.StudentEntries[0] with { WordCount = 600, ErrorCount = null },
                sample.StudentEntries[1] with { WordCount = 0 } }
        };
        using var stream = new MemoryStream(new DocxExportService().GenerateExamDocument(exam));
        using var document = WordprocessingDocument.Open(stream, false);
        var tables = document.MainDocumentPart!.Document!.Body!.Elements<Table>().ToList();
        var rows = tables[1].Elements<TableRow>().ToList();
        Assert.Contains("18: -1 NP", rows[1].InnerText);
        Assert.DoesNotContain("36:", rows[1].InnerText);
        Assert.Contains("Wortzahl fehlt oder ist 0", rows[2].InnerText);
        Assert.Empty(new OpenXmlValidator().Validate(document));
        Assert.NotEmpty(new PdfExportService().GenerateExamReport(exam));
        var percent = exam with { GradingProfileSnapshot = GradingProfile.FachschuleSozialwesen(),
            StudentEntries = new() { sample.StudentEntries[0] with { WordCount = 100 } } };
        using var percentStream = new MemoryStream(new DocxExportService().GenerateExamDocument(percent));
        using var percentDocument = WordprocessingDocument.Open(percentStream, false);
        Assert.Contains("2: -1 %; 4: -3 %; 6: -6 %", percentDocument.MainDocumentPart!.Document!.Body!.InnerText);
    }

    [Fact]
    public void ReportsIncludeTaskAdditionAndItsSum()
    {
        var sample = CreateSampleExam();
        var exam = sample with { StudentEntries = new() { sample.StudentEntries[0] with
            { TaskPointExpressions = new() { [1] = "5+4+5" } } } };
        Assert.Equal("5 + 4 + 5 = 14", TaskPointExpression.Format(exam.StudentEntries[0], 1));
        using var stream = new MemoryStream(new DocxExportService().GenerateExamDocument(exam));
        using var document = WordprocessingDocument.Open(stream, false);
        Assert.Contains("5 + 4 + 5 = 14", document.MainDocumentPart!.Document!.Body!.InnerText);
        Assert.Contains("Genehmigung der Klausur:", document.MainDocumentPart.Document.Body.InnerText);
        Assert.NotEmpty(new PdfExportService().GenerateExamReport(exam));
    }

    [Theory]
    [InlineData(150, 5)]
    [InlineData(40, 20)]
    public void LargeReports_PreserveUnicodeAndAllStudentRows(int students, int tasks)
    {
        var sample = CreateSampleExam();
        var exam = sample with
        {
            Title = "Prüfung ÄÖÜ ß & <Test>", ClassName = "Klässe Ü",
            Tasks = Enumerable.Range(1, tasks).Select(i => new TaskDefinition(i, $"Aufgabe {i}", 10m)).ToList(),
            StudentEntries = Enumerable.Range(1, students).Select(i => new ExamStudentEntry
            {
                StudentId = $"synthetic-{i}", StudentName = $"Änne Öztürk ß {i}",
                Status = StudentExamStatus.Graded, WordCount = 200, ErrorCount = 2,
                TaskPoints = Enumerable.Range(1, tasks).ToDictionary(n => n, _ => (decimal?)7.5m)
            }).ToList()
        };
        var pdf = new PdfExportService().GenerateExamReport(exam);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf.Take(5).ToArray()));
        using var stream = new MemoryStream(new DocxExportService().GenerateExamDocument(exam));
        using var document = WordprocessingDocument.Open(stream, false);
        Assert.Empty(new OpenXmlValidator().Validate(document));
        var text = document.MainDocumentPart!.Document!.Body!.InnerText;
        Assert.Contains(exam.Title, text);
        Assert.Contains(exam.ClassName, text);
        Assert.Contains($"Änne Öztürk ß {students}", text);
        Assert.Equal(students + 1, document.MainDocumentPart.Document.Body.Elements<Table>().First().Elements<TableRow>().Count());
    }

    [Fact]
    public void Docx_GradeDistributionIsHorizontalAndIncludesTheRules()
    {
        var exam = CreateSampleExam() with { GradingProfileSnapshot = GradingProfile.BeruflichesGymnasium() };
        var bytes = new DocxExportService().GenerateExamDocument(exam);
        using var stream = new MemoryStream(bytes);
        using var document = WordprocessingDocument.Open(stream, false);
        var body = document.MainDocumentPart?.Document?.Body;
        Assert.NotNull(body);
        var tables = body.Elements<Table>().ToList();
        Assert.Equal(3, tables.Count);
        var rows = tables[2].Elements<TableRow>().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(17, rows[0].Elements<TableCell>().Count());
        Assert.Equal(17, rows[1].Elements<TableCell>().Count());
        Assert.Equal("15", rows[0].Elements<TableCell>().Skip(1).First().InnerText);
        Assert.Equal("0", rows[0].Elements<TableCell>().Last().InnerText);
        Assert.Contains("Fehlerquotient", body.InnerText);
        Assert.Contains("ab FQ 3", body.InnerText);
        Assert.Empty(new OpenXmlValidator().Validate(document));
    }

    [Fact]
    public void Pdf_WithManyStudentsAndSixteenGradeLevels_GeneratesSuccessfully()
    {
        var sample = CreateSampleExam();
        var exam = sample with
        {
            GradingProfileSnapshot = GradingProfile.BeruflichesGymnasium(),
            StudentEntries = Enumerable.Range(1, 65).Select(i => sample.StudentEntries[0] with
                { StudentId = i.ToString(), StudentName = $"Testschüler {i}" }).ToList()
        };
        var pdf = new PdfExportService().GenerateExamReport(exam);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf.Take(5).ToArray()));
    }
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
        using var stream = new MemoryStream(docxBytes);
        using var document = WordprocessingDocument.Open(stream, false);
        var errors = new OpenXmlValidator().Validate(document).Select(e => e.Description).ToList();
        Assert.True(errors.Count == 0, string.Join("; ", errors.Take(10)));
    }
}

