# Constrained HTML Visuals

**Status:** Accepted
**Date:** 2026-08-25
**Decision scope:** Template grammar, sanitizer, typed evaluator, interaction projection,
embedded-visual boundaries, budgets, CSS isolation, and portable fallback contract for
`CREATE VISUAL ... AS HTML`.

**Design input:** [`micro-charts-and-html-embedding.md`](micro-charts-and-html-embedding.md)
provides Tier 2 syntax examples. Those examples are design input, not parser contracts — this ADR
is authoritative.

---

## 1. Context

`CREATE VISUAL ... AS CUSTOM` gives script authors a renderer-neutral Grammar-of-Graphics grammar
for composing charts. It owns the graphical layer: marks, encodings, scales, and axes compile to a
typed `ChartSpec` IR rendered by pluggable backends.

Authors also need bespoke **non-chart** presentation components: operational tiles, narrative metric
panels, status cards, row repeaters, infographic layouts, and data-driven badges. Built-in `CARD`,
`TABLE`, `TEXT`, and `IMAGE` cover the most common shapes, but every novel layout would require a
new named visual keyword, which does not scale.

`CREATE VISUAL ... AS HTML` fills this gap with a constrained, renderer-owned template surface.
The template evaluator substitutes escaped data into semantic HTML; the renderer owns the DOM.
No author-supplied JavaScript executes on any host.

### Design constraints

1. **Zero-trust authoring.** Reports are authored by privileged users but rendered for unprivileged
   viewers across browser, PDF, email, terminal, and screen-reader surfaces. Any content the author
   supplies is untrusted from the renderer's perspective.
2. **Deterministic multi-surface parity.** The same template must produce equivalent output in
   browser, PDF, print, and static export. DOM scripting, canvas, and client-side rendering are
   excluded by construction.
3. **Visible SQL.** All calculation, aggregation, lookup, filtering, and data transformation
   remains in visible SQL. The template language is a substitution surface, not a second expression
   or execution engine.
4. **Existing action model.** Interactions use declarative Report-SQL actions (parameter updates,
   navigation, bookmarks, drill, cross-filtering) — never DOM event handlers.

---

## 2. Canonical Syntax

```sql
CREATE VISUAL NodeClusterStatus AS HTML (
  SOURCE   = #cluster_nodes,
  MODE     = REPEATER,
  TEMPLATE = '
    <article class="node-card">
      <h3>{{HostName}}</h3>
      <p>CPU: {{CpuPercent FORMAT ''0.0%''}}</p>
      {{#IF Status = ''Critical''}}
        <span class="badge-critical">CRITICAL</span>
      {{/IF}}
    </article>
  ',
  STYLE (
    CSS = '
      .node-card { padding: 1rem; border: 1px solid var(--etl-border); }
      .badge-critical { color: var(--etl-danger); font-weight: 600; }
    '
  ),
  FALLBACK = 'Cluster node status: {{HostName}} — CPU {{CpuPercent}}',
  ACTIONS (
    ON CLICK SET @selected_node = {{HostName}}
  )
);
```

### 2.1 Statement Shape

```
CREATE [OR ALTER] VISUAL <name> AS HTML (
  [SOURCE = <source-expr>,]
  [MODE = SINGLE | REPEATER,]
  TEMPLATE = '<template-string>',
  [STYLE ( CSS = '<css-string>' ),]
  [FALLBACK = '<fallback-template>',]
  [TITLE = <expr>,]
  [SUBTITLE = <expr>,]
  [TOOLTIP ...,]
  [FETCH = AUTO | ONLOAD | ONRUN,]
  [OPTIONS (...),]
  [ACTIONS (...)]
);
```

