# HTML

Creates a bespoke presentation component from sanitized semantic HTML, scoped CSS, escaped source fields or report parameters, and declarative Report-SQL actions. Author JavaScript, inline event handlers, executable URLs, raw substitution, and unscoped styles are rejected.

## Syntax

```sql
CREATE [OR ALTER] VISUAL <name> AS HTML (
  [SOURCE = <source>,]
  [MODE = SINGLE | REPEATER,]
  TEMPLATE = '<semantic-html>',
  [STYLE (CSS = '<scoped-css>'),]
  [FALLBACK = '<plain-text-template>',]
  [OPTIONS (MAX_ROWS = <count>),]
  [ACTIONS (<declarative-actions>)]
);
```

## Mappings

- **SOURCE = source** — Optional temp table, dataset, or inline query. Omit it for static or parameter-only components.
- **MODE = SINGLE** — Renders the first source row. This is the default and also supports source-free components.
- **MODE = REPEATER** — Renders once per source row. A source is required.
- **TEMPLATE = text** — Required semantic HTML. Use `{{Field}}`, `{{Field FORMAT 'spec'}}`, `{{@parameter}}`, and bounded `{{#IF ...}}{{/IF}}` blocks. Every value is escaped.
- **VISUAL(name, PARAMETERS(...))** — Optionally embeds a declared report visual. References resolve before publication, nesting is capped at two levels, and missing targets, cycles, sensitive bindings, and aggregate budget overruns fail closed.
- **STYLE (CSS = text)** — Optional CSS rewritten beneath the visual container. Only approved `--etl-*` theme variables are visible.
- **FALLBACK = text** — Optional plain-text semantic summary for terminal, Markdown, email text, screen readers, and unsupported surfaces. It accepts field and parameter substitutions but no markup or conditionals.
- **ACTIONS (...)** — Uses standard Report-SQL actions. Interactive elements declare `data-action`, `data-param`, and `data-value`; they never declare `onclick` or other event handlers.

## Options

- **OPTIONS (MAX_ROWS = count)** — Sets the repeater row limit. All fixed template, node, byte, output, and render-work budgets still apply.
- **Bindings** — Field and parameter values are HTML-escaped. There is no raw-output form.
- **Elements and attributes** — A closed allowlist admits semantic structure, accessible images, links, and `type="button"` controls.
- **URLs** — Only HTTP(S), email, telephone, fragments, and approved script-free data images are accepted.
- **CSS** — External loads, imports, fonts, expressions, browser bindings, host variables, and scope escape are rejected.
- **Static output** — Browser, print, and browser-backed PDF use sanitized markup. Text-only surfaces use the deterministic semantic fallback without claiming unavailable interaction.
- **Budgets** — Oversized templates, styles, row expansions, node counts, output, and aggregate report work fail closed with `RPT3020`–`RPT3029` diagnostics.

## Examples

```sql
DECLARE @environment VARCHAR(20) = 'Production';

CREATE VISUAL EnvironmentBanner AS HTML (
  TEMPLATE = '<section class="banner"><strong>{{@environment}}</strong></section>',
  STYLE (CSS = '.banner { color: var(--etl-text); background: var(--etl-surface); }'),
  FALLBACK = 'Environment: {{@environment}}'
);
```

```sql
SELECT 'api-01' AS HostName, 'Healthy' AS Status INTO #nodes
UNION ALL SELECT 'db-01', 'Critical';

DECLARE @selected_status VARCHAR(20) = '';

CREATE VISUAL NodeStatus AS HTML (
  SOURCE = #nodes,
  MODE = REPEATER,
  TEMPLATE = '<article class="node"><strong>{{HostName}}</strong>{{#IF Status = ''Critical''}}<mark>Critical</mark>{{/IF}}<button type="button" data-action="SET_PARAMETER" data-param="@selected_status" data-value="{{Status}}">Select</button></article>',
  STYLE (CSS = '.node { border: 1px solid var(--etl-border); padding: 0.75rem; }'),
  FALLBACK = '{{HostName}}: {{Status}}',
  OPTIONS (MAX_ROWS = 100),
  ACTIONS (ON_CLICK = SET_PARAMETER(@selected_status, Status))
);

CREATE VISUAL StatusSummary AS CARD (
  SOURCE = (SELECT @selected_status AS Status)
);

CREATE VISUAL StatusComponent AS HTML (
  TEMPLATE = '<aside>{{VISUAL(StatusSummary, PARAMETERS(@selected_status = ''Critical''))}}</aside>',
  FALLBACK = 'Selected status summary'
);
```

## References

- [Report-SQL Guide](../../../guides/feature-guides/report-sql.md)
- [VISUAL Statement](../report/visual.md)
- [Constrained HTML Visuals Decision](../../../architecture/decisions/constrained-html-visuals.md)
