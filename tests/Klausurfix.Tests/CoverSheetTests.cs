namespace Klausurfix.Tests;

using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using Klausurfix.Core.Models;
using Klausurfix.Infrastructure.Export;

public class CoverSheetTests
{
    private static Exam Sample()
    {
        var exam = new Exam
        {
            Title = "Klausur: Gesellschaft im Wandel", ClassName = "Testklasse BG", SchulformName = "Berufliches Gymnasium",
            Date = new(2026, 11, 2), GradingProfileSnapshot = GradingProfile.BeruflichesGymnasium(),
            Tasks = new() { new(1, "Aufgabe 1", 25m), new(2, "Aufgabe 2", 30m), new(3, "Aufgabe 3", 45m) },
            StudentEntries = new() { new() { StudentName = "NICHT AUF DAS DECKBLATT", TaskPoints = new() { [1] = 24m } } }
        };
        return exam with { CoverSheet = CoverSheetDocument.Defaults(exam) with
        {
            Subject = "Politik und Wirtschaft", Instructions = "Arbeitszeit: 90 Minuten.\nViel Erfolg!",
            Tasks = new() { new(1, "Geben Sie die zentralen Aussagen des Materials wieder."),
                new(2, "Erklären Sie die Auswirkungen auf den Arbeitsmarkt."), new(3, "Diskutieren Sie die These anhand von vier Argumenten.") }
        } };
    }

    [Fact]
    public void Docx_HasOperatorBodyFrameAndPageNumberFooterAndDoesNotIncludeStudentResults()
    {
        var bytes = new DocxExportService().GenerateCoverSheet(Sample());
        using var stream = new MemoryStream(bytes);
        using var doc = WordprocessingDocument.Open(stream, false);
        AssertValid(doc);
        var main = doc.MainDocumentPart!;
        Assert.Single(main.FooterParts);
        Assert.Single(main.HeaderParts.Single().ImageParts);
        var footer = main.FooterParts.Single().Footer!.InnerText;
        Assert.DoesNotContain("Operator", footer);
        Assert.Contains("Operator „wiedergeben“:", main.Document!.Body!.InnerText);
        Assert.Contains("Operator „erklären“:", main.Document.Body.InnerText);
        Assert.Contains("Begründungszusammenhang", main.Document.Body.InnerText);
        Assert.DoesNotContain("Hessisches Ministerium", footer);
        Assert.DoesNotContain("NICHT AUF DAS DECKBLATT", main.Document!.InnerText + footer);
        Assert.Contains("02.11.2026", main.HeaderParts.Single().Header!.InnerText);
        Assert.Contains("__/25", main.Document.Body.InnerText);
        Assert.Equal(16, main.Document.Body.Elements<Table>().Single().Elements<TableRow>().First().Elements<TableCell>().Skip(1).Count());
        Assert.Contains("Arbeitszeit: 90 Minuten.", main.Document.Body.InnerText);
        Assert.Contains("Mängel in Rechtschreibung, Grammatik und Ausdruck können zu einem Punktabzug von bis zu 2 Notenpunkten führen.", main.Document.Body.InnerText);
        Assert.DoesNotContain("Es gilt die höchste erreichte Prozentgrenze", main.Document.Body.InnerText);
        Assert.DoesNotContain("Nur die höchste erreichte Stufe", main.Document.Body.InnerText);

        var headerParagraphs = main.HeaderParts.Single().Header!.Elements<Paragraph>().ToList();
        Assert.Equal(JustificationValues.Center, headerParagraphs[0].ParagraphProperties!.Justification!.Val!.Value);
        Assert.Equal(JustificationValues.Right, headerParagraphs[1].ParagraphProperties!.Justification!.Val!.Value);
        var headerCells = main.HeaderParts.Single().Header!.Elements<Table>().Single().Elements<TableRow>().Single().Elements<TableCell>().ToList();
        Assert.Contains("Politik und Wirtschaft", headerCells[0].InnerText);
        Assert.Equal("Testklasse BG", headerCells[1].InnerText);
        Assert.Equal("02.11.2026", headerCells[2].InnerText);
        Assert.Equal(JustificationValues.Left, headerCells[0].Elements<Paragraph>().Single().ParagraphProperties!.Justification!.Val!.Value);
        Assert.Equal(JustificationValues.Center, headerCells[1].Elements<Paragraph>().Single().ParagraphProperties!.Justification!.Val!.Value);
        Assert.Equal(JustificationValues.Right, headerCells[2].Elements<Paragraph>().Single().ParagraphProperties!.Justification!.Val!.Value);
        var table = main.Document.Body.Elements<Table>().Single();
        Assert.Equal("D0CECE", table.Elements<TableRow>().First().Elements<TableCell>().First().TableCellProperties!.Shading!.Fill!.Value);
        var taskHeading = main.Document.Body.Elements<Paragraph>().Single(p => p.InnerText == "Aufgabe 1");
        Assert.NotNull(taskHeading.Descendants<Italic>().FirstOrDefault());
        Assert.Equal(JustificationValues.Left, taskHeading.ParagraphProperties!.Justification!.Val!.Value);
        var taskText = main.Document.Body.Elements<Paragraph>().Single(p => p.InnerText.StartsWith("Geben Sie"));
        Assert.Equal(JustificationValues.Both, taskText.ParagraphProperties!.Justification!.Val!.Value);
        Assert.Equal("708", taskText.ParagraphProperties.Indentation!.Left!.Value);
        Assert.Equal(new[] { "Geben", "wieder" }, taskText.Elements<Run>()
            .Where(r => r.RunProperties?.Underline != null).Select(r => r.InnerText));
        var points = main.Document.Body.Elements<Paragraph>().Single(p => p.InnerText == "__/25");
        Assert.Equal(JustificationValues.Right, points.ParagraphProperties!.Justification!.Val!.Value);
        var operatorParagraphs = main.Document.Body.Elements<Paragraph>().Where(p => p.ParagraphProperties?.GetFirstChild<FrameProperties>() != null).ToList();
        Assert.Equal(6, operatorParagraphs.Count);
        Assert.StartsWith("Operator „wiedergeben“:", operatorParagraphs[0].InnerText);
        Assert.Contains("Einleitungssatz", operatorParagraphs[1].InnerText);
        Assert.All(operatorParagraphs.SelectMany(p => p.Elements<Run>()), r => Assert.Equal("000000", r.RunProperties!.Color!.Val!.Value));
    }