| Clause | Required | Default | Description |
|--------|----------|---------|-------------|
| `SOURCE` | No | (none) | Temp table or inline SELECT. When absent, the visual is source-free (parameter-driven or static). |
| `MODE` | No | `SINGLE` | `SINGLE` renders one instance from the first row (or no row). `REPEATER` renders one instance per row. |
| `TEMPLATE` | **Yes** | — | The HTML template string. Substitutions use `{{FieldOrParam}}` syntax. |
| `STYLE` | No | (none) | Visual-scoped CSS. The `CSS` key is required inside the `STYLE (...)` block. |
| `FALLBACK` | No | auto-generated | Concise semantic summary template for non-browser surfaces. |
| `TITLE`, `SUBTITLE`, `TOOLTIP` | No | — | Standard visual clauses, shared with all visual types. |
| `FETCH` | No | `AUTO` | Standard fetch-mode control. |
| `OPTIONS` | No | — | Named key-value options (e.g., `MAX_ROWS`, `CONTAINER_TAG`). |
| `ACTIONS` | No | — | Declarative actions. Same grammar as all other visual types. |

HTML visuals **cannot** use `MAPPINGS`, `SERIES`, `OVERLAYS`, `FORMATTING`, `CHART`, or `CASCADE`
clauses. These belong to chart and control visuals respectively.

### 2.2 Source Combinations

| SOURCE present | MODE | Behavior |
|:-:|:-:|:--|
| No | SINGLE | Static or parameter-only. Template substitutes `@param` references only. |
| No | REPEATER | Parse error: REPEATER requires a SOURCE. |
| Yes | SINGLE | First row of the result set. Template substitutes fields and `@param` references. |
| Yes | REPEATER | One instance per row, capped by the row budget. |

---

## 3. Template Grammar

### 3.1 Substitution Syntax

```
{{FieldName}}                          -- Column from the SOURCE row
{{FieldName FORMAT '<format-spec>'}}   -- Formatted column value
{{@ParamName}}                         -- Report parameter
{{@ParamName FORMAT '<format-spec>'}}  -- Formatted parameter
```

All substitutions produce **HTML-escaped text** by default. There is no raw-output escape hatch.
Format specs use the same format-string syntax as `MAPPINGS ... FORMAT`.

Field names resolve against the source row schema (case-insensitive, unambiguous match required).
Parameter names resolve against declared report parameters. Unknown fields and parameters are
analysis errors (`RPT3001`, `RPT3002`).

### 3.2 Conditional Form

The template supports one conditional construct:

```
{{#IF <field-or-param> <op> <literal>}}
  ...content...
{{/IF}}
```

Supported operators: `=`, `!=`, `<>`, `<`, `>`, `<=`, `>=`, `IS NULL`, `IS NOT NULL`.

Literals are single-quoted strings (`'value'`) or unquoted numbers. Comparison uses the field's
runtime type with the same coercion rules as `WHERE` clauses.

**Nesting:** `{{#IF}}` blocks nest to a maximum depth of 4. Deeper nesting is a parse error.
There is no `{{#ELSE}}` or `{{#ELSEIF}}` — chain non-overlapping `{{#IF}}` blocks instead.
This keeps the template language trivially analyzable and prevents it from becoming a second
expression engine.

**No loops, no expressions, no assignments.** Row iteration is `MODE = REPEATER`; per-row
computation is SQL. The template is a substitution and visibility surface only.

### 3.3 Micro-Chart Helpers

Within an HTML template, authors can invoke server-rendered micro-chart helpers that produce
inline SVG. These helpers compile through the existing GoG `ChartSpec` IR and `PlotPlanSvgRenderer`:

```
{{SPARKLINE(<data-col>, TYPE="LINE|AREA|BAR", COLOR="<hex>", WIDTH=<n>, HEIGHT=<n>)}}
{{PROGRESS_BAR(<value-col>, MIN=<n>, MAX=<n>, COLOR="<hex>", HEIGHT=<n>)}}
{{BG_CHART(<data-col>, TYPE="LINE|AREA|BAR", COLOR="<hex>", OPACITY=<n>)}}
```

