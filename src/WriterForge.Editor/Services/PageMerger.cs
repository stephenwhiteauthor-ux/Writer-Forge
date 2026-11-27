using System.Windows.Documents;

namespace WriterForge.Editor.Services
{
    /// <summary>
    /// Merges edited page-view slice documents back into a single master
    /// FlowDocument. Slice paragraphs carry their source ordinal; fragments
    /// of the same source paragraph are concatenated in page order. Tables
    /// are atomic — each slice replaces its source table wholesale. List
    /// membership is reconstructed from the source document.
    /// </summary>
    internal static class PageMerger
    {
        /// <summary>
        /// Merge page body documents (in page order) into a document matching
        /// the source structure. Footnote paragraphs (excluded from page flow)
        /// are appended at the end.
        /// </summary>
        public static FlowDocument Merge(
            IReadOnlyList<FlowDocument> pageDocs, FlowDocument source)
        {
            var sourceParas = BlockTree.AllParagraphs(source).ToList();
            var sourceTables = BlockTree.FlowTables(source);

            // Gather paragraph fragments per source ordinal and table slices
            // per table ordinal, recording the interleaved document order.
            var paraFrags = new Dictionary<int, List<Paragraph>>();
            var tableSlices = new Dictionary<int, Table>();
            var seq = new List<(bool IsTable, int Ordinal)>();
            var seenParas = new HashSet<int>();
            var seenTables = new HashSet<int>();

            foreach (var pageDoc in pageDocs)
            foreach (var block in pageDoc.Blocks)
            {
                switch (block)
                {
                    case Paragraph frag:
                    {
                        var ord = EditorTags.GetSourceOrdinal(frag);
                        if (ord < 0 || ord >= sourceParas.Count) break;
                        if (seenParas.Add(ord)) seq.Add((false, ord));
                        if (!paraFrags.TryGetValue(ord, out var list))
                            paraFrags[ord] = list = new List<Paragraph>();
                        list.Add(frag);
                        break;
                    }
                    case Table table:
                    {
                        var ord = EditorTags.GetSourceOrdinal(table);
                        if (ord < 0 || ord >= sourceTables.Count || !seenTables.Add(ord)) break;
                        seq.Add((true, ord));
                        tableSlices[ord] = table;
                        break;
                    }
                }
            }

            var result = new FlowDocument
            {
                PageWidth = source.PageWidth,
                PageHeight = source.PageHeight,
                PagePadding = source.PagePadding,
            };

            List? currentList = null;
            ListItem? currentItem = null;

            void CloseList()
            {
                currentItem = null;
                currentList = null;
            }

            void EmitTable(Table slice)
            {
                CloseList();
                result.Blocks.Add(DocumentCopier.Copy(slice));
            }

            void EmitParagraph(Paragraph merged, int ordinal)
            {
                var sourcePara = sourceParas[ordinal];
                var list = BlockTree.ParentList(sourcePara);
                var item = sourcePara.Parent as ListItem;

                if (!ReferenceEquals(list, currentList))
                {
                    CloseList();
                    if (list is not null)
                    {
                        currentList = new List { MarkerStyle = list.MarkerStyle, StartIndex = list.StartIndex };
                        result.Blocks.Add(currentList);
                    }
                }

                if (currentList is null)
                {
                    result.Blocks.Add(merged);
                }
                else
                {
                    if (currentItem is null || !ReferenceEquals(item, currentItem))
                    {
                        currentItem = new ListItem();
                        currentList.ListItems.Add(currentItem);
                    }
                    currentItem.Blocks.Add(merged);
                }
            }

            Paragraph MergeParagraphFragments(int ordinal)
            {
                var frags = paraFrags[ordinal];
                var para = DocumentCopier.Copy(frags[0]);
                for (var i = 1; i < frags.Count; i++)
                    MoveInlines(frags[i], para);
                return para;
            }

            foreach (var (isTable, ordinal) in seq)
            {
                if (isTable)
                {
                    if (tableSlices.TryGetValue(ordinal, out var slice)) EmitTable(slice);
                }
                else
                {
                    EmitParagraph(MergeParagraphFragments(ordinal), ordinal);
                }
            }

            // Safety net: source content that produced no slice is appended
            // whole rather than lost (shouldn't happen in practice).
            for (var i = 0; i < sourceParas.Count; i++)
            {
                if (seenParas.Contains(i)) continue;
                if (StyleKeys.IsNoteStyle(EditorTags.GetStyleKey(sourceParas[i]))) continue;
                EmitParagraph(DocumentCopier.Copy(sourceParas[i]), i);
            }
            for (var i = 0; i < sourceTables.Count; i++)
                if (!seenTables.Contains(i)) EmitTable(sourceTables[i]);

            // Footnote paragraphs live outside the page flow — restore at end.
            foreach (var p in sourceParas)
                if (EditorTags.GetStyleKey(p) == FootnoteService.FootnoteStyleKey)
                    result.Blocks.Add(DocumentCopier.Copy(p));

            return result;
        }

        private static void MoveInlines(Paragraph from, Paragraph to)
        {
            foreach (var inline in from.Inlines.ToList())
            {
                from.Inlines.Remove(inline);
                to.Inlines.Add(inline);
            }
        }
    }
}
