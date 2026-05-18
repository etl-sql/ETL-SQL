# Subscription Parameters Strategy

> [!IMPORTANT]
> **Historical feature plan.** Relative date parameters and subscription parameter behavior are now documented in current reference/user docs. Use this file for rationale and implementation history only; use `Docs/Reference/RelativeDate_Parameters.md`, `Docs/Report_SQL_Guide.md`, and `Docs/ReportPortal_Administrators_Guide.md` for current behavior.

## Problem Statement

Subscriptions today are static — they deliver the same report snapshot on every run. A report writer who wants a "Yesterday's Sales" subscription must hardcode a date or write a new script for every variation. There is no way to tell a subscription "run with `@start = yesterday`" without modifying the script itself.

This strategy adds three tightly related capabilities:

1. **`RELDATE`** — a parameter type that encodes a time-relative expression (`D-1`, `ME-1`, `N-2H`) and resolves to an actual datetime at execution time. Subscriptions store the expression; each run resolves it fresh.
2. **`LIST(type)`** — a parameter type for multi-value `IN`-clause filters, encoded as a comma-separated string with double-quote escaping for values that contain commas.
3. **`CREATE SUBSCRIPTION … PARAMETERS(…)`** — a clause that lets the report writer (or a scripted deploy) pin parameter values for a subscription directly in ETL-SQL syntax, and a corresponding portal UI so end users can do the same.

These three pieces are designed as one cohesive feature. Implementing them independently will create loose ends.

---

## Goals

- A report writer can declare `DECLARE @start RELDATE = D-1` and a subscription will always pass "yesterday at midnight" without any hardcoding.
- A user subscribing via the portal sees INPUT parameter fields tailored to the parameter's type (text, number, date-picker, RELDATE quick-pick, chip input).
- Administrators can script subscriptions with full parameter control via `CREATE SUBSCRIPTION`.
- Parameters saved on a subscription are editable post-creation from the "My Subscriptions" view.
- NULL-for-empty semantics: a blank INPUT parameter passes `NULL`; the report writer handles it explicitly (typically "no filter").

---

## New SQL Types

### RELDATE

#### Purpose

`RELDATE` parameters encode a relative date *expression* rather than a fixed value. The engine resolves the expression to a concrete `DATETIME` at execution time.

```sql
DECLARE @start RELDATE = D-1;   -- yesterday at midnight
DECLARE @end   RELDATE = D;     -- today at midnight
```

#### Anchors

| Anchor | Resolves to | Arithmetic unit |
| :--- | :--- | :--- |
| `D` | Today at midnight | Days |
| `W` / `WS` | Start of current week (configurable day, default Monday) | Weeks |
| `WE` | Last day of current week (day before next week start) | Weeks |
| `M` / `MS` | 1st of current month at midnight | Months |
| `ME` | Last day of current month at midnight | Months |
| `Q` / `QS` | 1st of current quarter at midnight | Quarters |
| `QE` | Last day of current quarter at midnight | Quarters |
| `Y` / `YS` | January 1 of current year at midnight | Years |
| `YE` | December 31 of current year at midnight | Years |
| `N` | Exact current local datetime (floating) | `H` hours, `I` minutes, `S` seconds |
| `NU` | Exact current UTC datetime (floating) | `H` hours, `I` minutes, `S` seconds |

`W`/`WS`, `M`/`MS`, `Q`/`QS`, `Y`/`YS` are aliases; the `S` suffix is for readability only.

#### Arithmetic

Append `+n` or `-n` to shift by *n* units. **Critical rule:** arithmetic shifts the period anchor first, then applies the start/end modifier — it does not shift the resolved date.

```
D-1       → yesterday at midnight
ME-1      → last day of last month  (not: last day of this month − 1 day)
QE-1      → last day of last quarter
YE-2      → December 31 two years ago
N-2H      → exactly 2 hours ago
N-30I     → exactly 30 minutes ago
```

For `N`/`NU`, the arithmetic unit is inline:

| Modifier | Meaning |
| :--- | :--- |
| `nH` | n hours |
| `nI` | n minutes |
| `nS` | n seconds |

`H`, `I`, `S` are **not** standalone anchors — only modifiers on `N`/`NU`.