Helper output is server-generated declarative SVG — no client-side charting library loads. The SVG
is owned by the renderer and is not author-supplied content, so it bypasses the element allowlist
(it cannot contain `<script>` by construction). Colors are validated as safe hexadecimal paint
values before SVG generation.

### 3.4 Embedded Visual References

```
{{VISUAL(<visual-name> [, PARAMETERS(@p1 = <field-or-value>, ...)])}}
```

Embeds a declared report visual (any type) inline. The referenced visual is resolved statically at
analysis time. Restrictions:

- The target must be a declared visual in the same report.
- Self-references, cycles, and recursive nesting are rejected (`RPT3010`).
- Maximum embedding depth: 2 (an HTML visual may embed a visual, but that embedded visual may not
  itself embed another HTML visual).
- Each embedded visual's query, row, node, and byte costs are summed against the report's aggregate
  budgets.
- The embedded visual reuses its own manifest and actions — it does not instantiate a separate
  chart runtime.

---

## 4. Threat Model

### 4.1 Threat Actors and Attack Surface

| Actor | Capability | Goal |
|-------|-----------|------|
| Malicious report author | Can write `.rptsql` scripts with arbitrary TEMPLATE and CSS strings | XSS, data exfiltration, phishing, defacement, session hijacking |
| Compromised data source | Can supply column values containing hostile markup | Stored XSS, content injection |
| Viewer | Can interact with rendered visuals via normal browser controls | Not an attacker — but their browser is the target |

The attack surface is the TEMPLATE string, the CSS string, field values substituted at runtime, and
parameter values supplied by viewers or bookmarks.

### 4.2 Threat Catalog

| ID | Threat | Vector | Mitigation | Section |
|----|--------|--------|------------|---------|
| T-1 | Script injection (XSS) | `<script>` tag in template or data | Element allowlist rejects `<script>`. All substitutions HTML-encode. No raw-output escape hatch. | §5.1, §5.2 |
| T-2 | Event handler injection | `onload`, `onerror`, `onclick` in template or data | Attribute allowlist rejects all `on*` attributes. | §5.3 |
| T-3 | JavaScript URL injection | `href="javascript:..."`, `src="data:text/html,..."` | URL policy rejects `javascript:`, `data:` (except `data:image/*`), `vbscript:`, `blob:`. | §5.4 |
| T-4 | CSS exfiltration | `background: url(https://evil/...)`, `@import`, `@font-face` | CSS sanitizer rejects `url()` with external hosts, `@import`, `@font-face`, and `expression()`. Only `var(--etl-*)` custom properties pass. | §5.5 |
| T-5 | Iframe/embed escape | `<iframe>`, `<object>`, `<embed>`, `<applet>`, `<form>` with external action | Element allowlist rejects all frame, plugin, and external-submit elements. | §5.1 |
| T-6 | DOM mutation | `<base>`, `<meta http-equiv>`, `<link>` | Element allowlist rejects document-level elements. | §5.1 |
| T-7 | SVG script injection | `<svg><script>`, `<svg onload="...">` | Inline SVG is rejected in author templates. Server-rendered micro-chart SVG is declarative by construction. | §5.1, §5.3 |
| T-8 | Data-driven injection | Column value `<img src=x onerror=alert(1)>` | All substitutions HTML-encode by default. No raw escape hatch. | §5.2 |
| T-9 | Secret disclosure via embedded visual | `{{VISUAL(SecretDashboard)}}` exposing a viewer-restricted visual | Embedded visual references are resolved at analysis time using the author's declared permissions. Runtime row-level security is applied independently by the embedded visual's own query. | §3.4 |
| T-10 | Resource exhaustion | Template with 10,000 `{{#IF}}` blocks or 50,000 rows | Row, node, byte, output, and render-work budgets. | §7 |
| T-11 | CSS scope escape | `.report-header { display: none }` in visual CSS | CSS is rewritten to scope all selectors under the visual's unique container ID. | §5.5 |
| T-12 | Template injection via format spec | `FORMAT '<script>'` | Format specs are validated against the existing format-string grammar. The output is HTML-encoded after formatting. | §5.2 |
| T-13 | Content spoofing / phishing | Fake login form, fake system notification | `<form>` is rejected. The `action` attribute is not in the attribute allowlist. Visual renders inside the report chrome, not full-screen. | §5.1, §5.3 |

