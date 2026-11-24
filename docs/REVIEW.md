# WriterForge — Architecture Review & Phasing

Companion to `WordProcessor-Spec-v1.0.md`. Captures the design decisions made
before implementation started.

## Key decisions

1. **Standalone WPF app, editor in a class library.**
   `WriterForge.Editor` is a class library so the editor control can be hosted
   inside other applications in the future (WPF directly, or via ElementHost).
   `WriterForge.App`
   is a thin shell. Do not let features leak into the App project — everything
   document-related belongs in `WriterForge.Editor` or future `WriterForge.*`
   libraries.

2. **Build services UI-agnostic from the start.** Services that don't depend
   on WPF should live in `WriterForge.Editor` (or future `WriterForge.*`
   libraries) so they can be tested and reused in isolation:
   - `SpellChecker` (WeCantSpell.Hunspell + en_US dictionaries)
   - `GrammarCheckService` (LanguageTool, public API + REST fallback,
     `LanguageToolUrl` app setting)
   - `ThemeManager` (light/dark)
   - `DocumentFormat.OpenXml` for DOCX

3. **Canonical format TBD — decide before v1 ships.** Options:
   - Self-contained `.wforge` ZIP package (DOCX-derived container + assets)
   - Plain text/RTF with sidecar metadata
   - DOCX as canonical (simplest interop, but locks you into Word's model)
   Recommendation: ZIP package. A future project-management layer could map
   documents onto scene/chapter records.

4. **Scope cuts for v1 (agreed direction):**
   - DEFER: real page layout (margins/headers/footers/page numbers/sections),
     tables, text boxes, text wrapping, footnotes/endnotes/TOC/cross-references,
     track changes, comments, script-based macros.
   - SHIP in v1: prose editing, basic character/paragraph formatting,
     styles-as-presets, lists, find/replace (incl. regex), writing stats,
     typography cleanup tools, spell + grammar check, DOCX import/export,
     autosave, PDF export (QuestPDF), macro *recorder* (not script engine).

5. **Performance requirement:** RichTextBox/FlowDocument degrades on very long
   documents. Design for per-chapter/per-scene documents or chunked loading —
   do not attempt a single 200k-word FlowDocument.

## Suggested build order (MVP)

1. Editor surface: formatting bar, font/style controls, undo/redo
2. Document model + `.wforge` (or interim `.rtf`/`.txt`) load/save + autosave
3. Stats + typography tools (cheap, high value)
4. Spell/grammar integration (port services)
5. DOCX import/export via OpenXML
6. Find/replace, lists, images (inline only)
7. Print/PDF (QuestPDF), macro recorder
8. Embedding/host integration for future applications

## Open questions

- Does WriterForge need its own project/database, or are documents always
  standalone files?
- Which .wforge container layout (DOCX clone vs custom XML)?
- Image handling: insert/resize only, or also cropping?
