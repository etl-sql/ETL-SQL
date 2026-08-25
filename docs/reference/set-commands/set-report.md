# SET REPORT
Sets a report-level property for a `.rptsql` script: page metadata, branding, and the deterministic formatting every renderer uses. Unknown keys are rejected.

## Syntax
```sql
SET REPORT <KEY> = '<value>';
```

## Parameters
- **TITLE = 'text'** — report title shown in the browser tab, exports, and the Portal catalog.
- **DESCRIPTION = 'text'** — short description shown alongside the title.
- **CSS = 'text'** — extra stylesheet text injected into the rendered report.
- **JS = 'text'** — extra script text injected into the rendered report.
- **HEAD = 'html'** — raw HTML appended to the document head.
- **BODY = 'html'** — raw HTML injected at the start of the document body.
- **FOOTER = 'html'** — raw HTML injected at the end of the document body.
- **FAVICON = 'path'** — favicon used by the rendered report.
- **LOGO = 'path'** — logo image shown in the report header.
- **BACKGROUND = 'value'** — page background color or image.
- **THEME = 'name'** — default theme applied to every page and visual that does not set its own.
- **NAVIGATION = 'name'** — the navigation component rendered for the report.
- **TIME_ZONE = 'zone'** — the zone every date and time in the report is rendered in. Accepts IANA ids (`America/New_York`) and the abbreviations the rest of the language accepts (`UTC`, `EST`, `JST`).
- **LOCALE = 'culture'** — the culture used to format dates, times, and computed numbers. Use a .NET culture name such as `de-DE`, or `''` for the invariant culture.
- **NULL_LABEL = 'text'** — the text rendered in place of a NULL value.

## Formatting Precedence
Formatting is resolved on the server and never inferred from the viewer's browser, so the same report renders identically in the browser, a PDF, an email, and the terminal.

- **Time zone** — `SET REPORT TIME_ZONE`, then `Scheduler:DefaultTimeZone`, then `UTC`.
- **Locale** — `SET REPORT LOCALE`, then `Reporting:DefaultLocale`, then the invariant culture.
- **NULL label** — a visual's `OPTIONS (NULL_LABEL = '...')`, then `SET REPORT NULL_LABEL`, then `Reporting:DefaultNullLabel`, then `-`.

## Example
```sql
-- Every instant renders in New York time, formatted for a German audience
SET REPORT TITLE = 'Regional Revenue';
SET REPORT TIME_ZONE = 'America/New_York';
SET REPORT LOCALE = 'de-DE';
SET REPORT NULL_LABEL = 'kein Wert';

CREATE VISUAL RevenueTrend AS LINE (
  SOURCE = #revenue,
  MAPPINGS (X = ObservedTime, Y = Amount)
);
```

```sql
-- A single visual can override the report's NULL label
CREATE VISUAL Coverage AS BAR (
  SOURCE = #coverage,
  MAPPINGS (X = Region, Y = Pct),
  OPTIONS (NULL_LABEL = 'not reported')
);
```

## Notes
- An unrecognised key is a syntax error. `SET REPORT TIMEZONE = 'UTC'` fails and names the supported keys.
- An unknown time zone or culture is rejected when the statement runs, rather than falling back silently.
- The resolved values survive report-context clone and clear, parallel visual builds, interaction refreshes, and snapshots.
- Named visuals and `CUSTOM` charts resolve the same formatting and the same theme tokens.

## References
- [SET Commands](README.md)
- [Report-SQL](../visuals-reporting/report/index.md)
- [Configuration Settings Reference](../../administration/platform/appsettings-reference.md)
