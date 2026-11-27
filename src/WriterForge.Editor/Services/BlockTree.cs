using System.Windows.Documents;

namespace WriterForge.Editor.Services
{
    /// <summary>
    /// Walks a FlowDocument's block tree (sections, lists, list items)
    /// yielding every Paragraph in document order.
    /// </summary>
    internal static class BlockTree
    {
        public static IEnumerable<Paragraph> AllParagraphs(FlowDocument document) =>
            WalkBlocks(document.Blocks);

        public static IEnumerable<Paragraph> WalkBlocks(IEnumerable<Block> blocks)
        {
            foreach (var block in blocks)
            {
                switch (block)
                {
                    case Paragraph paragraph:
                        yield return paragraph;
                        break;
                    case Section section:
                        foreach (var p in WalkBlocks(section.Blocks))
                            yield return p;
                        break;
                    case List list:
                        foreach (var item in list.ListItems)
                            foreach (var p in WalkBlocks(item.Blocks))
                                yield return p;
                        break;
                }
            }
        }

        /// <summary>The List ancestor of a paragraph, or null.</summary>
        public static List? ParentList(Paragraph paragraph)
        {
            if (paragraph.Parent is ListItem item && item.Parent is List list)
                return list;
            return null;
        }

        /// <summary>
        /// Top-level flow items in document order: paragraphs and tables,
        /// descending through sections and lists (but not into tables).
        /// </summary>
        public static IEnumerable<Block> FlowBlocks(FlowDocument document) =>
            WalkFlow(document.Blocks);

        /// <summary>All tables that participate in page flow, in document order.</summary>
        public static List<Table> FlowTables(FlowDocument document) =>
            FlowBlocks(document).OfType<Table>().ToList();

        private static IEnumerable<Block> WalkFlow(IEnumerable<Block> blocks)
        {
            foreach (var block in blocks)
            {
                switch (block)
                {
                    case Paragraph:
                    case Table:
                        yield return block;
                        break;
                    case Section section:
                        foreach (var b in WalkFlow(section.Blocks)) yield return b;
                        break;
                    case List list:
                        foreach (var item in list.ListItems)
                        foreach (var b in WalkFlow(item.Blocks)) yield return b;
                        break;
                }
            }
        }

        /// <summary>All paragraphs inside a table's cells, in row-major order.</summary>
        public static IEnumerable<Paragraph> CellParagraphs(Table table)
        {
            foreach (var group in table.RowGroups)
            foreach (TableRow row in group.Rows)
            foreach (TableCell cell in row.Cells)
            foreach (var p in WalkBlocks(cell.Blocks))
                yield return p;
        }
    }
}