#### Week start day

`W` snaps to the configured start-of-week day. Two-tier config:

**`appsettings.json`** (global default):
```json
"Engine": { "StartOfWeek": "Monday" }
```

**Per-script override** (applies for the duration of the session):
```sql
SET WEEK_START_DAY = 'Sunday';
```

Valid values: `Monday` through `Sunday` (case-insensitive). Any other value is a runtime error. Default: `Monday` (ISO 8601).

Example with `WEEK_START_DAY = 'Wednesday'`, today = Thursday April 17:
- `W` = Wednesday April 16 (most recent start day, including today if today is that day)
- `W-1` = Wednesday April 9 (one full week back)
- `WE` = Tuesday April 22 (day before the next Wednesday)
- `WE-1` = Tuesday April 15

#### Fixed date passthrough

`RELDATE` also accepts a fixed ISO date string. The resolver distinguishes them by the first character: letter → expression; digit → fixed date.

```sql
DECLARE @start RELDATE = D-1;         -- relative expression
DECLARE @end   RELDATE = '2026-12-31'; -- fixed passthrough
```

#### Time zone

All period anchors resolve in **server local time**. `N` → local; `NU` → UTC. Use `FOR TIMEZONE` or a timezone parameter for zone conversion.

---

### LIST(type)

For multi-value `IN`-clause parameters:

```sql
DECLARE @regions LIST(VARCHAR(200)) INPUT;
```

#### Encoding

Comma-separated values. Values containing a comma are double-quoted:

```
North,South,East             -- three values
"North, Central",South       -- two values; first contains a comma
```

#### NULL semantics

An empty or absent `LIST` parameter is `NULL`. The report writer handles it:

```sql
WHERE (@regions IS NULL OR Region IN (SELECT value FROM STRING_SPLIT(@regions, ',')))
```

#### Portal UI

Render `LIST` fields as a tag/chip input. Each chip = one value. A plain textarea is the accessibility fallback.

---

## SQL Syntax

### INPUT modifier

Report writers mark parameters as subscription-configurable with the `INPUT` modifier:

```sql
DECLARE @start  RELDATE          INPUT;
DECLARE @end    RELDATE          INPUT;
DECLARE @region VARCHAR(100)     INPUT;
DECLARE @year   INT              INPUT;
DECLARE @brands LIST(VARCHAR(200)) INPUT;
```

`INPUT` parameters without a default arrive as `NULL` if not supplied by the subscription. Parameters with a default use the default if the subscription omits them.

```sql
DECLARE @region VARCHAR(100) = 'All' INPUT;  -- defaults to 'All' if not supplied
```

---

### CREATE SUBSCRIPTION

Full proposed syntax:

```sql
CREATE SUBSCRIPTION ['<name>']
FOR REPORT '<script-path>'
DELIVER TO '<email>' | GROUP '<group-name>'
SCHEDULE '<cron-expression>' | ON REFRESH
FORMAT PDF | CSV | BOTH
AT <smtp-alias>
[ PARAMETERS (
    @param1 = '<value>',
    @param2 = '<value>',
    ...
) ];
```

`'<name>'` is an optional human-readable label. If omitted, the subscription is anonymous and identified by its ID in `ALTER` / `DROP` statements.

Parameter values are stored as strings and must be single-quoted. RELDATE expressions are stored as strings and resolved fresh at delivery time.

```sql
-- Daily sales report: always yesterday's data
CREATE SUBSCRIPTION 'DailySales'
FOR REPORT '/Reports/Sales/Daily'
DELIVER TO 'john@example.com'
SCHEDULE '0 6 * * *'
FORMAT PDF
AT corporate-smtp
PARAMETERS (
    @start  = 'D-1',
    @end    = 'D',
    @region = 'All'
);

-- Monthly executive summary
CREATE SUBSCRIPTION 'MonthlyExec'
FOR REPORT '/Reports/Executive/MonthlySummary'
DELIVER TO GROUP 'Executives'
SCHEDULE '0 7 1 * *'
FORMAT PDF
AT corporate-smtp
PARAMETERS (
    @period_start = 'M-1',
    @period_end   = 'ME-1'
);

-- Ad-hoc fixed date range
CREATE SUBSCRIPTION 'Q1Review'
FOR REPORT '/Reports/Finance/Quarterly'
DELIVER TO 'cfo@example.com'
SCHEDULE '0 8 * * 1'
FORMAT PDF
AT corporate-smtp
PARAMETERS (
    @start = '2026-01-01',
    @end   = '2026-03-31'
);
```