### 4.3 Security Invariants

1. **No author JavaScript executes.** Not in the template, not in data values, not through URL
   schemes, not through event handlers, not through CSS, not through SVG, not through embedded
   visuals. This invariant holds across browser, PDF, email, terminal, and all future surfaces.

2. **All substituted values are HTML-encoded.** There is no initial raw-output escape hatch.
   If a future need arises, it requires a separate ADR with its own threat model.

3. **CSS cannot escape the visual boundary.** Visual CSS is rewritten to scope under the visual's
   container. It cannot restyle the report shell, neighboring visuals, or the Portal chrome.

4. **URLs are restricted to safe schemes.** Only `https:`, `http:`, `mailto:`, `tel:`, and
   `data:image/*` are permitted. All other schemes are rejected.

5. **External resource loading is blocked.** CSS `url()` with external hosts, `@import`,
   `@font-face`, and `<link>` are rejected. The rendered visual loads no external resources.

6. **The sanitizer runs before rendering, not after.** Invalid content is rejected at analysis/build
   time, not silently stripped at render time. Authors get clear diagnostics.

---

## 5. Sanitizer Specification

### 5.1 Element Allowlist

The sanitizer operates on a closed allowlist — only listed elements pass. Everything else is
rejected with a diagnostic identifying the element and its location.

**Allowed elements:**

| Category | Elements |
|----------|----------|
| Structure | `div`, `span`, `section`, `article`, `aside`, `header`, `footer`, `nav`, `main` |
| Headings | `h1`, `h2`, `h3`, `h4`, `h5`, `h6` |
| Text | `p`, `br`, `hr`, `pre`, `code`, `blockquote`, `em`, `strong`, `i`, `b`, `u`, `s`, `small`, `sub`, `sup`, `mark`, `abbr`, `time`, `cite`, `q`, `dfn`, `var`, `kbd`, `samp` |
| Lists | `ul`, `ol`, `li`, `dl`, `dt`, `dd` |
| Tables | `table`, `thead`, `tbody`, `tfoot`, `tr`, `th`, `td`, `caption`, `colgroup`, `col` |
| Media | `img` (with URL policy), `figure`, `figcaption`, `picture`, `source` (type-restricted) |
| Interactive | `a` (with URL policy), `button` (type=button only), `details`, `summary` |
| Data | `data`, `meter`, `progress`, `output` |

**Rejected elements (non-exhaustive — anything not in the allowlist is rejected):**

`script`, `style` (as element — CSS is supplied through `STYLE(CSS=...)`), `link`, `meta`, `base`,
`iframe`, `frame`, `frameset`, `object`, `embed`, `applet`, `form`, `input`, `select`, `textarea`,
`svg` (inline — server-rendered micro-chart SVG is injected by the renderer), `math`, `template`,
`slot`, `dialog`, `canvas`, `audio`, `video`, `track`, `map`, `area`, `portal`, `noscript`.

### 5.2 Substitution Encoding

All `{{...}}` substitutions produce HTML-encoded output:

| Character | Encoded as |
|-----------|-----------|
| `&` | `&amp;` |
| `<` | `&lt;` |
| `>` | `&gt;` |
| `"` | `&quot;` |
| `'` | `&#x27;` |
| `/` | `&#x2F;` |

This encoding applies after any format-spec processing and before insertion into the template.
There is no `{{{raw}}}` or `{{& unescaped}}` form.

### 5.3 Attribute Allowlist

Attributes operate on a per-element allowlist. Any attribute not explicitly listed for its element
is rejected.

