# ETL-SQL Help and Snippet Formatting Standards

This document defines formatting and style standards for editor help files (markdown-based
tooltips) and autocomplete snippets within the ETL-SQL ecosystem.

All contributions and edits must follow these guidelines to ensure consistency, prevent layout
rendering issues, and provide a premium user experience across all IDE/editor environments
(VS Code, terminal IDE, etc.).

---

## 1. Page-Ownership Contract

Every documentation page has exactly one type. The type determines what the page owns, what
it only links to, and which sections are required. This is not optional: a page that restates
syntax owned by a focused reference page creates a competing owner and will require future
work to reconcile.

The authoritative ownership table lives in
[docs/README.md — Page-Ownership Contract](../../README.md#page-ownership-contract).
The templates under `docs/templates/` each include a page-type ownership header that
summarizes the same contract for quick reference while authoring.

### 1.1 Required Sections by Type

Deviate from required sections only when the page genuinely has nothing to say in that
section — never to make the page shorter.

| Page type | Required sections |
| :--- | :--- |
| Reference — function | Syntax, Parameters, Returns, Null Behavior, Example, References |
| Reference — statement | Syntax, Semantics, Examples, Guardrails or Errors, References |
| Reference — connector | Syntax, Required Options, Authentication (both patterns), Mutually Exclusive Options, Security Notes, Examples, Troubleshooting, References |
| Reference — visual | Syntax, Mappings, Options, Actions, Example, Common Failures, FAQ, References |
| Reference — CLI command | Synopsis, Arguments, Options, Exit Codes, Examples, Notes, References |
| Reference — configuration | Settings, Details, Example, Security Notes, References |
| Hub / README | Topic table or list, Common Tasks, See Also |
| Guide | Audience, Prerequisites, Workflow, Validation, Related Reference |
| Cookbook recipe | Goal, Requirements, Complete Script, Validation, Cleanup, Operational Notes |
| Architecture | Purpose, Components, Data Flow, Contracts, Security And Reliability, Extension Points, Tests, References |
| ADR / Decision record | Status, Date, Context, Decision, Consequences, Alternatives Considered, Validation, References |
| Index | Opening description, grouped table(s), See Also |

### 1.2 Migration Rule — `docs/reference/**`

`docs/reference/**` pages are embedded directly into the engine build as the runtime help
corpus (CLI `help`, LSP hover tooltips, and autocomplete). Moving or renaming a file changes
or removes a help keyword. When a reference page moves or is renamed:

1. Update the `.csproj` `<EmbeddedResource Include="..." Link="..." />` entry or its glob.
2. Update any `LanguageMetadata` / `LanguageService` keyword mapping that resolves the old
   filename.
3. Update every inbound link in `docs/`, `snippets/`, and source code comments in the same
   change.
4. Verify `dotnet build` (embed globs resolve) and
   `node scripts/audit-syntax-index.js --strict` both pass before merging.

Moving files **within** `functions/**`, `statements/**`, or `connectors/**` is safe
(recursive globs cover them). Moving outside those trees requires an explicit glob update.

`guides/**` is **not** build-embedded — those files can be freely split, renamed, or deleted
(fix inbound links only).

---

## 2. Editor Help Documents (.md)

Help documents located under `docs/reference/` are loaded dynamically by the engine and LSP
server to serve hover tooltips and in-editor help.

Because editor hover viewports are narrow and automatically collapse single-newline paragraphs,
all help documents must adhere to these consistency rules:

### 2.1 Document Structure

Every help document must follow this exact layout:

1. **Title Header** — Start with a level-1 header `# TOPIC` matching the exact keyword or
   visual type (e.g., `# PAGE` or `# TABLE`).
2. **Description Paragraph** — A short, single-paragraph description immediately following
   the title header, explaining what the command or feature does.
3. **Syntax Block** — The statement syntax MUST be placed inside a code-fenced block
   ` ```sql ... ``` ` to prevent line collapsing and preserve structure, indentation, and
   casing.
4. **Lists and Options** — All lists of types, mappings, configuration options, or actions
   MUST be formatted as Markdown bullet points (`- **OptionName** — Description`).
   - Never use raw leading-space indentation (e.g., `  PAGE_SIZE — rows per page`) as it
     collapses into a single paragraph in editor hover cards.
   - Bold the option/parameter name (e.g., `- **PAGE_SIZE = n** — ...`).
5. **Examples** — Provide one or two clean, copy-pasteable example blocks using
   ` ```sql ... ``` ` to illustrate common use cases.
6. **References** — Always end the document with a `References` section pointing to the
   official manuals or specifications (e.g.,
   `- [Report SQL Guide](../../guides/feature-guides/report-sql.md)`).

---

## 3. Autocomplete Snippets (.md)

Snippet files located under `snippets/` are used by the LSP server to generate autocomplete
templates. They must adhere to this exact structure:

### 3.1 Frontmatter

Every snippet file must start with a YAML frontmatter delimited by `---` containing exactly:

- `trigger` — The autocomplete trigger keyword, which MUST start with a `$`
  (e.g., `trigger: $dataset`).
- `label` — A short visual title of what the snippet creates
  (e.g., `label: CREATE DATASET &name`).
- `description` — A brief description shown in the autocomplete suggestion list.

### 3.2 Placeholder Syntax

Use French quotation marks `«` and `»` around placeholder/tabstop names
(e.g., `«VisualName»`, `«1h»`). These allow the editor to cycle through fields using `Tab`.

### 3.3 Formatting

Indent block statements using 2 spaces for SQL clauses, options, or mappings. Keep them
clean, valid, and consistent with core syntax.

---

## Compliance Checklist

When adding or editing help or snippet files:

- [ ] Page type is identified and matches its template.
- [ ] Page owns only what its type is allowed to own (see §1 above).
- [ ] All required sections for the page type are present.
- [ ] Help title uses `# TOPIC` format.
- [ ] Help syntax is enclosed in ` ```sql ... ``` ` blocks.
- [ ] No raw leading-space indented lists exist in markdown files
      (all lists use `- **Term** — Description`).
- [ ] Autocomplete snippet trigger starts with `$`.
- [ ] Autocomplete snippet uses `«` and `»` for placeholders.
- [ ] If a `docs/reference/**` page was moved or renamed, the migration rule in §1.2 was
      followed and `dotnet build` + `audit-syntax-index.js --strict` both pass.
