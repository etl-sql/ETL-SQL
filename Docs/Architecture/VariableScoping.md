# ETL-SQL Variable Scoping, Procedures & Dynamic Execution

This document covers how `@variables` are stored and scoped, how procedures and user-defined functions work, and how `RUN SCRIPT` / `EXECUTE` create isolated execution contexts with parameter passing and output propagation.

---

## 1. Overview

```
Session (global scope)
    │
    ├─ @globalVar ─────── declared at top-level, visible everywhere
    │
    ├─ PROCEDURE Foo (@in INT, @out INT OUTPUT)
    │       PushScope({ @in, @out })
    │       │  body executes
    │       PopScope → @out mapped back
    │
    ├─ RUN SCRIPT 'child.etlsql' @param = @globalVar
    │       PushScope({ @param })
    │       │  child script executes
    │       PopScope → OUTPUT vars mapped back
    │
    └─ FOR @i = 1 TO 10     (no new scope — @i in current scope)
           body executes with @i changing each iteration
```

---

## 2. `VariableScopeManager`

**File:** `ETL-SQL.Engine/Services/VariableScopeManager.cs`

The single source of truth for all variable state. `Evaluator` holds one instance and exposes it to handlers via `IExecutionContext`.

### Internal State

| Field | Type | Role |
|-------|------|------|
| `_variables` | `Dictionary<string, object?>` | Global (session-level) variables |
| `_variableMetadata` | `Dictionary<string, VariableMetadata>` | Metadata flags for global variables |
| `_scopeStack` | `Stack<Dictionary<string, object?>>` | Nested local scopes (LIFO) |
| `_metadataStack` | `Stack<Dictionary<string, VariableMetadata>>` | Metadata mirror of `_scopeStack` |
| `_procedures` | `Dictionary<string, CreateProcedureStatement>` | Registered procedure definitions |
| `_functions` | `Dictionary<string, CreateFunctionStatement>` | Registered user-defined functions |

All dictionaries are **case-insensitive** (`StringComparer.OrdinalIgnoreCase`).

### Scope Resolution Order

When any code reads `@var`, the lookup order is:

1. Top of `_scopeStack` (most-local scope)
2. Next scope down the stack
3. … (all nested scopes, innermost first)
4. `_variables` (global scope)
5. `null` if not found anywhere

When code **writes** `@var` via `SET`, the manager finds the first scope containing the variable and updates it there. If the variable isn't found anywhere, a runtime error is thrown (unlike JavaScript `var` — undeclared assignment is always an error).

### Key Methods

| Method | Behavior |
|--------|----------|
| `DeclareVariable(name, value, metadata)` | Creates variable in the **current scope** (top of stack, or global if stack empty) |
| `SetVariable(name, value)` | Finds variable in stack/globals and updates it in place; throws if not found |
| `GetVariable(name)` | Searches stack top-down then globals; returns `null` if absent |
| `ContainsVariable(name)` | Returns true if found anywhere in the scope chain |
| `PushScope(vars, metadata)` | Adds a new scope layer on top of the stack |
| `PopScope()` | Removes the top scope layer (always call in a `finally` block) |
| `GetVariablesWithMetadata(predicate)` | Returns all variables from all scopes matching the predicate |
| `Fork()` | Copies globals and stack frames for `PARALLEL` execution |
| `Merge(spawned)` | Syncs global variables back from a forked scope |

### `VariableMetadata` Flags

| Flag | Set by | Meaning |
|------|--------|---------|
| `IsDeclared` | `DECLARE` statement | Variable was explicitly declared (not auto-created) |
| `IsInput` | `RUN SCRIPT` / `EXECUTE` parameter | Variable is an input-only parameter in this scope |
| `IsOutput` | `DECLARE … OUTPUT` | Variable's final value is propagated back to the caller's scope |
| `IsSensitive` | `DECLARE … SENSITIVE` | Variable value is masked in logs and diagnostics |

---

## 3. DECLARE and SET

### `DECLARE @var [AS type] [= expr]`

**Handler:** `DeclareStatementHandler`

1. If `InitialValue` provided: evaluate expression; cast to declared `DataType` via `CastToType()`
2. If no initial value but metadata flags are set AND the variable already exists in the current scope: preserve the existing value (re-declaration preserves value)
3. If variable already declared in the same scope (`IsDeclared = true`): throw `DuplicateDeclareException`
4. Call `context.DeclareVariable(name, value, metadata)`

**Scope placement:** Variable goes into the **current scope only** (top of stack if inside a procedure/script, otherwise global).

### `SET @var = expr`

**Handler:** `SetVariableStatementHandler`

1. Check `ContainsVariable(name)` — throw if not found (undeclared assignment is an error)
2. Evaluate expression
3. Call `context.SetVariable(name, value)` — updates the variable wherever it lives in the scope chain

