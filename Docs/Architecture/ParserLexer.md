# ETL-SQL Parser & Lexer Deep Dive

This document is the primary reference for developers adding new statement types, modifying grammar, or debugging parse errors. For the higher-level architecture overview see [Engine.md](Engine.md).

---

## 1. Overview

```
Source text (.etlsql / .rptsql)
        │
        ▼
┌─────────────────────────────────────────────────┐
│  Lexer  (ETL-SQL.Core/Parser/Lexer.cs)          │
│  source → List<Token>                           │
│  Single-pass; 1-char lookahead for operators    │
└─────────────────────────────┬───────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────┐
│  Parser  (ETL-SQL.Core/Parser/Parser.cs          │
│          + StatementParser.*.cs partials)        │
│  tokens → Script { Statements, Diagnostics }    │
│  Recursive descent; up to 3-token lookahead     │
│  Selective 1-token backtracking                 │
└─────────────────────────────┬───────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────┐
│  ExpressionParser  (ExpressionParser.cs)         │
│  7-level operator precedence chain              │
└─────────────────────────────────────────────────┘
```

---

## 2. Lexer

**File:** `ETL-SQL.Core/Parser/Lexer.cs`

### 2.1 Token Record

```csharp
public record Token(TokenType Type, string Value, int Line, int Column, int Offset);
```

`Offset` is the absolute character position in the source string, used for go-to-definition and diagnostics.

### 2.2 Tokenization Pass

The lexer is a **single-pass, single-character-lookahead** scanner. `Tokenize()` walks the source string character by character:

| Input | Result |
|-------|--------|
| Whitespace | Skipped |
| `--` | `COMMENT` token (line comment to EOL) |
| `/*` | `COMMENT` token (block; supports `/*@ tag: value */` metadata) |
| `#word` or `@@word` | Temp table / system variable identifier |
| `@word` | Variable identifier (`@VariableName`) |
| Letter or `_` | Identifier lookup in keyword dictionary → keyword token or `IDENTIFIER` |
| `'text'` | `STRING` literal (double `''` = escaped quote) |
| `"text"` or `[text]` | Quoted identifier — except `[a,b]` or `['x']` which become list literals |
| Digit | `NUMBER` literal (optional decimal `.`) |
| Operators | Single-char or two-char (`<=`, `<>`, `!=`, `>=`) |

**Bracket disambiguation:** When the lexer sees `[`, it scans ahead to the matching `]`. If the content contains `,` or a `'`, it is treated as a list literal `LIST`; otherwise it is a quoted identifier `QUOTED_IDENTIFIER`.

### 2.3 Keyword Registry

All keywords are registered in a `static Dictionary<string, TokenType>` at class initialization. The dictionary is case-insensitive.

**Reserved keywords** (always lexed as their token type, never as `IDENTIFIER`):  
`SELECT`, `FROM`, `WHERE`, `INSERT`, `UPDATE`, `DELETE`, `CREATE`, `DROP`, `INTO`, `AS`, `ON`, `JOIN`, `AND`, `OR`, `NOT`, `IN`, `IS`, `NULL`, `IF`, `ELSE`, `WHILE`, `FOR`, `BEGIN`, `END`, `DECLARE`, `SET`, `RETURN`, `WITH`, `UNION`, `EXCEPT`, `INTERSECT`, `ORDER`, `GROUP`, `BY`, `HAVING`, `CASE`, `WHEN`, `THEN`, `CAST`, `OVER`, `PARTITION`, `EXISTS`, `DISTINCT`, `ALL`, `TOP`, `LIMIT`, `OFFSET`, `FETCH`, `NEXT`, `ONLY`, …

**Non-reserved / context-sensitive keywords** (lexed as their token type but accepted as identifiers by the parser outside their specific contexts — see §3.3):  
Report-SQL: `VISUAL`, `PAGE`, `DATASET`, `LAYOUT`, `MAPPINGS`, `OPTIONS`, `ACTIONS`, `STRUCTURE`, `MAP`, `SERIES`, `SLICER`, `CARD`, `HEATMAP`, `DONUT`, `HBAR`, `BOXPLOT`, `TREEMAP`, `COLORS`, `STYLE`, `CONTAINER`, `NAVIGATION`, `COMBO`, `DATEPICKER`, `SLIDER`, `MULTISELECT`, `SEARCH`, `REFRESH`, `TTL`, `KEYFILE`, `X_AXIS`, `Y_AXIS`, `TITLE`, `SUBTITLE`

