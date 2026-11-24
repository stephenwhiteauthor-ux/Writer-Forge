# WriterForge

**A word processor built for writers — not office workers.**

WriterForge is a standalone writing application for fiction and non-fiction
authors. It aims to deliver the ~80% of Microsoft Word that writers actually
use — distraction-free prose editing, styles, statistics, spell and grammar
checking, and clean DOCX/PDF export — without the bloat, cost, or
business-oriented feature creep.

> **Status: early development (pre-alpha).** The foundation is in place; the
> feature set is being built out per the spec in [`docs/`](docs/). Feedback,
> criticism, and feature requests are welcome — this project is being built
> in the open.

## Planned feature set

- Document management: new/open/save/save-as, autosave, backups, version history
- Formats: DOCX import/export, PDF export, RTF, TXT (native `.wforge` package planned)
- Formatting: fonts, colours, bold/italic/underline/strikethrough, alignment,
  spacing, indents, lists, tabs
- Styles: built-in manuscript styles (Normal, Headings, Quote, Caption) + custom styles
- Writing statistics: word/character/paragraph/sentence counts, reading & speaking time
- Writer analysis: repeated words, sentence starters, adverbs, passive voice, overused words
- Typography tools: smart quotes, ellipsis normalisation, whitespace cleanup
- Spell check (Hunspell dictionaries) and grammar check (LanguageTool)
- Find & replace including regular expressions
- Macro recorder for repetitive editing tasks
- Print & PDF export (QuestPDF)

Deliberately excluded: mail merge, VBA, forms, labels, envelopes, SharePoint,
corporate workflows. See [`docs/WordProcessor-Spec-v1.0.md`](docs/WordProcessor-Spec-v1.0.md)
for the full specification and [`docs/REVIEW.md`](docs/REVIEW.md) for the
architecture decisions and build order.

## Tech stack

- **.NET 10 / WPF** — no third-party editor controls, no paid components
- **DocumentFormat.OpenXml** — DOCX read/write
- **QuestPDF** — PDF generation (planned)
- **WeCantSpell.Hunspell** — spell checking
- **LanguageTool** — grammar checking

## Build

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build WriterForge.slnx
dotnet run --project src/WriterForge.App
```

## Repository layout

```
src/WriterForge.App      Thin WPF application shell
src/WriterForge.Editor   Class library — the embeddable editor control
docs/                    Product spec and architecture review
```

`WriterForge.Editor` is intentionally a separate class library so the editor
control can be embedded in `WriterForge.App` today and reused or hosted by
other applications in the future.

## License

MIT — see [LICENSE](LICENSE). Free to use, modify, and distribute.