---

## 4. Control Flow Scoping

**Important:** `IF`, `WHILE`, `FOREACH`, `FOR`, and `BEGIN…END` do **not** push new scopes. Variables declared inside a block are visible in the enclosing scope and persist after the block exits.

### FOR loops

```sql
FOR @i = 1 TO 10
BEGIN
    DECLARE @squared = @i * @i;  -- lives in enclosing scope, survives loop
    ...
END
```

`ForStatementHandler` declares `@i` in the current scope if it doesn't exist; updates it each iteration. All variables declared inside the body land in the same scope.

### FOREACH loops

Same behavior: `@item` declared in current scope. For remote SQL sources with `ORDER BY`, the handler converts the loop into paginated `LIMIT`/`OFFSET` queries to avoid materializing the entire result set.

### TRY…CATCH

`TryCatchStatement` executes both branches in the current scope. If `ErrorVariable` is specified (e.g., `CATCH @err`), `@err` is declared in the current scope before the catch body runs.

---

## 5. RUN SCRIPT

**Handler:** `RunScriptStatementHandler`

```sql
RUN SCRIPT 'path/child.etlsql' @param1 = @sourceVar, @param2 = 'literal';
```

**Execution flow:**

1. Evaluate the path expression
2. Read and parse the child script file
3. Build `localVars` dict: for each parameter expression, evaluate it and store under the parameter name
4. Mark all parameters as `IsInput = true` in metadata
5. `PushScope(localVars, metadata)` — child script runs in an **isolated scope**
6. `context.Evaluate(childScript)` — evaluates the full child script
7. Capture all variables from the nested scope: `GetVariablesWithMetadata(v => true)`
8. `PopScope()`
9. Map results back:
   - For each parameter whose value-expression was a `VariableExpression` or `IdentifierExpression` pointing to a parent variable, write the updated value back via `context.SetVariable()`
   - For any variable in the child scope marked `IsOutput = true`, write it back to the parent scope under the same name

**Key constraint:** The child script does **not** inherit the parent's variables. All data must flow through explicit parameters. This is by design — it enforces interface contracts between scripts.

---

## 6. EXECUTE (Procedure Call)

**Handler:** `ExecuteStatementHandler`

```sql
EXECUTE ProcedureName @param1 = 42, @result OUTPUT;
```

**Two modes:**

**Mode A — Stored Procedure:**
- Calls `context.EvaluateProcedure(name, args)`
- `ProcedureExecutor` binds positional arguments to parameter names, pushes scope, runs body, catches `ReturnException`, pops scope
- OUTPUT parameters: `VariableMetadata.IsOutput = true` on those parameters; after execution their values are mapped back to the caller's scope

**Mode B — Script File (if path resolves to an `.etlsql` file):**
- Similar to `RUN SCRIPT` but uses the `IsOutput` flag from the EXECUTE parameter list to determine what to map back

### Parameter Binding

`ProcedureExecutor.BuildParameterDictionary()` maps by position:

```
args[0] → parameters[0].Name
args[1] → parameters[1].Name
...
extra args → ignored
missing args → null
```

There is no keyword-argument syntax. Arguments are always positional.

---

## 7. User-Defined Functions

**Handler:** `CreateFunctionStatementHandler` (registers), `ExpressionEvaluator.EvaluateFunction()` (calls)

```sql
CREATE FUNCTION GetFullName(@first VARCHAR, @last VARCHAR) RETURNS VARCHAR
AS BEGIN
    RETURN @first + ' ' + @last;
END
```

Functions execute in an isolated scope identical to procedures. `ReturnException` carries the return value out. If no `RETURN` is hit, the function returns `null`.

Functions are dispatched **before** the built-in `FunctionRegistry` in `EvaluateFunction()`, so user-defined functions can shadow built-in names within their script scope.

---

## 8. PARALLEL Execution

`PARALLEL` blocks execute statements concurrently. Each branch gets a **forked** `VariableScopeManager`:

```csharp
var fork = _scopeManager.Fork();  // copies globals and stack frames
// each branch runs with its own fork
// after all branches complete:
foreach (var fork in completedForks)
    _scopeManager.Merge(fork);  // sync global variables back
```

`Fork()` copies the variable dictionaries (globals and stack frames) so branches don't interfere. `Merge()` writes the forked global state back to the parent, with the last-writer-wins semantic for any variable written by multiple branches.

---

## 9. Session Persistence

`GetGlobalState()` snapshots the current global `_variables` and `_variableMetadata` dictionaries. `LoadGlobalState()` restores them. This is used by the session manager to persist variable state between REPL invocations.

Variables marked `IsSensitive = true` are excluded from session snapshots and never written to the REPL `variables` notification payload.