---

## 3. Parser

**Files:** `Parser.cs` (token stream, lookahead, dispatch), `StatementParser.cs` + domain-specific partials

### 3.1 Token Stream API (`IParser`)

```csharp
Token Current   // token at current position
Token Peek      // Current + 1
Token Peek2     // Current + 2
Token LookAhead(int distance)  // arbitrary lookahead

bool Match(TokenType t)        // consume and return true if current matches
Token Consume(TokenType t, string errorMsg)  // consume or throw SyntaxException
Token ConsumeIdentifier(string errorMsg)     // consume any identifier-like token
Token Advance()                // move forward one position
void Backtrack()               // move back one position (used sparingly)
bool IsIdentifier(Token t)     // true for IDENTIFIER + non-reserved keywords
```

**Maximum lookahead in practice:** 3 tokens (`Peek2`). `LookAhead(n)` is used in a small number of ambiguous grammar rules.

### 3.2 Statement Dispatch

`ParseStatement()` in `StatementParser.cs` uses a keyword-based dispatch table across ~37 statement types:

```
Match WITH          → ParseStatementWithCte()
Match CREATE        → ParseCreate()
  Match CONNECTION  → ParseCreateConnection()
  Match TABLE       → ParseCreateTable()
  Match PROCEDURE   → ParseCreateProcedure()
  Match FUNCTION    → ParseCreateFunction()
  Match INDEX       → ParseCreateIndex()
  Match VISUAL      → ParseCreateVisual()
  Match PAGE        → ParseCreatePage()
  Match DATASET     → ParseCreateDataset()
  Match CONTAINER   → ParseCreateContainer()
  Match NAVIGATION  → ParseCreateNavigation()
Match SELECT / (  → ParseQuery() via backtrack
Match INSERT        → ParseInsert()
Match UPDATE        → ParseUpdate()
Match DELETE        → ParseDelete()
Match MERGE         → ParseMerge()
Match IF            → ParseIf()
Match WHILE         → ParseWhile()
Match FOR           → ParseFor()
Match FOREACH       → ParseForeach()
Match BEGIN         → ParseBlock()
Match EXECUTE/CALL  → ParseExecute()
Match RUN           → ParseRunScript()
Match DECLARE       → ParseDeclare()
Match SET           → ParseSet() or ParseSetReportMetadata()
Match PRINT/RAISE   → ParsePrint() / ParseRaise()
Match DROP          → ParseDrop()
Match TRUNCATE      → ParseTruncate()
Match WAITFOR       → ParseWaitFor()
Match PARALLEL      → ParseParallel()
Match SEND          → ParseSend()
Match SHOW          → ParseShow()
Match USE           → ParseUse()
Match DOCKER        → ParseDocker()
Match ASSERT        → ParseAssert()
```

### 3.3 Context-Sensitive Keyword Handling

Non-reserved keywords produce their own `TokenType` from the lexer but are accepted as identifiers by the parser in any position where an identifier is valid. This is controlled by `IsIdentifier(Token t)`:

```csharp
public bool IsIdentifier(Token t) =>
    t.Type == TokenType.IDENTIFIER
    || (t.Type >= TokenType.VISUAL && t.Type <= TokenType.SEARCH)  // Report-SQL range
    || t.Type == TokenType.REFRESH || t.Type == TokenType.TTL
    || t.Type == TokenType.KEYFILE || ... ;  // other context-sensitive tokens
```

`ConsumeIdentifier()` internally calls `IsIdentifier()`, so any grammar position that says "identifier" will accept these keywords transparently.

### 3.4 CTE Parsing (`WITH` clause)

```
WITH cte_name AS ( query )
     [, cte_name2 AS ( query ) ]*
     [RECURSIVE]
     final_select_statement
```

`ParseStatementWithCte()` collects `CteDefinition` records (name + `SelectStatement` body) and attaches them to the final `SelectStatement.Ctes` list. The `RECURSIVE` keyword sets `SelectStatement.IsRecursive`.

### 3.5 Ambiguous Grammar Resolution

