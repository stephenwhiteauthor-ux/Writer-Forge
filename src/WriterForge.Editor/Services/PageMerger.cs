using System.Windows.Documents;

namespace WriterForge.Editor.Services
{
    /// <summary>
    /// Merges edited page-view slice documents back into a single master
    /// FlowDocument. Slice paragraphs carry their source ordinal; fragments
    /// of the same source paragraph are concatenated in page order, and list
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

            // Gather fragments per source ordinal, in encounter order.
            var fragments = new Dictionary<int, List<Paragraph>>();
            var ordinalOrder = new List<int>();
            foreach (var pageDoc in pageDocs)
            foreach (var block in pageDoc.Blocks)
            {
                if (block is not Paragraph frag) continue;
                var ordinal = EditorTags.GetSourceOrdinal(frag);
                if (ordinal < 0 || ordinal >= sourceParas.Count) continue;
                if (!fragments.TryGetValue(ordinal, out var list))
                {
                    list = new List<Paragraph>();
                    fragments[ordinal] = list;
                    ordinalOrder.Add(ordinal);
                }
                list.Add(frag);
            }

            // Rebuild one paragraph per ordinal.
            var merged = new List<(Paragraph Para, int Ordinal)>();
            var covered = new HashSet<int>();
            foreach (var ordinal in ordinalOrder)
            {
                var frags = fragments[ordinal];
                var first = frags[0];
                var para = DocumentCopier.Copy(first);
                for (var i = 1; i < frags.Count; i++)
                    MoveInlines(frags[i], para);
                merged.Add((para, ordinal));
                covered.Add(ordinal);
            }

            // Safety net: source paragraphs that produced no slice (shouldn't
            // happen) are appended whole rather than lost.
            for (var i = 0; i < sourceParas.Count; i++)
            {
                if (covered.Contains(i)) continue;
                if (EditorTags.GetStyleKey(sourceParas[i]) == FootnoteService.FootnoteStyleKey) continue;
                merged.Add((DocumentCopier.Copy(sourceParas[i]), i));
            }

            // Rebuild list wrappers from source structure.
            var result = new FlowDocument
            {
                PageWidth = source.PageWidth,
                PageHeight = source.PageHeight,
                PagePadding = source.PagePadding,
            };
            EmitBlocks(merged, sourceParas, result);

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

        private static void EmitBlocks(
            List<(Paragraph Para, int Ordinal)> merged,
            List<Paragraph> sourceParas, FlowDocument result)
        {
            List? currentList = null;
            ListItem? currentItem = null;

            void CloseList()
            {
                currentItem = null;
                currentList = null;
            }

            foreach (var (para, ordinal) in merged)
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
                    result.Blocks.Add(para);
                }
                else
                {
                    if (currentItem is null || !ReferenceEquals(item, currentItem))
                    {
                        currentItem = new ListItem();
                        currentList.ListItems.Add(currentItem);
                    }
                    currentItem.Blocks.Add(para);
                }
            }
        }
    }
}
