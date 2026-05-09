# ETL-SQL Language Server Architecture

This document describes the internal design of `ETL-SQL-LSP` — the LSP server that powers IDE features (completions, diagnostics, hover, navigation, formatting) for `.etlsql` and `.rptsql` files.

For the VS Code extension that hosts this server, see [VSCodeExtension.md](VSCodeExtension.md).

---

## 1. Overview

```
Editor (VS Code / JetBrains)
        │  LSP messages (stdio)
        ▼
┌──────────────────────────────────────────────────────────────┐
│  ETL-SQL.LanguageServer  (OmniSharp.Extensions.LanguageServer)│
│                                                              │
│  TextDocumentHandler ──► Lex → Parse → Metadata → Lint       │
│                              │            │                  │
│                              ▼            ▼                  │
│                      DocumentStateStore  MetadataManager     │
│                              │            │                  │
│       ┌──────────────────────┤            │                  │
│       ▼           ▼          ▼            ▼                  │
│  CompletionProvider HoverProvider  DefinitionProvider        │
│  SignatureHelpProvider  FormattingProvider                   │
│  CustomMethodsHandler   RefreshMetadataHandler               │
└──────────────────────────────────────────────────────────────┘
```

**Transport:** Stdio — the server reads LSP messages from `stdin` and writes responses to `stdout`. The VS Code Language Client spawns the server as a child process on extension activation.

---

## 2. Startup & Dependency Injection

**Entry point:** `Program.cs`

```csharp
var server = await LanguageServer.From(options => options
    .WithInput(Console.OpenStandardInput())
    .WithOutput(Console.OpenStandardOutput())
    .WithServices(services =>
    {
        // Connectors
        services.AddSingleton<IConnectorRegistry>(registry =>
        {
            registry.Register(new MockDbConnector());
            registry.Register(new FlatFileConnector());
            registry.Register(new SqlServerConnector());
            registry.Register(new PostgresConnector());
            registry.Register(new OracleConnector());
        });
        services.AddSingleton<IMetadataManager, MetadataManager>();
        services.AddSingleton<DocumentStateStore>();
    })
    .WithHandler<TextDocumentHandler>()
    .WithHandler<CompletionProvider>()
    .WithHandler<HoverProvider>()
    .WithHandler<DefinitionProvider>()
    .WithHandler<SignatureHelpProvider>()
    .WithHandler<FormattingProvider>()
    .WithHandler<CustomMethodsHandler>()
    .WithHandler<RefreshMetadataHandler>());
```

All handlers share the same `IMetadataManager` and `DocumentStateStore` singletons via constructor injection.

---

## 3. Analysis Pipeline (`TextDocumentHandler`)

`TextDocumentHandler` implements `TextDocumentSyncHandlerBase` and triggers re-analysis on `didOpen`, `didChange`, and `didSave`. The analysis pipeline runs in `AnalyzeAsync()`:

```
Document text
    │
    ▼
1. Lexer(text).Tokenize()
    │
    ▼
2. Parser(tokens).Parse()  →  Script { Statements, Diagnostics }
    │
    ├─ 3. Metadata Discovery
    │       Scan statements for:
    │         CreateConnectionStatement  → register document connection
    │         SelectStatement.IntoTable  → register temp table
    │         CreateTableStatement       → register temp table
    │         DockerStatement            → register docker alias as connection
    │       Notify client: etlsql/scriptConnections
    │
    ├─ 4. Lineage Analysis
    │       ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(script)
    │       Store tracker in DocumentStateStore
    │
    ├─ 5. Parser Diagnostics → LSP Diagnostics
    │       script.Diagnostics → PublishDiagnosticsParams
    │
    └─ 6. Linting
            Linter.AnalyzeAsync(script, lintContext)
              lintContext.Metadata = LanguageServerMetadataProvider(metadataManager)
            Lint results → PublishDiagnosticsParams (appended)
```

Parser diagnostics and lint diagnostics are merged into a single `PublishDiagnosticsParams` call so the editor shows all issues together.

---

## 4. State Management