    [Fact]
    public void DocxFileName_FollowsTemplateAndUsesSafeWindowsCharacters()
    {
        var sample = Sample();
        var exam = sample with { ClassName = "11 BG", Title = "PoWi", Date = new DateOnly(2025, 5, 8) };
        Assert.Equal("11 BG PoWi Klausur 08.05.2025.docx", CoverSheetFileName.Docx(exam));
        Assert.Equal("11 BG PoWi Klausur 08.05.2025.docx", CoverSheetFileName.Docx(exam with { Title = "Klausur: PoWi" }));
        Assert.Equal("11 BG PoWi Klausur 08.05.2025.docx", CoverSheetFileName.Docx(exam with { Title = "PoWi Klausur" }));
        Assert.Equal("11 BG Politik Wirtschaft Klausur 08.05.2025.docx", CoverSheetFileName.Docx(exam with { Title = "Politik/Wirtschaft" }));
        Assert.Equal("11 BG Klausurfix Klausur 08.05.2025.docx", CoverSheetFileName.Docx(exam with { Title = "Klausurfix" }));
    }

    [Fact]
    public void Docx_AddsBlankSourcePageWithSameHeaderAndNormalBottomMargin()
    {
        using var stream = new MemoryStream(new DocxExportService().GenerateCoverSheet(Sample()));
        using var doc = WordprocessingDocument.Open(stream, false);
        AssertValid(doc);
        var body = doc.MainDocumentPart!.Document!.Body!;
        var sourceSection = body.Elements<SectionProperties>().Single();
        var sectionBreak = body.Elements<Paragraph>().Single(p => p.ParagraphProperties?.GetFirstChild<SectionProperties>() != null);
        var coverSection = sectionBreak.ParagraphProperties!.GetFirstChild<SectionProperties>()!;
        Assert.Equal(SectionMarkValues.NextPage, coverSection.GetFirstChild<SectionType>()!.Val!.Value);
        Assert.Equal(coverSection.GetFirstChild<HeaderReference>()!.Id!.Value, sourceSection.GetFirstChild<HeaderReference>()!.Id!.Value);
        Assert.True(coverSection.GetFirstChild<PageMargin>()!.Bottom!.Value > sourceSection.GetFirstChild<PageMargin>()!.Bottom!.Value);
        Assert.Equal(1000, sourceSection.GetFirstChild<PageMargin>()!.Bottom!.Value);
        var sourceParagraph = Assert.IsType<Paragraph>(sectionBreak.NextSibling());
        Assert.Empty(sourceParagraph.InnerText);
        Assert.Same(sourceSection, sourceParagraph.NextSibling());
    }

