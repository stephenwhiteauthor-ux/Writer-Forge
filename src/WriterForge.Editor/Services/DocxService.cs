using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Color = System.Windows.Media.Color;
using Hyperlink = System.Windows.Documents.Hyperlink;
using Paragraph = System.Windows.Documents.Paragraph;
using Run = System.Windows.Documents.Run;
using WpAlignment = DocumentFormat.OpenXml.Wordprocessing.JustificationValues;
using WpBody = DocumentFormat.OpenXml.Wordprocessing.Body;
using WpBold = DocumentFormat.OpenXml.Wordprocessing.Bold;
using WpBookmarkEnd = DocumentFormat.OpenXml.Wordprocessing.BookmarkEnd;
using WpBookmarkStart = DocumentFormat.OpenXml.Wordprocessing.BookmarkStart;
using WpBreak = DocumentFormat.OpenXml.Wordprocessing.Break;
using WpColor = DocumentFormat.OpenXml.Wordprocessing.Color;
using WpDocument = DocumentFormat.OpenXml.Wordprocessing.Document;
using WpFontSize = DocumentFormat.OpenXml.Wordprocessing.FontSize;
using WpHyperlink = DocumentFormat.OpenXml.Wordprocessing.Hyperlink;
using WpItalic = DocumentFormat.OpenXml.Wordprocessing.Italic;
using WpJustification = DocumentFormat.OpenXml.Wordprocessing.Justification;
using WpNoBreakHyphen = DocumentFormat.OpenXml.Wordprocessing.NoBreakHyphen;
using WpParagraph = DocumentFormat.OpenXml.Wordprocessing.Paragraph;
using WpParagraphProperties = DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties;
using WpParagraphStyleId = DocumentFormat.OpenXml.Wordprocessing.ParagraphStyleId;
using WpRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using WpRunFonts = DocumentFormat.OpenXml.Wordprocessing.RunFonts;
using WpRunProperties = DocumentFormat.OpenXml.Wordprocessing.RunProperties;
using WpSdtRun = DocumentFormat.OpenXml.Wordprocessing.SdtRun;
using WpSoftHyphen = DocumentFormat.OpenXml.Wordprocessing.SoftHyphen;
using WpStrike = DocumentFormat.OpenXml.Wordprocessing.Strike;
using WpTabChar = DocumentFormat.OpenXml.Wordprocessing.TabChar;
using WpText = DocumentFormat.OpenXml.Wordprocessing.Text;
using WpUnderline = DocumentFormat.OpenXml.Wordprocessing.Underline;
using WpUnderlineValues = DocumentFormat.OpenXml.Wordprocessing.UnderlineValues;
using WpVerticalPositionValues = DocumentFormat.OpenXml.Wordprocessing.VerticalPositionValues;
using WpVerticalTextAlignment = DocumentFormat.OpenXml.Wordprocessing.VerticalTextAlignment;

namespace WriterForge.Editor.Services
{
    /// <summary>
    /// Converts between FlowDocument and .docx (WordprocessingML) using
    /// DocumentFormat.OpenXml. Paragraph style keys round-trip via
    /// <see cref="EditorTags.StyleKeyProperty"/>; run formatting maps to
    /// standard Word run properties.
    /// </summary>
    public static class DocxService
    {
        // ---------- Export ----------

        public static void Export(string path, FlowDocument document)
        {
            using var wordDoc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
            var mainPart = wordDoc.AddMainDocumentPart();
            mainPart.Document = new WpDocument();
            var body = mainPart.Document.AppendChild(new WpBody());

            foreach (var block in document.Blocks)
                ExportBlock(block, body);

            mainPart.Document.Save();
        }

