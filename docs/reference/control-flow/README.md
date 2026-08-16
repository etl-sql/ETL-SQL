# CONTROL-FLOW Reference

[« Back to parent](../README.md)

| Page | Description |
| :--- | :--- |
| [BREAK](break.md) | Exits the innermost active loop immediately, transferring control to the statement after the loop's `END`. |
| [CONTINUE](continue.md) | Skips the remainder of the current loop iteration and begins the next one immediately. |
| [EXECUTE](execute.md) | Sends a raw command block to an external connection, or runs administrative/portal operations. |
| [for](for.md) | FOR provides a numeric counter loop or a per-row query loop. |
| [FOREACH](foreach.md) | Iterates sequentially over a `LIST` variable, the rows of an in-memory `#temp` table, or the results of an inline `(SELECT ...)` subquery. Provides... |
| [GO](go.md) | Batch separator. Divides a script into independent batches that each run in isolation — if one batch fails, subsequent batches still execute. |
| [IF...ELSE](if.md) | Conditionally executes a statement block based on the evaluation of a boolean predicate. Supports single-statement blocks, compound `BEGIN...END` b... |
| [parallel](parallel.md) | PARALLEL runs enclosed statements concurrently on a thread pool. Useful for independent I/O-bound operations such as loading from multiple sources ... |
| [RETURN](return.md) | Exits the current script or procedure immediately, optionally surfacing output variable values to the caller. |
| [RUN SCRIPT](run.md) | Executes another ETL-SQL script file, optionally passing parameters in or out. |
| [THROW / RAISEERROR](throw.md) | Raises a runtime error, terminating execution or transferring control to the nearest CATCH block. |
| [TRY...CATCH](try-catch.md) | Structured exception handling block. Intercepts runtime exceptions occurring within the `BEGIN TRY` block, transfers execution immediately to `BEGI... |
| [WAIT UNTIL](wait-until.md) | Polls a scalar condition until it becomes true, then continues script execution. Use this for readiness checks where the script should pause until ... |
| [WAITFOR](waitfor.md) | Suspends script execution for a duration or until a specific wall-clock time. Use `WAIT UNTIL` for condition polling. |
| [WHILE](while.md) | Executes a statement block repeatedly as long as a specified boolean condition evaluates to `TRUE`. The test condition is evaluated before each ite... |