    [Fact]
    public void OperatorRecognition_RecognizesSplitFormsAndPreservesTextWithoutPartialWords()
    {
        var text = "Geben Sie die Aussagen wieder. Erklären Sie die Folgen. Diskutieren Sie die These. Wiedergeben und vergleichen. Untersuchungen.";
        var result = OperatorRecognition.Recognize(text);
        Assert.Equal(text, string.Concat(result.Parts.Select(p => p.Text)));
        Assert.Equal(new[] { "Geben", "wieder", "Erklären", "Diskutieren", "Wiedergeben", "vergleichen" },
            result.Parts.Where(p => p.Underlined).Select(p => p.Text));
        Assert.Equal(new[] { "wiedergeben", "erklaeren", "diskutieren", "vergleichen" }, result.OperatorIds);
        Assert.Empty(OperatorRecognition.Recognize("Untersuchungen und Erklärungen.").OperatorIds);
        Assert.Empty(OperatorRecognition.Recognize("Geben Sie die Antwort. Wieder beginnt ein Satz.").OperatorIds);
        Assert.Equal(new[] { "zusammenfassen", "darstellen", "einordnen" },
            OperatorRecognition.Recognize("Fassen Sie zusammen. Stellen Sie dar. Ordnen Sie ein.").OperatorIds);
    }

    [Fact]
    public void Header_UsesAutomaticPrefixWithoutDuplicatingExistingTitlesOrSubjects()
    {
        var sample = Sample();
        var exam = sample with { Title = "Politik und Wirtschaft", ClassName = "11 BG-E", Date = new DateOnly(2026, 11, 2),
            CoverSheet = sample.CoverSheet! with { Subject = "" } };
        var header = CoverSheetDocument.Create(exam);
        Assert.Equal("Klausur Politik und Wirtschaft", header.HeaderTitle);
        Assert.Equal("11 BG-E", header.HeaderClass);
        Assert.Equal("02.11.2026", header.HeaderDate);
        Assert.Equal("Klausur Politik und Wirtschaft", CoverSheetDocument.Create(exam with
        {
            Title = "Klausur Politik und Wirtschaft", CoverSheet = exam.CoverSheet! with { Subject = "Politik und Wirtschaft" }
        }).HeaderTitle);
        Assert.Equal("Klausur: Politik und Wirtschaft", CoverSheetDocument.Create(exam with
        {
            Title = "Klausur: Politik und Wirtschaft", CoverSheet = exam.CoverSheet! with { Subject = "Politik und Wirtschaft" }
        }).HeaderTitle);
        Assert.Equal("Klausur Klausurfix", CoverSheetDocument.Create(exam with { Title = "Klausurfix" }).HeaderTitle);
        Assert.Equal("Klausur Gesellschaft im Wandel Politik und Wirtschaft", CoverSheetDocument.Create(exam with
        {
            Title = "Gesellschaft im Wandel", CoverSheet = exam.CoverSheet! with { Subject = "Politik und Wirtschaft" }
        }).HeaderTitle);
    }

