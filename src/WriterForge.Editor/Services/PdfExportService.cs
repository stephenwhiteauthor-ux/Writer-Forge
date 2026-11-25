using System.IO;
using System.Windows.Documents;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WF = System.Windows.Documents;

namespace WriterForge.Editor.Services;

/// <summary>
/// Exports the document to PDF via QuestPDF. Preserves paragraph styles
/// (headings bold/larger), runs (bold/italic/underline/strike), and text.
/// Images render as an "[image]" placeholder line.
/// </summary>
public static class PdfExportService
{
    static PdfExportService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private sealed record Entry(string StyleKey, string Text, bool Bold, bool Italic);

    public static void Export(FlowDocument document, string path, string title)
    {
        var entries = new List<Entry>();
        Collect(document.Blocks, entries);

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(60);
                page.DefaultTextStyle(x => x.FontFamily("Georgia").FontSize(11));
                page.Content().Column(col =>
                {
                    col.Item().Text(title).FontSize(20).Bold().FontColor(Colors.Grey.Darken2);
                    col.Item().PaddingTop(16);
                    foreach (var e in entries)
                    {
                        col.Item().PaddingVertical(2).Text(e.Text).Style(StyleFor(e));
                    }
                });
            });
        }).GeneratePdf(path);
    }

    private static void Collect(IEnumerable<WF.Block> blocks, List<Entry> entries)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case WF.Paragraph para:
                    var styleKey = EditorTags.GetStyleKey(para) ?? "Normal";
                    var (bold, italic) = ParaFlags(para);
                    entries.Add(new Entry(styleKey, ExtractText(para), bold, italic));
                    break;
                case WF.Section section:
                    Collect(section.Blocks, entries);
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