### `DocumentStateStore`

Keyed by document URI. Stores:
- Parsed `Script` object (AST)
- Raw document text
- `ILineageTracker` (used by hover for lineage display)

All feature providers call `store.TryGetState(uri)` to read the current parsed state without re-parsing.

### `MetadataManager`

Manages schema metadata with a `ConcurrentDictionary` cache. Supports two scopes:

| Scope | Description |
|-------|-------------|
| Global connections | Set via `etlsql/setConnections` notification on extension startup; persist across documents |
| Document connections | Discovered per-document by `TextDocumentHandler`; cleared on document close |

**Schema fetch:** When a table or column list is requested and not cached, `MetadataManager` instantiates the appropriate connector from `IConnectorRegistry`, connects, reads schema, caches, and returns. Subsequent requests for the same connection hit the cache.

---

## 5. Feature Providers

### 5.1 Completions (`CompletionProvider`)

Trigger characters: `" "`, `"."`, `"*"`

The completion engine resolves context from the cursor position:

| Context | Completions returned |
|---------|----------------------|
| After `.` on a connection alias | Tables for that connection |
| After `.` on `conn.table` | Columns of that table |
| After `FROM`, `JOIN`, `INTO` | Known connections + default table list |
| After `*` or `alias.*` | Full column expansion (replaces `*` with comma-separated list) |
| After `SHOW` | `DATASETS`, `JOBS`, `CONNECTIONS`, `TABLES`, `COLUMNS`, `VARIABLES`, `VERSION`, `LINEAGE`, `TAGS`, `PROFILE`, `ACTIVE` |
| After `USE` | `DATASET`, `DOCKER`, `SETS`, `PASSWORD` |
| After `USE DATASET` or prefix starts with `&` | Dataset names from `DatasetStore` — `&name`, with folder/row count/access level detail |
| After `SOURCE =` or prefix `&` | Dataset names from `DatasetStore` |
| Otherwise | Keywords (~50), built-in functions (~51), in-scope `@variables` |

**Variable discovery:** `CollectAvailableVariables()` walks the AST above the cursor position, collecting `DECLARE`, `SET`, `FOR`, and `FOREACH` variable bindings, respecting scope boundaries.

**Alias resolution:** `AliasScanner.Scan(script)` builds a map of table aliases to actual table names, used to resolve column lookups when the user types `alias.col`.

**Star expansion:** When `*` is typed after an alias, the provider fetches all columns for all tables in the current statement and generates a single `CompletionItem` whose `TextEdit` replaces `*` with the full column list.

**Dataset name completions:** `DatasetStore` holds a snapshot of portal datasets (loaded from portal.db via `etlsql/setPortalDbPath`). When active, dataset `&name` suggestions include a detail line showing folder path, row count, access level, and a staleness indicator.

### 5.2 Hover (`HoverProvider`)

Uses the `ILineageTracker` stored in `DocumentStateStore` to render a lineage graph at the hovered position.

- Hover over a `#temp` table → shows its lineage (source tables, transformations applied)
- Hover over a connection → shows its type and registered aliases
- Hover over `&datasetName` → shows a metadata card: folder, access level, row count, last refresh timestamp, TTL, and a staleness warning if applicable
- Rendering via `ETL_SQL.Analysis.Lineage.LineageGraphRenderer` (produces Markdown for the hover tooltip)

### 5.3 Go-to-Definition (`DefinitionProvider`)

Recursively walks the AST to find the declaration of the symbol under the cursor:

| Symbol | Declaration location |
|--------|----------------------|
| `@variable` | Nearest `DECLARE @var` or loop variable binding above the cursor |
| `#temp` | `SELECT ... INTO #temp` or `CREATE TABLE #temp` statement |
| Connection alias | `CREATE CONNECTION alias ...` statement |

Returns a `LocationOrLocationLinks` pointing to the start of the declaration token.

### 5.4 Signature Help (`SignatureHelpProvider`)