    [Fact]
    public void SharedDocument_UsesExamSnapshotAndExactDecimalThresholdsAndPercentageDeduction()
    {
        var exam = Sample() with
        {
            Tasks = new() { new(1, "Erste Aufgabe", 12.25m), new(2, "Zweite Aufgabe", 24.5m), new(3, "Dritte Aufgabe", 15m) },
            GradingProfileSnapshot = GradingProfile.FachschuleSozialwesen() with
            {
                GradeKey = new() { new(87.123456m, 1), new(0m, 6) }, MaxDeduction = 3m
            }
        };
        var model = CoverSheetDocument.Create(exam);
        Assert.Equal("100–88", model.GradeKey[0].Percentage);
        Assert.Equal("87–0", model.GradeKey[1].Percentage);
        Assert.Equal("Schulnote", model.GradeUnit);
        Assert.Equal("Gesamt: ______ / 51,75 Punkte", model.TotalPoints);
        Assert.Equal("______ / 12,25 Punkte", model.Tasks[0].Points);
        Assert.Contains("bis zu 3 % der maximalen Gesamtpunktzahl", model.DeductionNotice);
        Assert.DoesNotContain("Schulnoten", model.DeductionNotice);
        var custom = exam with { CoverSheet = exam.CoverSheet! with { DeductionNotice = "Eigener Hinweis." } };
        Assert.Equal("Eigener Hinweis.", CoverSheetDocument.Create(custom).DeductionNotice);
        Assert.Equal("", CoverSheetDocument.Create(custom with { CoverSheet = custom.CoverSheet! with { DeductionNotice = "" } }).DeductionNotice);
    }

    [Fact]
    public void EarnedPoints_StayOptionalExactAndSeparateFromStudentScores()
    {
        var sample = Sample();
        var exam = sample with { CoverSheet = sample.CoverSheet! with
        {
            Tasks = new() { new(1, "Erste Aufgabe", 12.75m), new(2, "Zweite Aufgabe", 0m), new(3, "Dritte Aufgabe") }
        } };
        var model = CoverSheetDocument.Create(exam);
        Assert.Equal("12,75/25", model.Tasks[0].ShortPoints);
        Assert.Equal("0/30", model.Tasks[1].ShortPoints);
        Assert.Equal("__/45", model.Tasks[2].ShortPoints);
        Assert.Equal(24m, exam.StudentEntries[0].TaskPoints[1]);
        using var stream = new MemoryStream(new DocxExportService().GenerateCoverSheet(exam));
        using var doc = WordprocessingDocument.Open(stream, false);
        AssertValid(doc);
        Assert.Contains("12,75/25", doc.MainDocumentPart!.Document!.Body!.InnerText);
        Assert.Contains("0/30", doc.MainDocumentPart.Document.Body.InnerText);
        Assert.Contains("__/45", doc.MainDocumentPart.Document.Body.InnerText);
        Assert.Throws<InvalidOperationException>(() => CoverSheetDocument.Create(exam with
        {
            CoverSheet = exam.CoverSheet! with { Tasks = new() { new(1, "", -0.1m), new(2, ""), new(3, "") } }
        }));
        Assert.Throws<InvalidOperationException>(() => CoverSheetDocument.Create(exam with
        {
            CoverSheet = exam.CoverSheet! with { Tasks = new() { new(1, "", 25.0000001m), new(2, ""), new(3, "") } }
        }));
    }

    [Fact]
    public void Docx_OptionsCanOmitLogoGradeKeyRulesWordCountAndOperators()
    {
        var sample = Sample();
        var exam = sample with { CoverSheet = sample.CoverSheet! with
        {
            UseTemplateLogo = false, ShowGradeKey = false, ShowDeductionRules = false, ShowWordCount = false, Operators = new()
        } };
        using var stream = new MemoryStream(new DocxExportService().GenerateCoverSheet(exam));
        using var doc = WordprocessingDocument.Open(stream, false);
        AssertValid(doc);
        Assert.Empty(doc.MainDocumentPart!.HeaderParts.Single().ImageParts);
        Assert.Empty(doc.MainDocumentPart.Document!.Body!.Elements<Table>());
        Assert.DoesNotContain("Fehlerquotient", doc.MainDocumentPart.Document.InnerText);
        Assert.DoesNotContain("Anzahl Wörter", doc.MainDocumentPart.Document.InnerText);
        Assert.DoesNotContain("Landesabitur", doc.MainDocumentPart.FooterParts.Single().Footer!.InnerText);
        Assert.Contains("Name:", doc.MainDocumentPart.HeaderParts.Single().Header!.InnerText);
    }

