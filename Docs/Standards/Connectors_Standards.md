# ETL-SQL Connectors Engineering Standards

This document defines the mandatory engineering and design standards for developing and maintaining Data Connectors within the ETL-SQL ecosystem. All connector implementations MUST adhere to these rules to ensure security, portability, and dialect awareness.

---

## 1. Core Engineering Principles

Connectors are the 'Sensors' of the ETL-SQL engine. They must remain isolated from the execution logic while providing structured metadata and data streams.

### 1.1 Dialect Isolation & Awareness
Connectors are responsible for declaring their native dialect constraints to the engine's Linter.
- **Rule**: Connectors MUST implement `GetExcludedKeywords()` to inform the engine of keywords that are invalid in their target dialect.
- **Rule**: Connectors MUST handle provider-specific exception wrapping.

**Engineering Pattern:**
```csharp
// CORRECT: Declarative Awareness
public IReadOnlyList<string> GetExcludedKeywords() => new[] { "TOP", "DATALENGTH" };

// INCORRECT: Attempting to rewrite the query manually within the connector.
```

### 1.2 Resource Lifecycle Management
Connectors must be 'Good Citizens' of the system memory and handle connection pools strictly.

---

## 2. Inviolable Governance Rules

### 2.1 Security & Path Resolution
Path resolution is a critical security guardrail. Connectors do not have the authority to bypass the Security Service.
- **Rule**: Connectors MUST NOT use relative paths.
- **Rule**: All file-based connectors MUST use the `IExecutionContext.ResolvePath()` utility.

**Engineering Pattern:**
```csharp
// CORRECT: Security-Aware Resolution
string safePath = context.ResolvePath(sourceString);
using var stream = File.OpenRead(safePath);

// INCORRECT: Bypassing the guardrails
using var stream = File.OpenRead(sourceString); // SECURITY VULNERABILITY
```

### 2.3 Credential Masking
Connectors are responsible for sanitizing their own metadata to prevent secret leakage in logs and diagnostics.
- **Rule**: All sensitive properties (`PASS`, `KEY`, `TOKEN`, `SECRET`) MUST be masked with `***` in `GetOptionValues()`, `GetSupportedOptions()`, and `SHOW CONNECTION` outputs.
- **Rule**: Never include plain-text connection strings in `ExecutionException` messages.

### 2.4 Encrypted Asset Portability
Every connector must support the engine's portable encryption scheme.
- **Rule**: Connectors MUST support the `ENC:` prefix for all credential fields.
- **Rule**: Decryption must be performed via the `SecurityService` before provider instantiation.

### 2.5 Pushdown optimization Contract
To ensure high-performance execution, SQL-like connectors SHOULD NOT rely solely on row-by-row iteration.
- **Rule**: All Relational or query-capable providers MUST implement `IDatabaseSource`.
- **Rule**: `SupportsSqlPushdown` must be set to `true` to enable MPP (Massive Parallel Processing).
- **Rule**: Connectors MUST provide a valid `DialectProfile` to the engine's optimizer.

### 2.2 Dependency Injection & Logging
- **Rule**: Use the `ILogger` provided during `CreateDataSource` or the `IExecutionContext` to record events.
- **Rule**: All diagnostic logs MUST be sanitized. Never log connection strings or credentials.

---

## 3. Violation Indicators (Code Smells)

| Indicator | Severity | Description |
| :--- | :--- | :--- |
| `System.IO.File.OpenRead(path)` | **CRITICAL** | Bypasses `SecurityService` path resolution. |
| `Console.WriteLine(...)` | **MAJOR** | Bypasses the unified logging sink. |
| `new SqlConnection(...)` | **MAJOR** | Direct instantiation in the connector logic. |
| `using (var r = cmd.ExecuteReader())` | **MINOR** | Blocking I/O. Use `ExecuteReaderAsync`. |

---

## 4. Compliance Checklist (New Connector)

Before a new connector is merged into the `ETL-SQL.Connectors` layer, it must pass this audit:

- [ ] Does it implement `IConnector` and `IDataSource`?
- [ ] Are all path operations validated via the `SecurityService`?
- [ ] Does it provide a full list of `GetSupportedOptions()` for the UI/Linter?
- [ ] **Security**: Are all secrets masked in metadata and `SHOW CONNECTION` outputs?
- [ ] **Security**: Does it handle `ENC:` prefixes via the `SecurityService`?
- [ ] **Performance**: Does it implement `IDatabaseSource` (for SQL targets)?
- [ ] **Performance**: Is a correct `DialectProfile` provided for query pushdown?
- [ ] Is every provider exception caught and re-thrown as an `ExecutionException`?
- [ ] Does it support `SET WHAT_IF` dry-run modes?
- [ ] Have all keywords and functions for this dialect been declared via `GetExcludedKeywords`?

---
*Refer to [Connectors_Architecture.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Architecture/Connectors_Architecture.md) for technical implementation details.*