Hard-coded dictionary of ~100+ function signatures including:
- Built-in ETL-SQL functions (`GETDATE`, `DATEADD`, `DATEDIFF`, `REGEX_MATCH`, window functions, JSON functions)
- Connector options (`WITH(ServerName=, Database=, Username=, ...)`)

Activates when the cursor is inside a function call `(` and returns parameter hints with the active parameter highlighted based on comma count.

### 5.5 Document Formatting (`FormattingProvider`)

Delegates to `SqlFormatter.Format()` from `ETL_SQL.Core.Formatting`. Applies keyword casing, indentation, and clause alignment to the entire document.

---

## 6. Linting Integration

**Bridge class:** `LanguageServerMetadataProvider`

Implements `IMetadataProvider` (the interface expected by all `ILintRule` implementations in `ETL_SQL.Analysis.Linting.Rules`). Delegates every metadata query to `MetadataManager` with the document URI for connection scoping.

This means every linter rule that checks column existence, source table availability, or connection validity automatically benefits from the server's cached schema without any rule-level changes.

**Linter rules in scope for `.rptsql`:**
- `DatasetEncryptWithoutKeyRule` — ENCRYPT = KEYFILE without KEYFILE clause
- `PageVisualReferencedRule` — MAP slot references an undefined visual

**Severity mapping:**

| Lint severity | LSP `DiagnosticSeverity` |
|---------------|--------------------------|
| Error | Error |
| Warning | Warning |
| Info | Information |

---

## 7. Custom LSP Methods

Beyond the standard LSP protocol, the server exposes custom requests and notifications:

### Notifications (Client → Server)

| Method | Payload | Effect |
|--------|---------|--------|
| `etlsql/setConnections` | `Connection[]` | Register global connections in `MetadataManager` |
| `etlsql/setDebugMode` | `{ enabled: bool }` | Toggle verbose protocol logging |
| `etlsql/refreshMetadata` | `{ uri: string }` | Clear metadata cache for document; re-trigger analysis |
| `etlsql/setPortalDbPath` | `{ path: string \| null }` | Set path to portal.db; triggers a synchronous refresh of the `DatasetStore` cache. Send `null` or empty string to disable dataset awareness. |

### Requests (Client → Server)

| Method | Params | Returns |
|--------|--------|---------|
| `etlsql/getTables` | `{ connectionName, uri }` | `string[]` table names |
| `etlsql/getColumns` | `{ connectionName, tableName, uri }` | `ColumnInfo[]` |
| `etlsql/getViews` | `{ connectionName, uri }` | `string[]` view names |
| `etlsql/getTempTables` | `{ uri }` | `string[]` temp table names in document scope |

### Notifications (Server → Client)

| Method | Payload | Effect |
|--------|---------|--------|
| `etlsql/scriptConnections` | `{ uri, connections: Connection[] }` | Client updates the Connections sidebar with document-scoped connections |

---

## 8. Supported Connectors (Metadata)

The language server can connect to these sources to fetch live schema:

| Connector | Schema support |
|-----------|----------------|
| `MockDbConnector` | In-memory mock tables — always available |
| `FlatFileConnector` | Reads header row from CSV/flatfile to infer columns |
| `SqlServerConnector` | `sys.tables`, `sys.columns`, `INFORMATION_SCHEMA.VIEWS` |
| `PostgresConnector` | `pg_catalog`, `information_schema` |
| `OracleConnector` | `ALL_TABLES`, `ALL_TAB_COLUMNS` |

Connectors not in this list (SFTP, API, Azure Blob, etc.) return empty metadata — completions fall back to keyword-only suggestions for those connection types.

---

## 9. Extension Points

To add a new LSP capability:

1. Create a handler class implementing the OmniSharp interface (e.g., `ICodeActionHandler`)
2. Inject `DocumentStateStore` and/or `IMetadataManager` via constructor
3. Register with `.WithHandler<MyHandler>()` in `Program.cs`

To add metadata support for a new connector type:

1. Implement `IConnector` with schema-fetch methods
2. Register in `IConnectorRegistry` at startup in `Program.cs`
3. `MetadataManager` will automatically use it when that connection type is encountered