**Global attributes** (allowed on all permitted elements):
`class`, `id`, `title`, `lang`, `dir`, `role`, `aria-*`, `data-etl-*`, `tabindex`, `hidden`.

**Element-specific attributes:**

| Element | Additional allowed attributes |
|---------|------------------------------|
| `a` | `href` (URL policy), `target` (`_blank` only, auto-adds `rel="noopener noreferrer"`), `rel` |
| `img` | `src` (URL policy), `alt` (required), `width`, `height`, `loading` (`lazy` or `eager`) |
| `button` | `type` (`button` only — `submit` rejected), `disabled`, `data-action`, `data-param`, `data-value` |
| `td`, `th` | `colspan`, `rowspan`, `scope`, `headers` |
| `col`, `colgroup` | `span` |
| `ol` | `start`, `type`, `reversed` |
| `time` | `datetime` |
| `meter` | `min`, `max`, `low`, `high`, `optimum`, `value` |
| `progress` | `max`, `value` |
| `data` | `value` |
| `abbr` | `title` |
| `blockquote`, `q` | `cite` (URL policy) |
| `source` | `srcset` (URL policy), `type` (image MIME types only), `media` |
| `details` | `open` |

**Rejected attribute patterns:**

- All `on*` attributes (`onclick`, `onload`, `onerror`, `onmouseover`, etc.).
- `style` (inline styles are rejected — all styling goes through `STYLE(CSS=...)`).
- `srcdoc`, `sandbox`, `allow`, `allowfullscreen`, `formaction`, `formmethod`.
- `href` with disallowed URL scheme.
- Any attribute not in the per-element allowlist.

### 5.4 URL Policy

URLs in `href`, `src`, `cite`, and `srcset` attributes are validated:

| Scheme | Allowed | Notes |
|--------|:-------:|-------|
| `https:` | Yes | External links open in new tab with `rel="noopener noreferrer"`. |
| `http:` | Yes | Same treatment as `https:`. |
| `mailto:` | Yes | Standard email links. |
| `tel:` | Yes | Standard telephone links. |
| `data:image/png`, `data:image/jpeg`, `data:image/gif`, `data:image/svg+xml`, `data:image/webp` | Yes | Inline images only. `data:image/svg+xml` content is validated for script absence. |
| `data:text/*`, `data:application/*` | **No** | Prevents HTML/JS injection through data URIs. |
| `javascript:` | **No** | — |
| `vbscript:` | **No** | — |
| `blob:` | **No** | — |
| Relative paths | **No** | Templates have no base URL. Relative paths would be ambiguous. |
| No scheme | **No** | Treated as relative path. |

### 5.5 CSS Isolation

CSS supplied through `STYLE(CSS=...)` is sanitized and scoped:

**Scoping:** All selectors are rewritten to nest under the visual's unique container:
`.node-card { ... }` becomes `#etl-v-<visual-id> .node-card { ... }`.

**Allowed CSS:**