| Ambiguity | Resolution |
|-----------|-----------|
| `CREATE TABLE` vs `CREATE CONNECTION` | Consume `CREATE`, peek next token — `TABLE` vs `CONNECTION` keyword |
| `SELECT` vs subquery in FROM | `(` before `SELECT` forces the subquery path; bare table name takes the identifier path |
| `(SELECT …)` scalar subquery | `ParsePrimary()` checks for `LPAREN` followed by `SELECT` keyword |
| Table function vs table name | `ParseTableReference()` checks for `(` after the name |
| `AS` in CTE vs `AS` in alias | After `WITH cte_name`, mandatory `AS` is consumed before `(`; elsewhere `AS` is optional alias |
| `CREATE VISUAL` — visual type as identifier | Fallback identifier switch in `ParseVisualType()` handles tokens that may arrive as `IDENTIFIER` |

**Backtracking:** Used in one specific case — `ParseCreate()` backtracks one token when it cannot determine whether it is looking at `CREATE TABLE`, `CREATE INDEX`, or a vendor DDL passthrough.

### 3.6 Error Recovery

```csharp
// In Parser.Parse()
while (!AtEnd())
{
    try
    {
        statements.Add(ParseStatement());
    }
    catch (SyntaxException ex)
    {
        script.Diagnostics.Add(new Diagnostic(ex));
        SkipToNextSemicolon();  // advance past `;` or EOF
    }
}
```

After catching a `SyntaxException`, the parser skips to the next `;` and continues. This means:
- A script with multiple statements continues executing past parse errors
- All syntax errors are collected in `Script.Diagnostics` rather than thrown immediately
- The language server publishes all errors in one batch via `PublishDiagnosticsParams`

---

## 4. Expression Parser

**File:** `ExpressionParser.cs`

The expression parser is a classic **recursive descent** implementation with explicit precedence levels:

```
ParseExpression()
  └─ ParseOr()
       └─ ParseAnd()
            └─ ParseNot()
                 └─ ParseComparison()    = <> < > <= >= IN LIKE IS [NOT NULL]
                      └─ ParseTerm()     + -
                           └─ ParseFactor()   * / %
                                └─ ParsePrimary()
```

### 4.1 ParsePrimary

Handles:
- Literals (`NUMBER`, `STRING`, `NULL`, `TRUE`, `FALSE`)
- Parenthesized expressions and scalar subqueries `(SELECT …)`
- `CASE WHEN … THEN … [ELSE …] END`
- `CAST(expr AS type)`
- `EXISTS (SELECT …)`
- `SUBSTRING(…)`, `POSITION(…)`, `OVERLAY(…)`, `TRIM(…)`, `EXTRACT(…)`
- Function calls `name(args…)` — with optional `OVER (…)` window clause
- Variable references `@var`, `@@system`
- Identifiers (column names, table aliases)

### 4.2 Window Functions

Window clause parsed in `ParsePrimary()` after detecting `OVER`:

```
FUNCTION(args) OVER (
  [PARTITION BY col, ...]
  [ORDER BY col [ASC|DESC], ...]
  [ROWS|RANGE BETWEEN frame_start AND frame_end]
)
```

Stored as `FunctionCallExpression.Window: WindowClause`.

### 4.3 Operator Precedence (by level, lowest first)

| Level | Operators |
|-------|-----------|
| 9 (lowest) | `OR` |
| 8 | `AND` |
| 7 | `NOT` (unary) |
| 6 | `=`, `<>`, `<`, `>`, `<=`, `>=`, `IN`, `NOT IN`, `LIKE`, `NOT LIKE`, `IS NULL`, `IS NOT NULL` |
| 5 | `+`, `-` |
| 4 | `*`, `/`, `%` |
| 3 | Unary `-` |
| 2 | `EXISTS`, `CAST`, `CASE`, special forms |
| 1 (highest) | Literals, function calls, parentheses |

---

## 5. AST Node Reference

**File:** `ETL-SQL.Core/Ast.cs`

All nodes are `record` types (immutable). `AstNode` is the base record:

```csharp
public abstract record AstNode
{
    public int Line   { get; init; }
    public int Column { get; init; }
}
```

### Core Statement Types

