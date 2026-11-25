using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace WriterForge.Editor.Services
{
    /// <summary>
    /// Attached property recording which built-in paragraph style a paragraph
    /// uses ("Heading1", "Quote", ...). Set when a style is applied from the
    /// toolbar; read by the DOCX exporter/importer.
    /// </summary>
    public static class EditorTags
    {
        public static readonly DependencyProperty StyleKeyProperty =
            DependencyProperty.RegisterAttached(
                "StyleKey",
                typeof(string),
                typeof(EditorTags),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

        public static void SetStyleKey(DependencyObject element, string? value) =>
            element.SetValue(StyleKeyProperty, value);

        public static string? GetStyleKey(DependencyObject element) =>
            element.GetValue(StyleKeyProperty) as string;
    }

    /// <summary>Shared brushes/decoration helpers for spell and grammar proof underlines.</summary>
    internal static class ProofDecorations
    {
        public static readonly SolidColorBrush SpellBrush = CreateBrush(0xB0, 0x00, 0x00);
        public static readonly SolidColorBrush GrammarBrush = CreateBrush(0x1A, 0x66, 0xCC);

        private static SolidColorBrush CreateBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        public static bool IsProof(TextDecoration decoration, SolidColorBrush brush) =>
            decoration.Pen is not null && ReferenceEquals(decoration.Pen.Brush, brush);

        public static bool IsProof(TextDecoration decoration) =>
            IsProof(decoration, SpellBrush) || IsProof(decoration, GrammarBrush);
    }
}