    [Fact]
    public void Pdf_LongTasksAndSixLongestOperatorDefinitions_CanSpanMultiplePages()
    {
        var sample = Sample();
        var exam = sample with { CoverSheet = sample.CoverSheet! with
        {
            Operators = OperatorCatalog.All.OrderByDescending(o => o.Definition.Length).Take(6).ToList(),
            Tasks = sample.Tasks.Select(t => new CoverSheetTask(t.TaskNumber,
                string.Join(" ", Enumerable.Repeat("Untersuchen Sie die Zusammenhänge und begründen Sie Ihre Einschätzung anhand des Materials.", 25)))).ToList()
        } };
        var pdf = new PdfExportService().GenerateCoverSheet(exam);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf.Take(5).ToArray()));
        var directory = Environment.GetEnvironmentVariable("KLAUSURFIX_COVER_PREVIEW_DIR");
        if (!string.IsNullOrWhiteSpace(directory)) { Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, "deckblatt-lang.pdf"), pdf); }
        using var stream = new MemoryStream(new DocxExportService().GenerateCoverSheet(exam));
        using var doc = WordprocessingDocument.Open(stream, false);
        AssertValid(doc);
    }

    private static void AssertValid(WordprocessingDocument doc)
    {
        var errors = new OpenXmlValidator().Validate(doc).Select(e => e.Description + " at " + e.Path?.XPath).ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Fact]
    public void ExistingExamWithoutCoverSheet_CreatesTemplateWithoutMutatingRulesOrExam()
    {
        var exam = Sample() with { CoverSheet = null };
        var model = CoverSheetDocument.Create(exam);
        Assert.False(model.IsSaved);
        Assert.Null(exam.CoverSheet);
        Assert.Equal(3, model.Settings.Operators.Count);
        Assert.Equal(31, OperatorCatalog.All.Count);
        Assert.Equal(new[] { "wiedergeben", "erklaeren", "diskutieren" }, model.Settings.Operators.Select(o => o.Id));
        Assert.Equal(3m, exam.GradingProfileSnapshot.DeductionTiers[0].Threshold);
    }

    [Fact]
    public void ExportRepresentativeDocuments_WhenPreviewDirectoryIsSpecified()
    {
        // Optional local review artifacts contain synthetic exam data only.
        var directory = Environment.GetEnvironmentVariable("KLAUSURFIX_COVER_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var exam = Sample();
        File.WriteAllBytes(Path.Combine(directory, "deckblatt.pdf"), new PdfExportService().GenerateCoverSheet(exam));
        File.WriteAllBytes(Path.Combine(directory, "deckblatt.docx"), new DocxExportService().GenerateCoverSheet(exam));
        var headerExample = exam with { Title = "Politik und Wirtschaft", ClassName = "11 BG-E", CoverSheet = exam.CoverSheet! with { Subject = "" } };
        File.WriteAllBytes(Path.Combine(directory, "deckblatt-kopf.pdf"), new PdfExportService().GenerateCoverSheet(headerExample));
        File.WriteAllBytes(Path.Combine(directory, "deckblatt-kopf.docx"), new DocxExportService().GenerateCoverSheet(headerExample));
        var withPoints = exam with { CoverSheet = exam.CoverSheet! with
        {
            Tasks = exam.CoverSheet!.Tasks.Select((t, i) => t with { EarnedPoints = i == 0 ? 12.75m : i == 1 ? 0m : null }).ToList()
        } };
        File.WriteAllBytes(Path.Combine(directory, "deckblatt-punkte.pdf"), new PdfExportService().GenerateCoverSheet(withPoints));
        File.WriteAllBytes(Path.Combine(directory, "deckblatt-punkte.docx"), new DocxExportService().GenerateCoverSheet(withPoints));
        File.WriteAllText(Path.Combine(directory, "deckblatt.json"), JsonSerializer.Serialize(CoverSheetDocument.Create(exam), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        File.WriteAllText(Path.Combine(directory, "operatoren.json"), JsonSerializer.Serialize(new { source = OperatorCatalog.Source, operators = OperatorCatalog.All }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}