| Record | Key fields |
|--------|-----------|
| `Script` | `List<Statement> Statements`, `List<Diagnostic> Diagnostics`, metadata dict |
| `SelectStatement` | `Columns`, `FromTable`, `Joins`, `Where`, `GroupBy`, `Having`, `OrderBy`, `Limit`, `Offset`, `Ctes`, `IntoTable`, `IsRecursive`, `IsDistinct`, `TopCount` |
| `SetOperationStatement` | `Left`, `Right`, `Operator` (UNION/EXCEPT/INTERSECT), `IsAll` |
| `InsertStatement` | `TargetTable`, `Columns`, `Source` (SELECT or VALUES) |
| `UpdateStatement` | `TargetTable`, `Assignments`, `Where`, `Joins` |
| `DeleteStatement` | `TargetTable`, `Where` |
| `MergeStatement` | `Target`, `Source`, `OnCondition`, `WhenMatched`, `WhenNotMatched` |
| `DeclareStatement` | `VariableName`, `DataType`, `InitialValue`, `IsInput`, `IsOutput`, `IsSensitive` |
| `SetVariableStatement` | `VariableName`, `Value` |
| `IfStatement` | `Condition`, `ThenBody`, `ElsifClauses`, `ElseBody` |
| `WhileStatement` | `Condition`, `Body` |
| `ForStatement` | `VariableName`, `Start`, `End`, `Step`, `Body` |
| `ForeachStatement` | `VariableName`, `Source`, `Body` |
| `BlockStatement` | `Statements: List<Statement>` |
| `TryCatchStatement` | `TryBody`, `CatchBody`, `ErrorVariable` |
| `ReturnStatement` | `Value?: Expression` |
| `BreakStatement`, `ContinueStatement` | — |
| `RunScriptStatement` | `PathExpression`, `Parameters` |
| `ExecuteStatement` | `ProcedureName`, `Parameters` |
| `CreateProcedureStatement` | `Name`, `Parameters: List<ParameterDefinition>`, `Body` |
| `CreateFunctionStatement` | `Name`, `Parameters`, `ReturnType`, `Body` |

### Expression Types (see also §4 above)

| Record | Key fields |
|--------|-----------|
| `LiteralExpression` | `Value: object?`, `Type: TokenType` |
| `IdentifierExpression` | `Name: string` |
| `VariableExpression` | `Name: string` (includes `@`, `@@`, `#`) |
| `BinaryExpression` | `Left`, `Operator: TokenType`, `Right` |
| `UnaryExpression` | `Operator: TokenType`, `Expression` |
| `FunctionCallExpression` | `FunctionName`, `Arguments`, `Window?: WindowClause`, `IsDistinct`, `WithinGroupOrderBy` |
| `CaseExpression` | `WhenClauses: List<(Condition, Result)>`, `ElseResult?` |
| `InExpression` | `Left`, `Right` (list or subquery), `IsNot` |
| `LikeExpression` | `Left`, `Pattern`, `IsNot`, `EscapeChar?` |
| `IsNullExpression` | `Expression`, `Not` |
| `ExistsExpression` | `Subquery: Statement`, `IsNot` |
| `SubqueryExpression` | `Query: Statement` |
| `CastExpression` | `Expression`, `TargetType: string` |
| `SubstringExpression` | `String`, `Start`, `Length?` |
| `ExtractExpression` | `Field: string`, `Source` |
| `TrimExpression` | `Type: TrimType`, `Characters?`, `String` |
| `WindowFunctionExpression` | `Function: FunctionCallExpression`, `Window: WindowClause` |

---

## 6. Adding a New Statement Type

1. **Add token(s) to `TokenType.cs`** — one entry per new keyword not already covered.
2. **Register keyword(s) in `Lexer.cs`** — add to the `Keywords` dictionary.
3. **Add AST record to `Ast.cs`** (or `ReportAst.cs` for Report-SQL) — must be a `record` inheriting `Statement`.
4. **Add parser case** in the appropriate `StatementParser.*.cs` partial:
   - If it starts with a new keyword: add a `Match(TokenType.NEW_KW)` branch in `ParseStatement()`
   - If it starts with an existing dispatch keyword (e.g., `CREATE`): add a nested branch in the existing handler
5. **Add statement handler** in `ETL-SQL.Engine/Handlers/` implementing `IStatementHandler`
6. **Register handler** in `DependencyInjectionSetup.cs`

**Checklist for token conflicts:** Run the test suite and check the LanguageServer `SignatureHelpProvider` hard-coded list — new keywords may need to be added to `IsIdentifier()` if they should be usable as identifiers in other contexts.
