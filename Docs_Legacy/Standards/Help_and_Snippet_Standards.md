# ETL-SQL Help and Snippet Formatting Standards

This document defines the formatting and style standards for editor help files (markdown-based tooltips) and autocomplete snippets within the ETL-SQL ecosystem. 

All contributions and edits must follow these guidelines to ensure consistency, prevent layout rendering issues, and provide a premium user experience across all IDE/editor environments (VS Code, terminal IDE, etc.).

---

## 1. Editor Help Documents (.md)

Help documents located under `src/ETL-SQL.Core/Resources/Help/` are loaded dynamically by the engine and LSP server to serve hover tooltips and in-editor help. 

Because editor hover viewports are narrow and automatically collapse single-newline paragraphs, all help documents must adhere to these consistency rules:

### 1.1 Document Structure

Every help document must follow this exact layout:

1. **Title Header**: Start with a level-1 header `# TOPIC` matching the exact keyword or visual type (e.g., `# PAGE` or `# TABLE`).
2. **Description Paragraph**: A short, single-paragraph description immediately following the title header, explaining what the command or feature does.
3. **Syntax Block**: The statement syntax MUST be placed inside a code-fenced block ` ```sql ... ``` ` to prevent line collapsing and preserve structure, indentation, and casing.
4. **Lists and Options**: All lists of types, mappings, configuration options, or actions MUST be formatted as Markdown bullet points (`- **OptionName** — Description`).
   - Never use raw leading-space indentation (e.g., `  PAGE_SIZE — rows per page`) as it collapses into a single paragraph in editor hover cards.
   - Bold the option/parameter name (e.g., `- **PAGE_SIZE = n** — ...`).
5. **Examples**: Provide one or two clean, copy-pasteable example blocks using ` ```sql ... ``` ` to illustrate common use cases.
6. **References**: Always end the document with a `References` section pointing to the official manuals or specifications (e.g., `- [Report SQL Guide](../../../../../Docs/Report_SQL_Guide.md)`).

---

## 2. Autocomplete Snippets (.md)

Snippet files located under `src/ETL-SQL.Core/Resources/Help/Snippets/` are used by the LSP server to generate autocomplete templates. They must adhere to this exact structure:

### 2.1 Frontmatter

Every snippet file must start with a YAML frontmatter delimited by `---` containing exactly:
- `trigger`: The autocomplete trigger keyword, which MUST start with a `$` (e.g., `trigger: $dataset`).
- `label`: A short visual title of what the snippet creates (e.g., `label: CREATE DATASET &name`).
- `description`: A brief description shown in the autocomplete suggestion list.

### 2.2 Placeholder Syntax

Use French quotation marks `«` and `»` around placeholder/tabstop names (e.g., `«VisualName»`, `«1h»`). These allow the editor to cycle through fields using `Tab`.

### 2.3 Formatting

Indent block statements using 2 spaces for SQL clauses, options, or mappings. Keep them clean, valid, and consistent with core syntax.

---

## Compliance Checklist

When adding or editing help or snippet files:

- [ ] Help title uses `# TOPIC` format.
- [ ] Help syntax is enclosed in ` ```sql ... ``` ` blocks.
- [ ] No raw leading-space indented lists exist in markdown files (all lists use `- **Term** — Description`).
- [ ] Autocomplete snippet trigger starts with `$`.
- [ ] Autocomplete snippet uses `«` and `»` for placeholders.
