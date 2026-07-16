# Engine Coding Standards

This document establishes the official coding standards and architectural principles for developers contributing to the C# source code of the **ETL-SQL** engine.

---

## 1. Zero-Trust Path Resolution

Security is a core pillar of ETL-SQL. To prevent directory traversal and access bypasses, all file I/O operations must respect the zero-trust boundary.

- **Rule**: Never use raw relative paths or unvalidated absolute paths in engine-level or connector-level file operations.
- **Enforcement**: You must call `IExecutionContext.ResolvePath(string path)` before executing any file I/O operation (reading, writing, copying, checking existence, etc.).
- **Example**:
  ```csharp
  // Incorrect: directly accessing path can bypass security rules
  if (File.Exists(targetPath)) { ... }

  // Correct: Resolve path through ExecutionContext first
  string resolvedPath = context.ResolvePath(targetPath);
  if (File.Exists(resolvedPath)) { ... }
  ```

---

## 2. Logging & Console Output

Direct console output is forbidden inside the engine core, libraries, and connectors to ensure clean logs, portability, and thread-safety.

- **No Obsolete Logger**: `Logger.Instance` is **obsolete** and must not be used in new code.
- **No Console Writes**: Do not call `Console.WriteLine` or `Console.Error.WriteLine`.
- **Injected Logging**: Always consume the `ILogger` interface provided via dependency injection (DI) or pull it from the current `IExecutionContext`.
- **Example**:
  ```csharp
  public class MyHandler : IStatementHandler
  {
      private readonly ILogger<MyHandler> _logger;

      // Inject logger via DI
      public MyHandler(ILogger<MyHandler> logger)
      {
          _logger = logger;
      }

      public async Task ExecuteAsync(IExecutionContext context, CancellationToken cancellationToken)
      {
          _logger.LogInformation("Executing statement...");
      }
  }
  ```

---

## 3. Immutable AST Nodes (Records)

Abstract Syntax Tree (AST) node representations must remain pure and free from side effects to prevent evaluation state corruption.

- **Rule**: Always declare AST node classes as C# `record` types to enforce immutability.
- **Forbidden**: Do not use mutable `class` declarations for AST nodes.
- **Example**:
  ```csharp
  // Correct
  public record CreateConnectionStatement(
      string ConnectionName,
      string ProviderName,
      Dictionary<string, string> Options
  ) : IStatement;

  // Incorrect
  public class CreateConnectionStatement : IStatement
  {
      public string ConnectionName { get; set; } // Mutable state is forbidden!
  }
  ```

---

## 4. Asynchronous I/O with Cancellation

ETL-SQL executes orchestration queries that can be canceled by a job scheduler, user request, or timeout. All I/O calls must support cancellation.

- **Rule**: All I/O operations (file, database query, network request) must use their `Async` overloads and propagate the active `CancellationToken`.
- **No Blocking Calls**: Never use `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` in connector or handler code. This blocks threads and causes deadlocks in asynchronous runtimes.
- **Example**:
  ```csharp
  // Correct
  await using var reader = await command.ExecuteReaderAsync(cancellationToken);
  while (await reader.ReadAsync(cancellationToken)) { ... }

  // Incorrect - blocking call and ignores cancellation
  var reader = command.ExecuteReader(); 
  ```

---

## 5. Exception Sanitization & Wrapping

ETL-SQL coordinates connections across diverse external databases. To prevent database credentials, server names, or schema secrets from leaking in logs or user-facing error messages, provider exceptions must be sanitized.

- **Rule**: Catch all provider-specific exceptions (`SqlException`, `NpgsqlException`, `OracleException`, etc.) at the connector boundary.
- **Action**: Wrap the caught exception inside an `ExecutionException` with a sanitized, user-safe error message.
- **Example**:
  ```csharp
  try
  {
      await _dbConnection.OpenAsync(cancellationToken);
  }
  catch (DbException ex)
  {
      // Incorrect: throwing raw exception leaks connection/host details
      // throw ex;

      // Correct: Sanitize and throw ExecutionException
      throw new ExecutionException(
          $"Failed to open database connection to '{_connectionName}': connection timed out or database is unreachable.",
          ex
      );
  }
  ```

---

## References

- [Connector Standards](Connectors_Standards.md)
- [Presentation Standards](Presentation_Standards.md)
- [Engine Subsystem Guide](../Engine.md)