---

### ALTER SUBSCRIPTION

```sql
ALTER SUBSCRIPTION <id> SET
    SCHEDULE = '<cron-expression>' |
    FORMAT = PDF | CSV | BOTH |
    SMTP = '<smtp-alias>' |
    ENABLE |
    DISABLE |
    PARAMETERS (
    @param1 = '<value>',
    ...
);
```

`PARAMETERS(...)` in `ALTER` **replaces** the full parameter set for the subscription. To clear all parameters, use `PARAMETERS ()` (empty list). To leave parameters unchanged, omit the clause entirely.

```sql
-- Change schedule only
ALTER SUBSCRIPTION 5 SET SCHEDULE = '0 8 * * 1-5';

-- Update parameters only
ALTER SUBSCRIPTION 5 SET
PARAMETERS (
    @start  = 'W-1',
    @end    = 'W',
    @region = 'North'
);

-- Pause a subscription
ALTER SUBSCRIPTION 6 SET DISABLE;
```

---

### DROP SUBSCRIPTION

```sql
DROP SUBSCRIPTION <id>;
```

(Subscriptions are mutated by ID after creation.)

---

## Portal UX Specification

### Subscribing to a Report (Section 6 — User Guide)

When a user clicks **Subscribe**, the portal:

1. Fetches the report's INPUT parameter metadata from `GET /api/reports/{path}/parameters`.
2. Renders the standard subscription fields (Schedule, Format, Recipient).
3. Renders one input control per INPUT parameter, labelled with the parameter name and type hint.

#### Per-type controls

| Parameter type | Control | Notes |
| :--- | :--- | :--- |
| `VARCHAR` / `NVARCHAR` / `TEXT` | Text input | Placeholder shows declared default if any |
| `INT` / `BIGINT` | Number input (integer) | |
| `DECIMAL` / `FLOAT` | Number input (decimal) | |
| `DATE` | Date picker | ISO date only, no time |
| `DATETIME` | Datetime-local picker | |
| `RELDATE` | Quick-pick dropdown + "Custom…" freetext | See quick-pick list below |
| `LIST(…)` | Chip/tag input | Chips are individual values; textarea fallback |
| `BIT` / `BOOLEAN` | Checkbox | |

#### RELDATE quick-pick options

| Label | Expression |
| :--- | :--- |
| Today | `D` |
| Yesterday | `D-1` |
| Start of this week | `W` |
| Start of last week | `W-1` |
| End of last week | `WE-1` |
| Start of this month | `M` |
| Start of last month | `M-1` |
| End of last month | `ME-1` |
| Start of this quarter | `Q` |
| Start of last quarter | `Q-1` |
| End of last quarter | `QE-1` |
| Start of this year | `Y` |
| Start of last year | `Y-1` |
| End of last year | `YE-1` |
| Now | `N` |
| *(Custom…)* | Freetext — validated on blur, rejected on save if invalid |

An **Advanced** toggle reveals the raw freetext input for any valid expression not in the quick-pick list.

#### Empty fields

An empty field submits `NULL` for that parameter. A hint label reads: *"Leave blank for no filter."* This applies to all types.

---

### Managing Your Subscriptions — Edit Parameters (Section 7 — User Guide)

The My Subscriptions list shows a compact parameter summary per subscription (e.g. `@start=D-1 @end=D @region=—`). The `—` indicates `NULL`.

**Edit Parameters** action:
- Opens a modal with the same per-type controls used in the Subscribe form.
- Pre-populated with saved values.
- Saving calls `PATCH /api/subscriptions/{id}` with the updated parameters dict.
- No full re-create is required.

**Inline quick-edit:** The parameter summary is clickable — clicking it opens the edit modal directly without requiring the user to find a separate action button.

---

### Admin Subscriptions View (Section 8 — Admin Guide)