- Standard properties (layout, spacing, typography, color, border, background with inline values).
- `var(--etl-*)` custom property references — the report theme exposes a defined set of tokens.
- Pseudo-classes (`:hover`, `:focus`, `:first-child`, `:nth-child(...)`, `:not(...)`, etc.).
- Pseudo-elements (`::before`, `::after`, `::marker`, `::first-line`, `::first-letter`).
- Media queries (`@media`).
- Keyframe animations (`@keyframes` — scoped to the visual's namespace).

**Rejected CSS:**

| Pattern | Reason |
|---------|--------|
| `@import` | External resource loading. |
| `@font-face` | External resource loading. |
| `url()` with `http:`, `https:`, or any external host | External resource loading / exfiltration. |
| `url()` with `data:` except `data:image/*` | Script injection vector. |
| `expression()` | IE CSS expression (JavaScript execution). |
| `-moz-binding` | XBL binding (script execution). |
| `behavior:` | IE DHTML behavior (script execution). |
| `var()` referencing non-`--etl-*` custom properties | Prevents reading host/Portal CSS state. |
| Selectors escaping the visual scope | Rewriting ensures all selectors are scoped. |

**Exposed theme tokens (`--etl-*`):**

| Token | Description |
|-------|-------------|
| `--etl-bg` | Page background |
| `--etl-surface` | Card/tile surface |
| `--etl-border` | Standard border color |
| `--etl-text` | Primary text |
| `--etl-text-secondary` | Secondary/muted text |
| `--etl-accent` | Theme accent color |
| `--etl-success` | Success state |
| `--etl-warning` | Warning state |
| `--etl-danger` | Danger/error state |
| `--etl-info` | Informational state |
| `--etl-font-family` | Report body font |
| `--etl-font-mono` | Monospace font |
| `--etl-radius` | Standard border radius |
| `--etl-shadow` | Standard box shadow |

---

## 6. Interaction Projection

HTML visuals participate in the declarative action system. No DOM event handlers are used.

### 6.1 Actions

HTML visual actions use the same `ACTIONS (...)` clause as all other visual types:

```sql
ACTIONS (
  ON CLICK SET @selected_node = {{HostName}},
  ON CLICK NAVIGATE TO NodeDetail
)
```

In `REPEATER` mode, `{{FieldName}}` in an action value resolves to the clicked row's column value.
The runtime projects actions onto the rendered component using `data-action`, `data-param`, and
`data-value` attributes on interactive elements. The report runtime handles the event — the
template never contains inline handlers.

### 6.2 Parameter Refresh

When a parameter changes, the HTML visual re-evaluates its source query (if any), re-evaluates
the template, and publishes an updated manifest. The update is atomic — viewers never see partial
template state (e.g., some `{{...}}` substituted and others not).

### 6.3 Cross-Filtering

HTML visuals can be cross-filter targets (their source query references the filtering parameter)
but do not emit cross-filter selections. Outbound cross-filtering requires a structured data
contract (mark selection) that HTML visuals do not have.

---

## 7. Budgets

HTML visuals enforce explicit budgets that fail the build rather than rendering a degraded surface.

| Budget | Default | Configurable | Description |
|--------|---------|:------------:|-------------|
| **Row budget** | 500 | Yes (`MAX_ROWS` option) | Maximum rows in REPEATER mode. Prevents rendering thousands of DOM nodes. |
| **Template node budget** | 200 | No | Maximum HTML element nodes in the parsed template (before row expansion). |
| **Output node budget** | 10,000 | No | Maximum total nodes after REPEATER expansion (template nodes × rows). |
| **Template byte budget** | 64 KB | No | Maximum size of the TEMPLATE string. |
| **CSS byte budget** | 32 KB | No | Maximum size of the CSS string. |
| **Output byte budget** | 2 MB | No | Maximum size of the rendered HTML output per visual instance. |
| **Conditional depth** | 4 | No | Maximum nesting depth of `{{#IF}}` blocks. |
| **Embed depth** | 2 | No | Maximum nesting depth for `{{VISUAL(...)}}` references. |
| **Aggregate report visual budget** | Shared | — | Embedded visuals count toward the report's overall visual count and query budget. |

Budget overruns are reported as analysis-time diagnostics (`RPT3020`–`RPT3029`), not runtime
errors. The author sees the specific budget that was exceeded and the actual value.

---

## 8. Portable Fallback Contract

Every HTML visual must have a semantic fallback for surfaces that cannot render HTML: email,
Markdown export, terminal, plain text, and screen readers.

### 8.1 Explicit Fallback

Authors can supply a `FALLBACK` clause containing a plain-text template with the same `{{...}}`
substitution syntax (but no HTML, no conditionals, no micro-chart helpers):

```sql
FALLBACK = 'Node {{HostName}}: CPU {{CpuPercent}}, Status {{Status}}'
```

In REPEATER mode, the fallback renders as a newline-separated list capped at 20 rows, with a
trailing `"... and N more"` summary when truncated.

### 8.2 Auto-Generated Fallback

When no `FALLBACK` is supplied, the renderer generates one:

- **SINGLE mode with SOURCE:** `"<VisualName>: <Field1> <Value1>, <Field2> <Value2>, ..."`
  using the first 5 fields.
- **SINGLE mode without SOURCE:** `"<VisualName>"` (static component — no data to summarize).
- **REPEATER mode:** `"<VisualName>: <N> items"` with a count of rows.

### 8.3 Surface Mapping

| Surface | Rendering |
|---------|-----------|
| Browser | Full sanitized HTML with scoped CSS. |
| PDF / Print | Full sanitized HTML rendered by the headless browser print path. |
| Email (HTML) | Sanitized HTML with CSS inlined to element `style` attributes (email client compat). For email, `STYLE(CSS=...)` is auto-inlined. |
| Email (plain text) | Fallback template. |
| Markdown export | Fallback template in a fenced block. |
| Terminal | Fallback template rendered via Spectre Console markup. |
| Screen reader | `aria-label` on the visual container carries the fallback text. The semantic HTML structure within provides additional navigation. |
| Snapshot (`__ETLSNAP__`) | Sanitized HTML and CSS are serialized in the snapshot payload. |

### 8.4 Determinism

The same template, data, and parameters must produce byte-identical output across evaluation
passes. The evaluator is stateless and side-effect-free. Field iteration order in auto-generated
fallbacks follows schema declaration order.

---

## 9. AST and IR Design

### 9.1 VisualType Enum

Add `Html` to the `VisualType` enum.

### 9.2 New AST Nodes

```csharp
public enum HtmlVisualMode { Single, Repeater }

public record HtmlTemplateDefinition : AstNode
{
    public required string Template { get; init; }
    public string? Css { get; init; }
    public string? Fallback { get; init; }
    public HtmlVisualMode Mode { get; init; } = HtmlVisualMode.Single;
}
```

`HtmlTemplateDefinition` is set on `CreateVisualStatement` only when `VisualType == Html`.

### 9.3 Manifest Extension

`VisualManifest` gains:

```csharp
[JsonPropertyName("htmlContent")]
public string? HtmlContent { get; set; }

[JsonPropertyName("htmlCss")]
public string? HtmlCss { get; set; }

[JsonPropertyName("htmlFallback")]
public string? HtmlFallback { get; set; }

[JsonPropertyName("htmlMode")]
public string? HtmlMode { get; set; }
```

Browser consumers render `htmlContent` inside a scoped container. Non-browser consumers use
`htmlFallback`.

---

## 10. Parser Validation Rules

The parser enforces these constraints at parse time:

| Rule | Diagnostic | Description |
|------|-----------|-------------|
| TEMPLATE required | `RPT3000` | HTML visuals must have a TEMPLATE clause. |
| No MAPPINGS | `RPT3003` | HTML visuals cannot use MAPPINGS. |
| No SERIES | `RPT3004` | HTML visuals cannot use SERIES. |
| No OVERLAYS | `RPT3005` | HTML visuals cannot use OVERLAYS. |
| No FORMATTING | `RPT3006` | HTML visuals cannot use FORMATTING rules. |
| No CHART | `RPT3007` | HTML visuals cannot use the CHART clause. |
| No CASCADE | `RPT3008` | HTML visuals cannot use CASCADE. |
| REPEATER needs SOURCE | `RPT3009` | MODE = REPEATER requires a SOURCE clause. |
| Unknown field | `RPT3001` | Template references a field not in the source schema. |
| Unknown parameter | `RPT3002` | Template references an undeclared parameter. |
| Embed cycle | `RPT3010` | Embedded visual reference creates a cycle. |
| Embed depth | `RPT3011` | Embedded visual nesting exceeds maximum depth. |
| Budget exceeded | `RPT3020–3029` | Various budget overruns. |

---

## 11. Analysis and LSP

### 11.1 Lint Rules

- `HtmlVisualTemplateRequiredRule` — TEMPLATE clause present.
- `HtmlVisualDisallowedClausesRule` — no MAPPINGS/SERIES/OVERLAYS/FORMATTING/CHART/CASCADE.
- `HtmlVisualFieldResolutionRule` — all `{{Field}}` references resolve against source schema.
- `HtmlVisualParameterResolutionRule` — all `{{@Param}}` references resolve against declared params.
- `HtmlVisualSanitizerRule` — template passes element/attribute/URL/CSS allowlists.
- `HtmlVisualBudgetRule` — template is within node, byte, and depth budgets.
- `HtmlVisualEmbedRule` — embedded visual references are acyclic and within depth budget.

### 11.2 LSP Support

| Feature | Behavior |
|---------|----------|
| Completion | Inside `{{...}}`, offer source columns and `@param` names. Inside `STYLE(CSS=...)`, offer `--etl-*` tokens. After `AS`, offer `HTML` alongside existing types. |
| Hover | On `{{FieldName}}`, show column type and source. On `{{@Param}}`, show parameter type and default. |
| Rename | Renaming a column or parameter updates `{{...}}` references in TEMPLATE, FALLBACK, and ACTIONS. |
| Diagnostics | Real-time sanitizer and budget validation. |

---

## 12. Delivery Slices

| Slice | Scope |
|-------|-------|
| **S-1** | ADR accepted (this document). |
| **S-2** | `VisualType.Html`, AST nodes, parser, formatter, parse-time validation. |
| **S-3** | Template evaluator: substitutions, conditionals, encoding. |
| **S-4** | HTML/CSS sanitizer: element/attribute/URL allowlists, CSS scoping. |
| **S-5** | Manifest builder: HTML content generation, fallback generation. |
| **S-6** | Renderer integration: browser, PDF/print, email, terminal, snapshot. |
| **S-7** | Micro-chart helpers (`{{SPARKLINE}}`, etc.) in HTML templates. |
| **S-8** | Embedded visual references (`{{VISUAL(...)}}`). |
| **S-9** | LSP, lint rules, syntax index, help, snippets. |
| **S-10** | Hostile-input test suite, budget tests, cross-surface conformance, production samples. |

Slices S-2 through S-4 are the minimal viable feature. S-7 and S-8 can be deferred without
blocking the initial delivery.

---

## 13. Alternatives Considered

### 13.1 Markdown-Only Presentation

Markdown is safe by default but cannot express grid layouts, styled badges, conditional visibility,
or data-driven repeated components. It would require inventing a non-standard Markdown dialect.

### 13.2 Full Client-Side Templating (Handlebars, Mustache)

Client-side templating engines include features (helpers, partials, block expressions, unescaped
output) that enlarge the attack surface and break deterministic multi-surface parity. The template
would execute on the client, violating the "no author code executes" invariant.

### 13.3 Shadow DOM Isolation

Shadow DOM provides style encapsulation but requires JavaScript to create and cannot be serialized
to PDF, email, or terminal. CSS scoping via selector rewriting achieves the isolation goal across
all surfaces.

### 13.4 CSP-Based Script Prevention

Content Security Policy headers can block inline scripts but are not available in PDF, email, or
terminal contexts. CSP is a defense-in-depth layer for the browser surface, not a substitute for
the sanitizer. The browser runtime may set `script-src 'none'` on HTML visual containers as an
additional hardening measure.

---

## 14. Decision

Adopt `CREATE VISUAL ... AS HTML` with the template grammar, sanitizer, evaluator, interaction
projection, budgets, and fallback contract defined in this ADR. The sanitizer operates on closed
allowlists and runs before rendering. All substitutions are HTML-encoded by default with no raw
escape hatch. CSS is scoped to the visual boundary. Interactions use the existing declarative action
system. Embedded visuals are statically resolved with cycle and depth limits.

The first delivery is script-first. WYSIWYG editing is deferred.