        private static void ExportBlock(Block block, WpBody body)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    body.AppendChild(ExportParagraph(paragraph));
                    break;
                case Section section:
                    foreach (var child in section.Blocks)
                        ExportBlock(child, body);
                    break;
                case List list:
                    var number = 1;
                    foreach (var item in list.ListItems)
                    {
                        var prefix = list.MarkerStyle == TextMarkerStyle.Disc ? "• " : $"{number}. ";
                        var first = true;
                        foreach (var child in item.Blocks)
                        {
                            if (child is Paragraph itemParagraph)
                                body.AppendChild(ExportParagraph(itemParagraph, first ? prefix : null));
                            else
                                ExportBlock(child, body);
                            first = false;
                        }
                        number++;
                    }
                    break;
            }
        }

        private static WpParagraph ExportParagraph(Paragraph paragraph, string? prefix = null)
        {
            var wp = new WpParagraph();
            var pPr = new WpParagraphProperties();

            var styleId = MapStyleKey(EditorTags.GetStyleKey(paragraph), paragraph);
            if (styleId is not null)
                pPr.Append(new WpParagraphStyleId { Val = styleId });

            var alignment = paragraph.TextAlignment switch
            {
                TextAlignment.Center => WpAlignment.Center,
                TextAlignment.Right => WpAlignment.Right,
                TextAlignment.Justify => WpAlignment.Both,
                _ => WpAlignment.Left,
            };
            pPr.Append(new WpJustification { Val = alignment });
            wp.Append(pPr);

            if (prefix is not null)
                wp.AppendChild(new WpRun(new WpText(prefix) { Space = SpaceProcessingModeValues.Preserve }));

            foreach (var run in EnumerateRuns(paragraph.Inlines))
            {
                if (run is Run r)
                    wp.AppendChild(ExportRun(r));
                else if (run is LineBreak)
                    wp.AppendChild(new WpRun(new WpBreak()));
            }

            return wp;
        }

        private static string? MapStyleKey(string? key, Paragraph paragraph)
        {
            if (key is not null)
            {
                return key switch
                {
                    "StyleHeading1" => "Heading1",
                    "StyleHeading2" => "Heading2",
                    "StyleHeading3" => "Heading3",
                    "StyleQuote" => "Quote",
                    "StyleCaption" => "Caption",
                    _ => null,
                };
            }

            // Fallback: infer heading level from direct formatting.
            var firstRun = EnumerateRuns(paragraph.Inlines).OfType<Run>().FirstOrDefault();
            if (firstRun is not null &&
                firstRun.FontWeight == FontWeights.Bold &&
                firstRun.FontSize >= 16)
            {
                return firstRun.FontSize >= 23 ? "Heading1" : firstRun.FontSize >= 19 ? "Heading2" : "Heading3";
            }
            return null;
        }

        private static WpRun ExportRun(Run run)
        {
            var wpRun = new WpRun();
            var rPr = new WpRunProperties();

            if (run.FontWeight == FontWeights.Bold) rPr.Append(new WpBold());
            if (run.FontStyle == FontStyles.Italic) rPr.Append(new WpItalic());

            var decorations = GetRealDecorations(run);
            var hasUnderline = decorations.Any(d => d.Location == TextDecorationLocation.Underline);
            var hasStrike = decorations.Any(d => d.Location == TextDecorationLocation.Strikethrough);
            if (hasUnderline) rPr.Append(new WpUnderline { Val = WpUnderlineValues.Single });
            if (hasStrike) rPr.Append(new WpStrike());

            if (run.BaselineAlignment == BaselineAlignment.Superscript)
                rPr.Append(new WpVerticalTextAlignment { Val = WpVerticalPositionValues.Superscript });
            else if (run.BaselineAlignment == BaselineAlignment.Subscript)
                rPr.Append(new WpVerticalTextAlignment { Val = WpVerticalPositionValues.Subscript });

            if (!string.IsNullOrEmpty(run.FontFamily?.Source))
                rPr.Append(new WpRunFonts { Ascii = run.FontFamily.Source, HighAnsi = run.FontFamily.Source });

            if (run.FontSize > 0)
                rPr.Append(new WpFontSize { Val = ((int)(run.FontSize * 2)).ToString() });

            if (run.Foreground is SolidColorBrush brush)
                rPr.Append(new WpColor { Val = brush.Color.ToString().TrimStart('#') });

            if (rPr.HasChildren) wpRun.Append(rPr);
            wpRun.Append(new WpText(run.Text) { Space = SpaceProcessingModeValues.Preserve });
            return wpRun;
        }

        /// <summary>Text decorations excluding spell/grammar proof underlines.</summary>
        private static IEnumerable<TextDecoration> GetRealDecorations(Run run)
        {
            if (run.GetValue(Inline.TextDecorationsProperty) is not TextDecorationCollection decorations)
                yield break;
            foreach (var d in decorations)
                if (!ProofDecorations.IsProof(d))
                    yield return d;
        }

        // ---------- Import ----------

        public static FlowDocument Import(string path)
        {
            using var wordDoc = WordprocessingDocument.Open(path, false);
            var body = wordDoc.MainDocumentPart?.Document?.Body
                ?? throw new InvalidDataException("The .docx file has no document body.");

            var document = new FlowDocument();
            var section = new Section();
            document.Blocks.Add(section);

            var imported = new List<Paragraph>();
            foreach (var wpParagraph in body.Elements<WpParagraph>())
            {
                var paragraph = ImportParagraph(wpParagraph);
                if (paragraph is not null)
                    imported.Add(paragraph);
            }

            foreach (var block in GroupIntoLists(imported))
                section.Blocks.Add(block);

            return document;
        }

        private static readonly Regex ListMarkerPattern =
            new(@"^\s*([•·▪◦]|\d{1,3}[.)])(\s+)", RegexOptions.Compiled);

        /// <summary>Group consecutive paragraphs starting with a bullet/number marker into List blocks.</summary>
        private static IEnumerable<Block> GroupIntoLists(List<Paragraph> paragraphs)
        {
            var result = new List<Block>();
            List? open = null;
            TextMarkerStyle? openStyle = null;

            void Flush()
            {
                if (open is not null) result.Add(open);
                open = null;
                openStyle = null;
            }

            foreach (var p in paragraphs)
            {
                var style = StripListMarker(p);
                if (style is null)
                {
                    Flush();
                    result.Add(p);
                    continue;
                }
                if (open is null || openStyle != style)
                {
                    Flush();
                    open = new List { MarkerStyle = style.Value };
                    openStyle = style;
                }
                var item = new ListItem();
                item.Blocks.Add(p);
                open.ListItems.Add(item);
            }
            Flush();
            return result;
        }

        /// <summary>Remove a leading list marker from a paragraph; returns the marker style or null.</summary>
        private static TextMarkerStyle? StripListMarker(Paragraph paragraph)
        {
            var firstRun = paragraph.Inlines.OfType<Run>().FirstOrDefault();
            if (firstRun is null) return null;

            var match = ListMarkerPattern.Match(firstRun.Text);
            if (!match.Success) return null;

            firstRun.Text = firstRun.Text[match.Length..];
            if (firstRun.Text.Length == 0)
                paragraph.Inlines.Remove(firstRun);

            return match.Groups[1].Value.Any(char.IsDigit)
                ? TextMarkerStyle.Decimal
                : TextMarkerStyle.Disc;
        }

        private static Paragraph? ImportParagraph(WpParagraph wp)
        {
            var paragraph = new Paragraph();

            // Style
            var styleId = wp.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
            var styleKey = styleId switch
            {
                "Heading1" or "Title" => "StyleHeading1",
                "Heading2" or "Subtitle" => "StyleHeading2",
                "Heading3" => "StyleHeading3",
                "Quote" or "IntenseQuote" => "StyleQuote",
                "Caption" => "StyleCaption",
                _ when styleId?.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) == true &&
                     int.TryParse(styleId["Heading".Length..], out var level) =>
                    level <= 1 ? "StyleHeading1" : level == 2 ? "StyleHeading2" : "StyleHeading3",
                _ => null,
            };
            EditorTags.SetStyleKey(paragraph, styleKey);

            // Alignment
            var jc = wp.ParagraphProperties?.Justification?.Val?.ToString();
            paragraph.TextAlignment = jc switch
            {
                "center" => TextAlignment.Center,
                "right" or "end" => TextAlignment.Right,
                "both" => TextAlignment.Justify,
                _ => TextAlignment.Left,
            };

            // Runs
            foreach (var element in wp.ChildElements)
            {
                switch (element)
                {
                    case WpRun wpRun:
                        var run = ImportRun(wpRun);
                        if (run is not null) paragraph.Inlines.Add(run);
                        break;
                    case WpHyperlink wpLink:
                        foreach (var wpLinkRun in wpLink.Descendants<WpRun>())
                        {
                            var linkRun = ImportRun(wpLinkRun);
                            if (linkRun is not null) paragraph.Inlines.Add(linkRun);
                        }
                        break;
                    case WpSdtRun sdtRun:
                        foreach (var wpSdtRun in sdtRun.Descendants<WpRun>())
                        {
                            var sdtText = ImportRun(wpSdtRun);
                            if (sdtText is not null) paragraph.Inlines.Add(sdtText);
                        }
                        break;
                    case WpBookmarkStart or WpBookmarkEnd:
                        break;
                }
            }

            return paragraph;
        }

        private static Run? ImportRun(WpRun wpRun)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var node in wpRun.ChildElements)
            {
                switch (node)
                {
                    case WpText text:
                        sb.Append(text.Text);
                        break;
                    case WpBreak:
                        sb.Append('\n');
                        break;
                    case WpTabChar:
                        sb.Append('\t');
                        break;
                    case WpNoBreakHyphen:
                        sb.Append('-');
                        break;
                    case WpSoftHyphen:
                        break;
                }
            }

            var content = sb.ToString();
            if (content.Length == 0 && wpRun.RunProperties is null) return null;

            var run = new Run { Text = content };
            var rPr = wpRun.RunProperties;
            if (rPr is null) return content.Length > 0 ? run : null;

            if (rPr.Bold is not null) run.FontWeight = FontWeights.Bold;
            if (rPr.Italic is not null) run.FontStyle = FontStyles.Italic;

            var decorations = new TextDecorationCollection();
            if (rPr.Underline is not null)
                foreach (var d in TextDecorations.Underline) decorations.Add(d);
            if (rPr.Strike is not null || rPr.DoubleStrike is not null)
                foreach (var d in TextDecorations.Strikethrough) decorations.Add(d);
            if (decorations.Count > 0) run.SetValue(Inline.TextDecorationsProperty, decorations);

            var vAlign = rPr.VerticalTextAlignment?.Val?.ToString();
            run.BaselineAlignment = vAlign == "superscript" ? BaselineAlignment.Superscript
                : vAlign == "subscript" ? BaselineAlignment.Subscript
                : BaselineAlignment.Baseline;

            var fontSize = rPr.FontSize?.Val?.Value;
            if (fontSize is not null && int.TryParse(fontSize, out var halfPoints) && halfPoints > 0)
                run.FontSize = halfPoints / 2.0;

            var font = rPr.RunFonts?.Ascii?.Value ?? rPr.RunFonts?.HighAnsi?.Value;
            if (!string.IsNullOrEmpty(font))
            {
                try { run.FontFamily = new FontFamily(font); } catch { /* unknown font name */ }
            }

            var color = rPr.Color?.Val?.Value;
            if (!string.IsNullOrEmpty(color) && color != "auto")
            {
                try
                {
                    var converted = ColorConverter.ConvertFromString("#" + color);
                    if (converted is Color c) run.Foreground = new SolidColorBrush(c);
                }
                catch { /* invalid color value */ }
            }

            return run;
        }

        // ---------- Shared inline enumeration ----------

        internal static IEnumerable<Inline> EnumerateRuns(IEnumerable<Inline> inlines)
        {
            foreach (var inline in inlines)
            {
                yield return inline;
                if (inline is Span span)
                    foreach (var nested in EnumerateRuns(span.Inlines))
                        yield return nested;
                else if (inline is Hyperlink hyperlink)
                    foreach (var nested in EnumerateRuns(hyperlink.Inlines))
                        yield return nested;
            }
        }
    }
}