Administrators see all subscriptions across all users. Each row includes:
- Subscription name (if set), report path, owner, schedule, last run, status.
- Parameter summary (same compact display as the user view).
- **Edit Parameters** action (same modal as the user view, but for any subscription).

---

## Implementation Checklist

### Phase 1 — Engine: New Types

These are pure engine changes with no UI surface. They can be built and unit-tested in isolation.

**`ETL-SQL.Core`**
- [ ] `Ast.cs`: Add `RelDateType`, `ListType` to the type system. `RELDATE` stores the expression string at parse time. `LIST(type)` wraps an inner type.
- [ ] `Ast.cs`: Add `SetWeekStartDayStatement` record.
- [ ] `TokenType.cs`: Add `RELDATE`, `LIST`, `INPUT`, `WEEK_START_DAY` tokens (check if any already exist).
- [ ] `Lexer.cs`: Ensure `RELDATE`, `LIST`, `INPUT`, `WEEK_START_DAY` are recognized as keywords.
- [ ] `StatementParser.*.cs`: Parse `DECLARE @var RELDATE = <expr> [INPUT]` and `DECLARE @var LIST(type) [= default] [INPUT]`.
- [ ] `StatementParser.*.cs`: Parse `SET WEEK_START_DAY = '<day>'`.
- [ ] Grammar.md: Document new tokens and production rules.

**`ETL-SQL.Engine`**
- [ ] `RelDateResolver.cs` (new): Stateless service that takes an expression string + `DayOfWeek weekStart` + `DateTime now` and returns a resolved `DateTime`. Pure function — easy to unit test exhaustively.
  - Parse anchor: `D`, `W`/`WS`, `WE`, `M`/`MS`, `ME`, `Q`/`QS`, `QE`, `Y`/`YS`, `YE`, `N`, `NU`.
  - Parse sign + magnitude + optional unit (`H`, `I`, `S` for `N`/`NU` only).
  - Apply period-shift rule: shift the period, then apply start/end modifier.
  - Passthrough: if first character is a digit, parse as ISO date and return directly.
  - Error path: unknown anchor or unit → `ExecutionException` with message including the expression.
- [ ] `SetWeekStartDayHandler.cs` (new): Validates the day name, stores on `IExecutionContext` (or a new `IWeekStartContext`). The `Evaluator` must expose and honour this.
- [ ] `Evaluator.cs`: Surface current `WeekStartDay` (defaults to `DayOfWeek.Monday` or from `appsettings.json`). Wire `SetWeekStartDayHandler` to update it.
- [ ] `appsettings.json`: Add `Engine.StartOfWeek` (string, default `"Monday"`). Read into `EvaluatorOptions` at startup.
- [ ] `ExpressionEvaluator.cs` (or wherever variable types are resolved): When a `RELDATE` variable is read, call `RelDateResolver` and return the resolved `DateTime` as the value for query purposes.
- [ ] `DeclareStatementHandler.cs` (or equivalent): Recognize `INPUT` modifier and store it on the variable descriptor.

**Tests**
- [ ] `RelDateResolverTests.cs`: Exhaustive coverage — all anchors, both arithmetic directions, all N/NU units, period-shift rule, fixed passthrough, error paths.
- [ ] `SetWeekStartDayTests.cs`: All 7 valid days, case-insensitive, invalid value → error.
- [ ] `WeekStartArithmeticTests.cs`: `W-1`, `WE-1`, `WE`, `W` with each of the 7 start days and representative calendar dates.

---

### Phase 2 — Subscription SQL Syntax

