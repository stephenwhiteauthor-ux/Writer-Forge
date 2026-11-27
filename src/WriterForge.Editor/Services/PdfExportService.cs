using System.IO;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WF = System.Windows.Documents;

namespace WriterForge.Editor.Services;

/// <summary>
/// Exports the document to PDF via QuestPDF. Preserves paragraph styles
/// (headings bold/larger), runs (bold/italic/underline/strike), tables and
/// embedded images.
/// </summary>
public static class PdfExportService
{
    static PdfExportService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private sealed record Entry(
        string StyleKey, string Text, bool Bold, bool Italic,
        List<List<string>>? Table = null,
        byte[]? Image = null, double ImageWidth = 0, double ImageHeight = 0);

    public static void Export(FlowDocument document, string path, string title, string? header = null, string? footer = null)
    {
        var entries = new List<Entry>();
        Collect(document.Blocks, entries);

        var notes = FootnoteService.NotesForOutput(document);
        if (notes.Count > 0)
        {
            entries.Add(new Entry("NotesHeading", FootnoteService.NotesHeading(document), true, false));
            foreach (var note in notes)
                entries.Add(new Entry("Normal", note, false, false));
        }

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(60);
                page.DefaultTextStyle(x => x.FontFamily("Georgia").FontSize(11));
                if (!string.IsNullOrWhiteSpace(header))
                    page.Header().AlignCenter().Text(header).FontSize(10).FontColor(Colors.Grey.Darken1);
                page.Footer().AlignCenter().Text(x =>
                {
                    if (!string.IsNullOrWhiteSpace(footer))
                    {
                        x.Span(footer + "    —    ").FontSize(10).FontColor(Colors.Grey.Darken1);
                    }
                    x.Span("Page ").FontSize(10).FontColor(Colors.Grey.Darken1);
                    x.CurrentPageNumber().FontSize(10).FontColor(Colors.Grey.Darken1);
                    x.Span(" of ").FontSize(10).FontColor(Colors.Grey.Darken1);
                    x.TotalPages().FontSize(10).FontColor(Colors.Grey.Darken1);
                });
                page.Content().Column(col =>
                {
                    col.Item().Text(title).FontSize(20).Bold().FontColor(Colors.Grey.Darken2);
                    col.Item().PaddingTop(16);
                    foreach (var e in entries)
                    {
                        if (e.Table is { } table)
                        {
                            col.Item().Table(t =>
                            {
                                var cols = table.Max(r => r.Count);
                                t.ColumnsDefinition(c =>
                                {
                                    for (var i = 0; i < cols; i++) c.RelativeColumn();
                                });
                                foreach (var row in table)
                                {
                                    for (var i = 0; i < cols; i++)
                                    {
                                        var text = i < row.Count ? row[i] : "";
                                        t.Cell().Border(0.5f).BorderColor(Colors.Grey.Medium).Padding(4)
                                            .Text(text);
                                    }
                                }
                            });
                        }
                        else if (e.Image is { } img)
                        {
                            col.Item().PaddingVertical(4)
                                .Width((float)Math.Min(e.ImageWidth, 480))
                                .Image(img).FitWidth();
                        }
                        else
                        {
                            col.Item().PaddingVertical(2).Text(e.Text).Style(StyleFor(e));
                        }
                    }
                });
            });
        }).GeneratePdf(path);
    }

    private static void Collect(IEnumerable<WF.Block> blocks, List<Entry> entries)
    {
        foreach (var block in blocks)
        {
            if (block is WF.Paragraph notePara && FootnoteService.IsNoteParagraph(notePara))
                continue;
            switch (block)
            {
                case WF.Paragraph para:
                    var styleKey = EditorTags.GetStyleKey(para) ?? "Normal";
                    var level = StyleKeys.HeadingLevel(styleKey);
                    if (level > 0) styleKey = $"Heading{level}";
                    var (bold, italic) = ParaFlags(para);
                    EmitParagraph(para, styleKey, bold, italic, entries);
                    break;
                case WF.Section section:
                    Collect(section.Blocks, entries);
                    break;
                case WF.Table table:
                    entries.Add(new Entry("Normal", "", false, false, Table: TableRows(table)));
                    break;
                case WF.List list:
                    var n = 1;
                    var numbered = list.MarkerStyle == System.Windows.TextMarkerStyle.Decimal;
                    foreach (var item in list.ListItems)
                    {
                        foreach (var child in BlockTree.WalkBlocks(item.Blocks))
                        {
                            var prefix = numbered ? $"{n}. " : "• ";
                            var (b, i) = ParaFlags(child);
                            entries.Add(new Entry("Normal", prefix + ExtractText(child), b, i));
                        }
                        n++;
                    }
                    break;
            }
        }
    }

    /// <summary>Flatten a table into rows of cell texts for QuestPDF.</summary>
    private static List<List<string>> TableRows(WF.Table table)
    {
        var rows = new List<List<string>>();
        foreach (var group in table.RowGroups)
        foreach (TableRow row in group.Rows)
        {
            var cells = new List<string>();
            foreach (TableCell cell in row.Cells)
            {
                var parts = BlockTree.WalkBlocks(cell.Blocks).Select(ExtractText);
                cells.Add(string.Join(" ", parts.Where(s => s.Length > 0)));
            }
            rows.Add(cells);
        }
        return rows;
    }

    /// <summary>
    /// Emit a paragraph as text entries, splitting around inline images so
    /// each image becomes its own embeddable entry.
    /// </summary>
    private static void EmitParagraph(
        WF.Paragraph para, string styleKey, bool bold, bool italic, List<Entry> entries)
    {
        var sb = new System.Text.StringBuilder();
        void Flush()
        {
            if (sb.Length == 0) return;
            entries.Add(new Entry(styleKey, sb.ToString(), bold, italic));
            sb.Clear();
        }

        void Walk(System.Windows.Documents.InlineCollection inlines)
        {
            foreach (var inline in inlines)
            {
                switch (inline)
                {
                    case WF.Run r: sb.Append(r.Text); break;
                    case WF.LineBreak: sb.Append('\n'); break;
                    case WF.Span s: Walk(s.Inlines); break;
                    case WF.InlineUIContainer c:
                        if (TryImageEntry(c, out var img))
                        {
                            Flush();
                            entries.Add(img);
                        }
                        else sb.Append("[image]");
                        break;
                }
            }
        }
        Walk(para.Inlines);
        Flush();
    }

    /// <summary>Encode an inline image's bitmap to PNG bytes with display size in points.</summary>
    private static bool TryImageEntry(WF.InlineUIContainer container, out Entry entry)
    {
        entry = new Entry("Normal", "", false, false);
        if (container.Child is not System.Windows.Controls.Image img || img.Source is not BitmapSource src)
            return false;

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(src));
        using var ms = new MemoryStream();
        encoder.Save(ms);

        // Display width mirrors the editor: explicit Width capped by MaxWidth,
        // else natural width capped by MaxWidth. Points = px * 0.75.
        var maxW = img.MaxWidth > 0 ? img.MaxWidth : double.PositiveInfinity;
        var w = double.IsNaN(img.Width) ? Math.Min(src.Width, maxW) : Math.Min(img.Width, maxW);
        var h = w * src.Height / src.Width;
        entry = new Entry("Normal", "", false, false,
            Image: ms.ToArray(), ImageWidth: w * 0.75, ImageHeight: h * 0.75);
        return true;
    }

    private static TextStyle StyleFor(Entry e)
    {
        var s = new TextStyle();
        switch (e.StyleKey)
        {
            case "Heading1": s = s.FontSize(18).Bold(); break;
            case "Heading2": s = s.FontSize(15).Bold(); break;
            case "Heading3": s = s.FontSize(13).Bold(); break;
            case "Quote": s = s.Italic().FontColor(Colors.Grey.Darken1); break;
            case "Caption": s = s.FontSize(9).Italic(); break;
            case "NotesHeading": s = s.FontSize(14).Bold(); break;
        }
        if (e.Bold) s = s.Bold();
        if (e.Italic) s = s.Italic();
        return s;
    }

    private static (bool Bold, bool Italic) ParaFlags(WF.Paragraph para)
    {
        bool? bold = null; bool? italic = null;
        foreach (var run in Runs(para))
        {
            bold ??= run.FontWeight == System.Windows.FontWeights.Bold;
            italic ??= run.FontStyle == System.Windows.FontStyles.Italic;
        }
        return (bold == true, italic == true);
    }

    private static IEnumerable<WF.Run> Runs(WF.Paragraph para)
    {
        foreach (var inline in para.Inlines)
        {
            switch (inline)
            {
                case WF.Run r: yield return r; break;
                case WF.Span s: foreach (var rr in RunsFrom(s.Inlines)) yield return rr; break;
            }
        }
    }

    private static IEnumerable<WF.Run> RunsFrom(System.Windows.Documents.InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case WF.Run r: yield return r; break;
                case WF.Span s: foreach (var rr in RunsFrom(s.Inlines)) yield return rr; break;
            }
        }
    }

    private static string ExtractText(WF.Paragraph para)
    {
        var sb = new System.Text.StringBuilder();
        void Walk(System.Windows.Documents.InlineCollection inlines)
        {
            foreach (var inline in inlines)
            {
                switch (inline)
                {
                    case WF.Run r: sb.Append(r.Text); break;
                    case WF.LineBreak: sb.Append('\n'); break;
                    case WF.InlineUIContainer: sb.Append("[image]"); break;
                    case WF.Span s: Walk(s.Inlines); break;
                }
            }
        }
        Walk(para.Inlines);
        return sb.ToString();
    }
}
