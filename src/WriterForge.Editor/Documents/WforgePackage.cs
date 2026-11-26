using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Documents;

namespace WriterForge.Editor.Documents
{
    /// <summary>Per-paragraph style annotation: which paragraphs carry a named style key.</summary>
    public sealed class ParaStyleRecord
    {
        /// <summary>Index into the flattened paragraph order (BlockTree.AllParagraphs).</summary>
        public int Index { get; set; }
        public string StyleKey { get; set; } = "";
        /// <summary>Footnote/endnote id when the paragraph is a note (0 otherwise).</summary>
        public int FootnoteId { get; set; }
    }

    /// <summary>Footnote marker location: paragraph ordinal + char offset + id.</summary>
    public sealed class FootnoteMarkerRecord
    {
        public int ParagraphIndex { get; set; }
        public int Offset { get; set; }
        public int Id { get; set; }
    }

    /// <summary>Serializable definition of a session custom style.</summary>
    public sealed class CustomStyleRecord
    {
        public string Name { get; set; } = "";
        public string Key { get; set; } = "";
        public string? FontFamily { get; set; }
        public double? FontSize { get; set; }
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public string? Alignment { get; set; }
    }

    /// <summary>Metadata stored alongside the document inside a .wforge package.</summary>
    public sealed class DocumentMetadata
    {
        public string Format { get; set; } = "wforge";
        public int Version { get; set; } = 1;
        public string? Title { get; set; }
        public string? Author { get; set; }
        public string? Header { get; set; }
        public string? Footer { get; set; }
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>Named-style tags per paragraph (RTF doesn't preserve attached properties).</summary>
        public List<ParaStyleRecord>? ParagraphStyles { get; set; }
        /// <summary>Footnote/endnote marker positions.</summary>
        public List<FootnoteMarkerRecord>? FootnoteMarkers { get; set; }
        /// <summary>Session custom paragraph styles.</summary>
        public List<CustomStyleRecord>? CustomStyles { get; set; }
    }

    /// <summary>
    /// The native WriterForge document format: a ZIP package containing the
    /// document body (RTF) and a JSON metadata file.
    /// </summary>
    public static class WforgePackage
    {
        private const string DocumentEntry = "document.rtf";
        private const string MetadataEntry = "metadata.json";

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static void Save(string path, FlowDocument document, DocumentMetadata metadata)
        {
            metadata.ModifiedUtc = DateTimeOffset.UtcNow;

            using var stream = File.Create(path);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

            var documentEntry = archive.CreateEntry(DocumentEntry);
            using (var entryStream = documentEntry.Open())
            {
                var range = new TextRange(document.ContentStart, document.ContentEnd);
                range.Save(entryStream, DataFormats.Rtf);
            }

            var metadataEntry = archive.CreateEntry(MetadataEntry);
            using (var writer = new StreamWriter(metadataEntry.Open()))
            {
                writer.Write(JsonSerializer.Serialize(metadata, JsonOptions));
            }
        }

        public static (FlowDocument Document, DocumentMetadata Metadata) Load(string path)
        {
            using var stream = File.OpenRead(path);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            var documentEntry = archive.GetEntry(DocumentEntry)
                ?? throw new InvalidDataException("Not a WriterForge document: missing document.rtf");

            var document = new FlowDocument();
            using (var entryStream = documentEntry.Open())
            {
                var range = new TextRange(document.ContentStart, document.ContentEnd);
                range.Load(entryStream, DataFormats.Rtf);
            }

            var metadata = new DocumentMetadata();
            var metadataEntry = archive.GetEntry(MetadataEntry);
            if (metadataEntry is not null)
            {
                try
                {
                    using var reader = new StreamReader(metadataEntry.Open());
                    metadata = JsonSerializer.Deserialize<DocumentMetadata>(reader.ReadToEnd(), JsonOptions) ?? metadata;
                }
                catch (JsonException)
                {
                    // Corrupt metadata shouldn't block opening the document.
                }
            }

            return (document, metadata);
        }
    }
}
