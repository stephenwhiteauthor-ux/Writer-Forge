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
    }
}
