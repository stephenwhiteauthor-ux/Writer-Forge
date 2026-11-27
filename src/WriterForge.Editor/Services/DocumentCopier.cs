using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace WriterForge.Editor.Services
{
    /// <summary>
    /// Deep-copies document content preserving every formatting property,
    /// Style reference and editor tag. Unlike an RTF round-trip this loses
    /// nothing — used for measurement clones and page-view slices.
    /// InlineUIContainers can't be reparented, so their child is dropped
    /// (paragraphs containing images are already flagged unreliable).
    /// </summary>
    internal static class DocumentCopier
    {
        public static FlowDocument Copy(FlowDocument source)
        {
            var copy = new FlowDocument
            {
                PageWidth = source.PageWidth,
                PageHeight = source.PageHeight,
                PagePadding = source.PagePadding,
                ColumnWidth = source.ColumnWidth,
                IsColumnWidthFlexible = source.IsColumnWidthFlexible,
                FlowDirection = source.FlowDirection,
            };
            foreach (var block in source.Blocks)
                if (CopyBlock(block) is { } b) copy.Blocks.Add(b);
            return copy;
        }

        public static Paragraph Copy(Paragraph source) => CopyParagraph(source);

        public static Table Copy(Table source) => CopyTable(source);

        public static Paragraph CopyParagraph(Paragraph source)
        {
            var p = new Paragraph();
            CopyParagraphProps(source, p);
            foreach (var inline in source.Inlines)
                if (CopyInline(inline) is { } ci) p.Inlines.Add(ci);
            return p;
        }

        private static void CopyParagraphProps(Paragraph src, Paragraph dst)
        {
            CopyIfSet(src, dst, Paragraph.MarginProperty);
            CopyIfSet(src, dst, Paragraph.TextIndentProperty);
            CopyIfSet(src, dst, Paragraph.TextAlignmentProperty);
            CopyIfSet(src, dst, Paragraph.LineHeightProperty);
            CopyIfSet(src, dst, Paragraph.StyleProperty);
            CopyIfSet(src, dst, TextElement.FontFamilyProperty);
            CopyIfSet(src, dst, TextElement.FontSizeProperty);
            CopyIfSet(src, dst, TextElement.FontWeightProperty);
            CopyIfSet(src, dst, TextElement.FontStyleProperty);
            CopyIfSet(src, dst, TextElement.FontStretchProperty);
            CopyIfSet(src, dst, TextElement.ForegroundProperty);
            CopyIfSet(src, dst, TextElement.BackgroundProperty);
            var styleKey = EditorTags.GetStyleKey(src);
            if (styleKey is not null) EditorTags.SetStyleKey(dst, styleKey);
            var fid = EditorTags.GetFootnoteId(src);
            if (fid != 0) EditorTags.SetFootnoteId(dst, fid);
        }

        private static Inline? CopyInline(Inline src) => src switch
        {
            Run run => CopyRun(run),
            Span span => CopySpan(span),
            LineBreak => new LineBreak(),
            InlineUIContainer => new InlineUIContainer(),
            _ => null,
        };

        private static Run CopyRun(Run src)
        {
            var run = new Run(src.Text);
            CopyTextProps(src, run);
            var fid = EditorTags.GetFootnoteId(src);
            if (fid != 0) EditorTags.SetFootnoteId(run, fid);
            return run;
        }

        private static Span CopySpan(Span src)
        {
            var span = new Span();
            CopyTextProps(src, span);
            foreach (var inline in src.Inlines)
                if (CopyInline(inline) is { } ci) span.Inlines.Add(ci);
            return span;
        }

        private static void CopyTextProps(TextElement src, TextElement dst)
        {
            CopyIfSet(src, dst, TextElement.FontFamilyProperty);
            CopyIfSet(src, dst, TextElement.FontSizeProperty);
            CopyIfSet(src, dst, TextElement.FontWeightProperty);
            CopyIfSet(src, dst, TextElement.FontStyleProperty);
            CopyIfSet(src, dst, TextElement.FontStretchProperty);
            CopyIfSet(src, dst, TextElement.ForegroundProperty);
            CopyIfSet(src, dst, TextElement.BackgroundProperty);
            CopyIfSet(src, dst, Inline.TextDecorationsProperty);
            CopyIfSet(src, dst, Inline.BaselineAlignmentProperty);
        }

        /// <summary>Copy a property only when it has a local value (not inherited/default).</summary>
        private static void CopyIfSet(DependencyObject src, DependencyObject dst, DependencyProperty prop)
        {
            var value = src.ReadLocalValue(prop);
            if (!ReferenceEquals(value, DependencyProperty.UnsetValue) && value is not null)
                dst.SetValue(prop, value);
        }

        /// <summary>Total plain-text length of a paragraph (Run text + 1 per break/object).</summary>
        public static int TextLength(Paragraph paragraph)
        {
            var length = 0;
            foreach (var inline in paragraph.Inlines) length += InlineLength(inline);
            return length;
        }

        private static int InlineLength(Inline inline) => inline switch
        {
            Run run => run.Text.Length,
            LineBreak => 1,
            InlineUIContainer => 1,
            Span span => span.Inlines.Cast<Inline>().Sum(InlineLength),
            _ => 0,
        };

        /// <summary>
        /// Copy a paragraph trimmed to the char range [startChar, endChar).
        /// Continuation slices (startChar &gt; 0) get their top margin removed.
        /// </summary>
        public static Paragraph SliceParagraph(Paragraph source, int startChar, int endChar)
        {
            var copy = CopyParagraph(source);
            TrimInlines(copy, startChar, endChar);
            if (startChar > 0) copy.Margin = new Thickness(copy.Margin.Left, 0, copy.Margin.Right, copy.Margin.Bottom);
            return copy;
        }

        private static void TrimInlines(Paragraph paragraph, int start, int end)
        {
            var kept = new List<Inline>();
            var pos = 0;
            foreach (var inline in paragraph.Inlines.ToList())
                CollectSlice(inline, start, end, ref pos, kept);
            paragraph.Inlines.Clear();
            foreach (var inline in kept) paragraph.Inlines.Add(inline);
        }

        /// <summary>Append the intersection of `inline` with [start,end) to kept; pos tracks global offset.</summary>
        private static void CollectSlice(Inline inline, int start, int end, ref int pos, List<Inline> kept)
        {
            if (inline is Span span)
            {
                var inner = new List<Inline>();
                foreach (var child in span.Inlines)
                    CollectSlice(child, start, end, ref pos, inner);
                if (inner.Count > 0)
                {
                    var slice = new Span();
                    CopyRunProps(span, slice);
                    foreach (var child in inner) slice.Inlines.Add(child);
                    kept.Add(slice);
                }
                return;
            }

            var len = InlineLength(inline);
            var inStart = pos;
            pos += len;
            var from = Math.Max(0, start - inStart);
            var to = Math.Min(len, end - inStart);
            if (to <= from) return;

            switch (inline)
            {
                case Run run:
                {
                    var trimmed = new Run(run.Text.Substring(from, to - from));
                    CopyRunProps(run, trimmed);
                    var fid = EditorTags.GetFootnoteId(run);
                    if (fid != 0) EditorTags.SetFootnoteId(trimmed, fid);
                    kept.Add(trimmed);
                    break;
                }
                case LineBreak:
                    kept.Add(new LineBreak());
                    break;
                case InlineUIContainer:
                    kept.Add(new InlineUIContainer());
                    break;
            }
        }

        private static void CopyRunProps(TextElement src, TextElement dst)
        {
            foreach (var prop in RunProps)
            {
                var v = src.ReadLocalValue(prop);
                if (!ReferenceEquals(v, DependencyProperty.UnsetValue) && v is not null)
                    dst.SetValue(prop, v);
            }
        }

        private static readonly DependencyProperty[] RunProps =
        {
            TextElement.FontFamilyProperty,
            TextElement.FontSizeProperty,
            TextElement.FontWeightProperty,
            TextElement.FontStyleProperty,
            TextElement.FontStretchProperty,
            TextElement.ForegroundProperty,
            TextElement.BackgroundProperty,
            Inline.TextDecorationsProperty,
            Inline.BaselineAlignmentProperty,
        };

        private static Block? CopyBlock(Block src) => src switch
        {
            Paragraph p => CopyParagraph(p),
            Section s => CopySection(s),
            List list => CopyList(list),
            Table table => CopyTable(table),
            _ => null,
        };

        private static Section CopySection(Section src)
        {
            var s = new Section();
            foreach (var block in src.Blocks)
                if (CopyBlock(block) is { } b) s.Blocks.Add(b);
            return s;
        }

        private static List CopyList(List src)
        {
            var list = new List
            {
                MarkerStyle = src.MarkerStyle,
                StartIndex = src.StartIndex,
            };
            foreach (var item in src.ListItems)
            {
                var li = new ListItem();
                foreach (var block in item.Blocks)
                    if (CopyBlock(block) is { } b) li.Blocks.Add(b);
                list.ListItems.Add(li);
            }
            return list;
        }

        private static Table CopyTable(Table src)
        {
            var table = new Table();
            foreach (var group in src.RowGroups)
            {
                var g = new TableRowGroup();
                foreach (TableRow row in group.Rows)
                {
                    var r = new TableRow();
                    foreach (TableCell cell in row.Cells)
                    {
                        var c = new TableCell();
                        foreach (var block in cell.Blocks)
                            if (CopyBlock(block) is { } b) c.Blocks.Add(b);
                        r.Cells.Add(c);
                    }
                    g.Rows.Add(r);
                }
                table.RowGroups.Add(g);
            }
            return table;
        }
    }
}
