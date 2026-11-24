# WriterForge Word Processor — Specification v1.0

> Source of truth for the WriterForge standalone application.
> Captured verbatim from the project proposal. See `docs/REVIEW.md` for the
> architectural review and recommended phasing that accompanies this spec.

## OBJECTIVE

Create a professional word processor aimed at fiction and non-fiction writers
that delivers approximately 70–80% of the functionality most authors use in
Microsoft Word whilst remaining completely independent of expensive commercial
editor components.

## TECHNOLOGY STACK

- **Core Editor:** WPF RichTextBox / FlowDocument
- **NuGet Packages:** DocumentFormat.OpenXml, QuestPDF, NHunspell (or WeCantSpell.Hunspell), SixLabors.ImageSharp
- **NO COMMERCIAL COMPONENTS. NO PAID LICENCES. NO DEPENDENCY ON THIRD-PARTY WORD PROCESSOR CONTROLS.**

---

## 1. DOCUMENT MANAGEMENT

**Features:** New Document, Open Document, Save Document, Save As, Recent Files, Auto Save, Backup Creation, Version History, Document Properties.

**Supported Formats:** WForge Package (.wforge), DOCX Import, DOCX Export, PDF Export, RTF Export, TXT Export.

## 2. TEXT FORMATTING

**Font Controls:** Font Family, Font Size, Font Colour, Highlight Colour.

**Text Styling:** Bold, Italic, Underline, Strikethrough, Superscript, Subscript.

**Paragraph Formatting:** Left Align, Centre Align, Right Align, Justify, Line Spacing, Paragraph Spacing Above/Below, Increase Indent, Decrease Indent.

**Lists:** Bullets, Numbering, Multi-level Lists.

**Tabs:** Left Tab, Centre Tab, Right Tab.

## 3. STYLES SYSTEM

**Built-in Styles:** Normal, Heading 1, Heading 2, Heading 3, Quote, Caption, Footnote, Endnote.

**Style Features:** Create Style, Modify Style, Apply Style, Update Style Automatically.

## 4. PAGE LAYOUT

**Page Features:** Margins, Portrait, Landscape, Custom Page Size, Page Break, Section Break.

**Headers:** Add Header, Edit Header, Different First Page, Different Odd/Even Pages.

**Footers:** Add Footer, Edit Footer.

**Page Numbering:** Automatic Numbering, Restart Numbering, Custom Start Number.

## 5. NON-FICTION SUPPORT

**Footnotes:** Insert, Automatic Numbering, Renumber Automatically, Edit, Delete.

**Endnotes:** Insert, Automatic Numbering, Convert Footnotes ↔ Endnotes.

**References:** Internal Bookmarks, Hyperlinks, Cross References.

**Table of Contents:** Automatic Generation, Automatic Update, Based on Heading Styles.

## 6. TABLE SUPPORT

**Insert:** Table, Row, Column.

**Editing:** Merge Cells, Split Cells, Delete Row, Delete Column.

**Formatting:** Borders, Cell Colour, Alignment.

## 7. IMAGE SUPPORT

**Insert:** PNG, JPG, JPEG, GIF, BMP, WEBP.

**Features:** Resize, Move, Align Left/Center/Right.

**Captions:** Add Caption, Edit Caption Text.

**Text Wrapping:** Inline, Square, Tight, Above and Below.

## 8. TEXT BOXES

Insert Text Box. **Features:** Resize, Move, Border Colour, Fill Colour, Transparency.

## 9. SEARCH SYSTEM

**Search:** Find, Find Next, Find Previous.

**Replace:** Replace, Replace All.

**Advanced:** Case Sensitive, Whole Word, Regular Expressions.

## 10. SPELL CHECKING

Real-time Spell Check, Custom Dictionary, Ignore Word, Add Word, Language Selection.

## 11. WRITING STATISTICS

**Document Statistics:** Word Count, Character Count, Character Count Without Spaces, Paragraph Count, Sentence Count, Page Count, Reading Time, Speaking Time.

**Project Statistics:** Chapter Word Count, Scene Word Count, Daily Word Count, Writing Target Progress.

## 12. WRITER ANALYSIS TOOLS

**Repeated Word Detection:** Consecutive Repeated Words, Excessive Repetition.

**Paragraph Analysis:** Same Starting Word, Same Starting Phrase.

**Sentence Analysis:** Repeated Sentence Starters, Sentence Length Statistics.

**Character Analysis:** Character Name Frequency, Character Mention Tracking.

**Writing Style:** Adverb Detection, Passive Voice Detection, Overused Words.

## 13. TYPOGRAPHY TOOLS

- Convert Straight Quotes: `"Text"` → `“Text”`
- Convert Apostrophes: `'word'` → `‘word’`
- Remove Double/Triple Spaces
- Remove Empty Paragraphs
- Normalise Ellipsis
- Normalise Punctuation

## 14. MACRO SYSTEM

**Prebuilt Macros:** Highlight Repeated Paragraph/Sentence Starts, Highlight Repeated Words, Convert Quotes, Remove Double Spaces, Remove Empty Paragraphs, Find Duplicate Paragraphs, Passive Voice Analysis, Adverb Analysis.

**Custom Macros:** Create, Save, Load, Run, Export, Import.

**Future:** C# Script-Based Macro Engine.

## 15. PRINTING

Print, Print Preview, Page Range, Selection Only, Multiple Copies.

## 16. FUTURE INTEGRATIONS

Character Database Integration (Link Character, View Profile), Location Database Integration, Research Integration, Timeline Integration, Project Integration (Chapter Manager, Scene Manager).

## 17. FUTURE VERSION 2 FEATURES

Track Changes, Comments, Collaboration, AI Assisted Editing, Grammar Suggestions, Citation Management, Template System, Split Screen Editing, Focus Mode.

## 18. FEATURES DELIBERATELY EXCLUDED

Mail Merge, VBA Compatibility, SharePoint Integration, Labels, Envelopes, Forms, XML Mapping, Office Automation, Corporate Workflows.

**RATIONALE:** WriterForge is a writer's platform, not an office productivity suite. Development effort should focus on Writing, Editing, Research, Project Management, and Manuscript Production rather than business-oriented Microsoft Word features.