**`ETL-SQL.Core`**
- [x] `Ast.cs`: Add `Name?` (string) and `Parameters: IReadOnlyList<SubscriptionParameter>` to `CreatePortalSubscriptionStatement`.
- [x] New record: `SubscriptionParameter(string Name, string Value)` — value is always stored as a string; typed parsing happens at execution time.
- [x] Add `AlterPortalSubscriptionStatement` record with: subscription ID, `Schedule?`, `Format?`, `IsActive?`, `Parameters?` (null = don't change; empty list = clear all).
- [x] `TokenType.cs`: Add portal/subscription tokens.
- [x] `PortalParser.cs`: Parse optional string-literal name at start of `CREATE SUBSCRIPTION`.
- [x] `PortalParser.cs`: Parse `PARAMETERS(...)` clause — `@name = 'value'` pairs separated by commas, terminated by `)`.
- [x] `PortalParser.cs`: Parse `ALTER SUBSCRIPTION` statement.
- [x] `Grammar.md`: Add CREATE SUBSCRIPTION and ALTER SUBSCRIPTION productions.

**`ETL-SQL.Engine`**
- [x] `CreatePortalSubscriptionHandler.cs`: Persist `Name` and parameters into the generated subscription job script.
- [x] `AlterPortalSubscriptionHandler.cs`: Handle `ALTER SUBSCRIPTION` — update schedule, format, active state, parameters. Replace full parameter set when clause is present; leave unchanged when absent; clear when clause is empty.

---

### Phase 3 — Portal Data Layer

**`ETL-SQL.ReportPortal` — Entity / Migration**
- [ ] `Subscription.cs` entity: Add `Name` (nullable `TEXT`) and `ParametersJson` (nullable `TEXT`) columns.
- [ ] New EF Core migration: `AddSubscriptionNameAndParameters`.
- [ ] `PortalDbContext.cs`: No index needed on `Name` (not queried by name from the portal — only stored for display).

---

### Phase 4 — Portal API

**Models**
- [ ] `CreateSubscriptionRequest.cs`: Add `Name?` (string), `Parameters?` (Dictionary<string, string>).
- [ ] `UpdateSubscriptionRequest.cs` (or `PatchSubscriptionRequest`): Add `Parameters?` (Dictionary<string, string>).
- [ ] Subscription response DTOs: Add `Name?`, `Parameters?`, `ParameterSummary` (string — compact display form, built server-side).

**New endpoint**
- [ ] `GET /api/reports/{*path}/parameters`: Returns INPUT parameter metadata from the compiled report manifest (name, type, declared default). Does not execute the script. Used by the Subscribe form to know what controls to render.
  - Response shape: `{ parameters: [{ name: string, type: string, defaultValue: string|null, required: bool }] }`
  - Source: read the `.rptsql` file, parse to AST, extract `DeclareStatement` nodes with `INPUT = true`. No execution needed.

**Existing endpoints**
- [ ] `POST /api/subscriptions`: Persist `Name` and `ParametersJson`.
- [ ] `PUT /api/subscriptions/{id}` or `PATCH /api/subscriptions/{id}`: Accept `Parameters` update. Replace the parameter set atomically.
- [ ] `GET /api/subscriptions` (admin) and `GET /api/subscriptions/mine`: Include `name`, `parameters`, `parameterSummary` in response.

**Orchestrator integration**
- [ ] When the Orchestrator fires a subscription job, it must pass the stored parameter values to the script execution. Parameter string values of type `RELDATE` are resolved fresh at execution time (the stored value is the expression, not a resolved date). The Orchestrator calls `RelDateResolver` (or defers to the engine) at job-fire time.

---

### Phase 5 — Portal UI

All UI changes are in the static web files served by `ETL-SQL.ReportPortal/wwwroot/`.

**Subscribe modal / form**
- [ ] On "Subscribe" click, call `GET /api/reports/{path}/parameters` before showing the modal.
- [ ] If no INPUT parameters, show the existing form unchanged.
- [ ] If INPUT parameters exist, append a "Parameters" section to the form.
- [ ] Render per-type controls (see UX spec above).
  - RELDATE: `<select>` with quick-pick options + an "Advanced" text input revealed on "Custom…" selection. Validate expression on blur using a client-side regex (same grammar as the spec).
  - LIST: chip/tag input backed by a hidden comma-separated `<input>`.
  - All others: standard HTML input with appropriate `type` attribute.
- [ ] On save, serialize parameters to `{ "@paramName": "value" }` and include in `POST /api/subscriptions`.

**My Subscriptions list**
- [ ] Each row: add parameter summary string (received from API) next to the subscription name.
- [ ] **Edit Parameters** action: opens modal pre-populated with saved values. On save, calls `PATCH /api/subscriptions/{id}`.
- [ ] The parameter summary text is clickable and opens the edit modal directly.

**Admin Subscriptions view**
- [ ] Add parameter summary column to the subscriptions table.
- [ ] Add **Edit Parameters** action (same modal).

---

### Phase 6 — Documentation

- [ ] `Docs/Reference/RelativeDate_Parameters.md` — already written. Add a cross-reference section: "See also: CREATE SUBSCRIPTION syntax, Portal Subscribe form."
- [ ] `Docs/Report_SQL_Guide.md` — add `RELDATE` and `LIST` to the parameter type table. Add `INPUT` modifier documentation with a section explaining the subscription lifecycle.
- [ ] `Docs/ReportPortal_User_Guide.md` — update Section 6 (Subscribe) with parameter controls. Update Section 7 (Manage Subscriptions) with Edit Parameters.
- [ ] `Docs/ReportPortal_Administrators_Guide.md` — update Section 8 (Subscriptions) with `CREATE SUBSCRIPTION` / `ALTER SUBSCRIPTION` syntax and PARAMETERS clause.
- [ ] `Docs/User_Manual.md` — add `SET WEEK_START_DAY` to the SET statement reference, and `Engine.StartOfWeek` to the configuration reference.
- [ ] `Docs/Reference/Grammar.md` — add productions for new statements and types.

---

## Suggested Build Order

The phases above are mostly independent but have some ordering constraints:

1. **Phase 1 first** — `RelDateResolver` is the most isolated and most testable component. Build and test it standalone before touching anything else. `SET WEEK_START_DAY` is a quick follow-on.
2. **Phase 2 + Phase 3 in parallel** — SQL syntax and the data migration don't depend on each other.
3. **Phase 4 after Phase 3** — the API needs the entity columns.
4. **Phase 5 after Phase 4** — the UI needs the API endpoints.
5. **Phase 6 throughout** — documentation can be written incrementally. The reference doc is already done.

Estimated scope (rough order-of-magnitude):
- Phase 1: ~2 days (resolver + tests are non-trivial)
- Phase 2: ~1 day
- Phase 3: ~0.5 day (migration is simple)
- Phase 4: ~1 day
- Phase 5: ~1.5 days (UI is the most effort after Phase 1)
- Phase 6: ~0.5 day (most already written)

Total: ~6.5 developer-days.

---

## Edge Cases & Decision Log

| Case | Decision |
| :--- | :--- |
| Subscription omits a parameter that has a `DEFAULT` | Use the declared default. |
| Subscription omits a parameter with no default (bare `INPUT`) | Pass `NULL`. |
| Subscription sets a parameter to `NULL` explicitly | Pass `NULL` (same outcome). |
| `RELDATE` expression is invalid | `ExecutionException` at resolve time — subscription delivery fails, logged in history. |
| `LIST` value is empty string | Treated as empty list → `NULL`. |
| Report script is updated to remove an INPUT parameter that subscriptions reference | Stored parameter is silently ignored; it is not an error. |
| Report script is updated to add a new required INPUT parameter | Existing subscriptions will get `NULL` for the new parameter. |
| Non-INPUT `DECLARE` targeted by a subscription `PARAMETERS(...)` clause | Parser/handler should accept it (the PARAMETERS clause is just a `SET @var = value` before execution); no restriction needed. |
| Two subscriptions for the same report with different parameters | Fully supported — each subscription stores its own parameter set independently. |
| `ALTER SUBSCRIPTION` with `PARAMETERS()` empty | Clears all parameters from the subscription. Subsequent runs use script defaults / NULL. |

---

## Out of Scope (This Feature)

- **RELDATE in non-subscription contexts**: `RELDATE` resolves fine in any script context (manual run, `dotnet run --project src/ETL-SQL.App -- run`, Orchestrator job). The resolver does not distinguish. The subscription use case is the motivator but there's no special-casing.
- **Per-user default parameters on a report**: Defaults live in the script.
- **Parameter validation rules beyond type parsing**: No min/max constraints, no allowed-values lists at the subscription layer. The report script handles its own validation.
- **Encrypted parameter values**: SMTP passwords are encrypted; subscription parameters are not sensitive by design. If a parameter needs to be secret (e.g. an API key), it should not be a subscription parameter.
- **UI parameter dependency / cascading**: Slicer-style cascading filters are a dashboard feature, not a subscription feature. Subscriptions take flat key/value pairs.
