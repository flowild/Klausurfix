namespace Klausurfix.Infrastructure.Export;

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using W = DocumentFormat.OpenXml.Wordprocessing;

public static class CoverSheetExports
{
    public static byte[] TemplateLogo()
    {
        using var stream = typeof(CoverSheetExports).Assembly.GetManifestResourceStream(
            "Klausurfix.Infrastructure.Export.Assets.template-logo.png")
            ?? throw new InvalidOperationException("Das lokale Vorlagenlogo fehlt.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static string OperatorTitle(Klausurfix.Core.Models.ExamOperator o) => $"Operator „{o.Name}“:";

    public static byte[] Pdf(CoverSheetDocument model)
    {
        var settings = model.Settings;
        return QuestPDF.Fluent.Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.MarginHorizontal(25, Unit.Millimetre);
            page.MarginVertical(18, Unit.Millimetre);
            page.DefaultTextStyle(s => s.FontFamily("Calibri", "Arial", "Lato").FontSize(11).FontColor("#000000"));
            page.Header().PaddingBottom(14).Column(header =>
            {
                if (settings.UseTemplateLogo) header.Item().AlignCenter().Width(182.4f).Height(38).Image(TemplateLogo()).FitArea();
                header.Item().AlignRight().Text(CoverSheetDocument.NameField).FontSize(12);
                header.Item().PaddingTop(6).Row(row =>
                {
                    row.RelativeItem().Text(model.HeaderTitle).FontSize(11);
                    row.RelativeItem().AlignCenter().Text(model.HeaderClass).FontSize(11);
                    row.RelativeItem().AlignRight().Text(model.HeaderDate).FontSize(11);
                });
            });
            page.Content().Decoration(decoration =>
            {
                decoration.After().ShowOnce().PaddingTop(10).Column(operators =>
                {
                    operators.Spacing(3);
                    foreach (var op in settings.Operators)
                    {
                        operators.Item().Text(OperatorTitle(op)).FontSize(9).Bold();
                        operators.Item().Text(op.Definition).FontSize(9);
                    }
                });
                decoration.Content().ExtendVertical().Column(col =>
                {
                col.Spacing(6);
                if (settings.ShowGradeKey)
                {
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(65);
                            foreach (var _ in model.GradeKey) c.RelativeColumn();
                        });
                        void Cell(string text, bool bold = false, bool shaded = false)
                        {
                            var t = table.Cell().Background(shaded ? "#D0CECE" : "#FFFFFF")
                                .Border(0.5f).BorderColor("#000000").Padding(3).AlignCenter().Text(text).FontSize(8);
                            if (bold) t.Bold();
                        }
                        Cell("Punkte", true, true);
                        foreach (var grade in model.GradeKey) Cell(grade.Percentage, true, true);
                        Cell(model.GradeUnit, true);
                        foreach (var grade in model.GradeKey) Cell(grade.Grade);
                    });
                }
                if (settings.ShowDeductionRules && model.DeductionNotice.Length > 0)
                    col.Item().Text(model.DeductionNotice).AlignCenter().FontSize(10).Bold();
                if (settings.Instructions.Length > 0) col.Item().AlignCenter().Text(settings.Instructions).AlignCenter();
                if (settings.ShowWordCount)
                {
                    col.Item().AlignCenter().Text(CoverSheetDocument.WordCountHint).FontSize(10).Bold();
                    col.Item().AlignCenter().Text(CoverSheetDocument.WordCountField).FontSize(10).Bold();
                }
                if (model.AdditionalFields.Count > 0)
                    col.Item().AlignCenter().Text(string.Join("    ", model.AdditionalFields)).FontSize(9).Bold();
                foreach (var task in model.Tasks)
                {
                    col.Item().EnsureSpace(48).PaddingTop(16).Text(task.Title).Bold().Italic();
                    col.Item().PaddingLeft(35).Text(text =>
                    {
                        text.Justify();
                        if (task.Text.Length == 0) text.Span("Aufgabenstellung: ___________________________________________________");
                        foreach (var part in task.TextParts)
                        {
                            var span = text.Span(part.Text);
                            if (part.Underlined) span.Underline();
                        }
                    });
                    col.Item().PaddingRight(35).AlignRight().Text(task.ShortPoints).FontSize(12);
                }
                });
            });
            page.Footer().PaddingTop(10).AlignRight().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8));
                    t.Span("Seite "); t.CurrentPageNumber(); t.Span(" von "); t.TotalPages();
                });
        })).GeneratePdf();
    }

    public static byte[] Docx(CoverSheetDocument model)
    {
        using var memory = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(memory, WordprocessingDocumentType.Document, true))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new W.Document(new Body());
            var body = main.Document.Body!;
            var styles = main.AddNewPart<StyleDefinitionsPart>();
            styles.Styles = new Styles(new DocDefaults(new RunPropertiesDefault(new RunPropertiesBaseStyle(
                new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri" }, new FontSize { Val = "22" })),
                new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(new SpacingBetweenLines { After = "100" }))));

            var header = main.AddNewPart<HeaderPart>();
            header.Header = new Header();
            if (model.Settings.UseTemplateLogo)
            {
                var image = header.AddImagePart(ImagePartType.Png);
                using var imageStream = new MemoryStream(TemplateLogo());
                image.FeedData(imageStream);
                var drawing = new Drawing(new DW.Inline(
                    new DW.Extent { Cx = 2316163L, Cy = 482640L },
                    new DW.DocProperties { Id = 1U, Name = "Schullogo", Description = "Saalburgschule Usingen" },
                    new A.Graphic(new A.GraphicData(new PIC.Picture(
                        new PIC.NonVisualPictureProperties(new PIC.NonVisualDrawingProperties { Id = 0U, Name = "Schullogo" }, new PIC.NonVisualPictureDrawingProperties()),
                        new PIC.BlipFill(new A.Blip { Embed = header.GetIdOfPart(image) }, new A.Stretch(new A.FillRectangle())),
                        new PIC.ShapeProperties(new A.Transform2D(new A.Offset { X = 0L, Y = 0L }, new A.Extents { Cx = 2316163L, Cy = 482640L }),
                            new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })
                    )) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })
                ) { DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U });
                header.Header.Append(new Paragraph(new ParagraphProperties(new Justification { Val = JustificationValues.Center }), new Run(drawing)));
            }
            header.Header.Append(Para(CoverSheetDocument.NameField, size: 24, alignment: JustificationValues.Right));
            var headerTable = new Table(new TableProperties(new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
                    new TableLayout { Type = TableLayoutValues.Fixed }),
                new TableGrid(new GridColumn { Width = "3023" }, new GridColumn { Width = "3023" }, new GridColumn { Width = "3024" }));
            TableCell HeaderCell(string text, string width, JustificationValues alignment) => new(
                new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = width }),
                Para(text, size: 22, alignment: alignment, after: "0"));
            headerTable.Append(new TableRow(HeaderCell(model.HeaderTitle, "3023", JustificationValues.Left),
                HeaderCell(model.HeaderClass, "3023", JustificationValues.Center),
                HeaderCell(model.HeaderDate, "3024", JustificationValues.Right)));
            header.Header.Append(headerTable);

            var footer = main.AddNewPart<FooterPart>();
            footer.Footer = new Footer();
            footer.Footer.Append(new Paragraph(new ParagraphProperties(new Justification { Val = JustificationValues.Right }),
                new Run(new RunProperties(new FontSize { Val = "16" }), new Text("Seite ") { Space = SpaceProcessingModeValues.Preserve }),
                new SimpleField { Instruction = "PAGE" }, new Run(new Text(" von ") { Space = SpaceProcessingModeValues.Preserve }),
                new SimpleField { Instruction = "NUMPAGES" }));

            // The floating body frame is anchored on page one; its reserved area keeps task text clear.
            var operatorLines = model.Settings.Operators.Sum(o => (int)Math.Ceiling(o.Definition.Length / 65d) + 2);
            var operatorHeight = operatorLines * 220;
            var bottomMargin = 1000 + operatorHeight;
            foreach (var op in model.Settings.Operators)
            foreach (var paragraph in new[] { Para(OperatorTitle(op), bold: true, size: 18, after: "0", black: true), Para(op.Definition, size: 18, after: "40", black: true) })
            {
                var frame = new FrameProperties();
                const string w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                frame.SetAttribute(new OpenXmlAttribute("w", "w", w, "9070"));
                frame.SetAttribute(new OpenXmlAttribute("w", "hAnchor", w, "margin"));
                frame.SetAttribute(new OpenXmlAttribute("w", "vAnchor", w, "page"));
                frame.SetAttribute(new OpenXmlAttribute("w", "x", w, "0"));
                frame.SetAttribute(new OpenXmlAttribute("w", "y", w, (16838 - bottomMargin).ToString(System.Globalization.CultureInfo.InvariantCulture)));
                frame.SetAttribute(new OpenXmlAttribute("w", "wrap", w, "notBeside"));
                paragraph.ParagraphProperties!.PrependChild(frame);
                body.Append(paragraph);
            }

            if (model.Settings.ShowGradeKey)
            {
                var table = new Table(new TableProperties(new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
                    new TableBorders(new TopBorder { Val = BorderValues.Single, Size = 4 }, new LeftBorder { Val = BorderValues.Single, Size = 4 },
                        new BottomBorder { Val = BorderValues.Single, Size = 4 }, new RightBorder { Val = BorderValues.Single, Size = 4 },
                        new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 }, new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 })));
                table.Append(new TableGrid(Enumerable.Range(0, model.GradeKey.Count + 1).Select(_ => new GridColumn())));
                TableRow Row(IEnumerable<string> values, bool shaded) => new(values.Select((text, index) =>
                {
                    var properties = new TableCellProperties();
                    if (shaded) properties.Append(new Shading { Val = ShadingPatternValues.Clear, Fill = "D0CECE" });
                    return new TableCell(properties, Para(text, bold: shaded || index == 0, size: 16, alignment: JustificationValues.Center, after: "0"));
                }));
                table.Append(Row(new[] { "Punkte" }.Concat(model.GradeKey.Select(g => g.Percentage)), true),
                    Row(new[] { model.GradeUnit }.Concat(model.GradeKey.Select(g => g.Grade)), false));
                body.Append(table);
            }
            if (model.Settings.ShowDeductionRules && model.DeductionNotice.Length > 0)
                body.Append(Para(model.DeductionNotice, bold: true, size: 20, alignment: JustificationValues.Center));
            if (model.Settings.Instructions.Length > 0) body.Append(Para(model.Settings.Instructions, alignment: JustificationValues.Center));
            if (model.Settings.ShowWordCount) body.Append(Para(CoverSheetDocument.WordCountHint, bold: true, size: 20, alignment: JustificationValues.Center), Para(CoverSheetDocument.WordCountField, bold: true, size: 20, alignment: JustificationValues.Center));
            if (model.AdditionalFields.Count > 0)
                body.Append(Para(string.Join("    ", model.AdditionalFields), bold: true, size: 18, alignment: JustificationValues.Center));
            foreach (var task in model.Tasks)
            {
                var heading = Para(task.Title, bold: true, italic: true, size: 22, before: "250", alignment: JustificationValues.Left);
                heading.ParagraphProperties!.PrependChild(new KeepNext());
                var paragraph = Para("", size: 22, alignment: JustificationValues.Both, indent: "708");
                paragraph.RemoveAllChildren<Run>();
                var parts = task.Text.Length > 0 ? task.TextParts : new() { new OperatorTextPart("Aufgabenstellung: ___________________________________________________", false) };
                foreach (var part in parts)
                {
                    var properties = new RunProperties(new FontSize { Val = "22" });
                    if (part.Underlined) properties.Append(new Underline { Val = UnderlineValues.Single });
                    var run = new Run(properties);
                    var lines = part.Text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                    for (var i = 0; i < lines.Length; i++)
                    {
                        if (i > 0) run.Append(new Break());
                        run.Append(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
                    }
                    paragraph.Append(run);
                }
                var points = Para(task.ShortPoints, size: 24, alignment: JustificationValues.Right);
                points.ParagraphProperties!.InsertBefore(new Indentation { Right = "708" }, points.ParagraphProperties.Justification);
                body.Append(heading, paragraph, points);
            }
            SectionProperties Section(int bottom) => new(new HeaderReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(header) },
                new FooterReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(footer) },
                new SectionType { Val = SectionMarkValues.NextPage },
                new W.PageSize { Width = 11906U, Height = 16838U },
                new PageMargin { Top = model.Settings.UseTemplateLogo ? 2200 : 1600, Bottom = bottom, Left = 1418U, Right = 1418U, Header = 500U, Footer = 500U });
            // A new section supplies an empty source page with the same header and normal writing space.
            body.Append(new Paragraph(new ParagraphProperties(Section(bottomMargin))), new Paragraph(), Section(1000));
            main.Document.Save();
        }
        return memory.ToArray();
    }

    private static Paragraph Para(string text, bool bold = false, int size = 20, bool italic = false,
        JustificationValues? alignment = null, string? indent = null, string after = "100", string? before = null, bool black = false)
    {
        var run = new Run(new RunProperties());
        if (bold) run.RunProperties!.Append(new Bold());
        if (italic) run.RunProperties!.Append(new Italic());
        if (black) run.RunProperties!.Append(new W.Color { Val = "000000" });
        run.RunProperties!.Append(new FontSize { Val = size.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) run.Append(new Break());
            run.Append(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
        }
        var spacing = new SpacingBetweenLines { After = after };
        if (before != null) spacing.Before = before;
        var props = new ParagraphProperties(spacing);
        if (indent != null) props.Append(new Indentation { Left = indent });
        if (alignment.HasValue) props.Append(new Justification { Val = alignment.Value });
        return new Paragraph(props, run);
    }
}
