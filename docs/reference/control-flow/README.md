# CONTROL-FLOW Reference

[« Back to parent](../README.md)

| Page | Description |
| :--- | :--- |
| [BREAK](break.md) | Exits the innermost active loop immediately, transferring control to the statement after the loop's `END`. |
| [CONTINUE](continue.md) | Skips the remainder of the current loop iteration and begins the next one immediately. |
| [EXECUTE](execute.md) | Sends a raw command block to an external connection, or runs administrative/portal operations. |
| [for](for.md) | FOR provides a numeric counter loop or a per-row query loop. |
| [foreach](foreach.md) | FOREACH iterates over a LIST variable, a #temp table's rows, or a JSON array. |
| [GO](go.md) | Batch separator. Divides a script into independent batches that each run in isolation — if one batch fails, subsequent batches still execute. |
| [if](if.md) | IF evaluates a condition and executes the matching branch. The ELSE branch is optional. |
| [parallel](parallel.md) | PARALLEL runs enclosed statements concurrently on a thread pool. Useful for independent I/O-bound operations such as loading from multiple sources ... |
| [RETURN](return.md) | Exits the current script or procedure immediately, optionally surfacing output variable values to the caller. |
| [RUN SCRIPT](run.md) | Executes another ETL-SQL script file, optionally passing parameters in or out. |
| [THROW / RAISEERROR](throw.md) | Raises a runtime error, terminating execution or transferring control to the nearest CATCH block. |
| [try-catch](try-catch.md) | TRY/CATCH provides structured error handling. If any statement inside the TRY block throws, execution jumps to the CATCH block. |
| [WAIT UNTIL](wait-until.md) | Polls a scalar condition until it becomes true, then continues script execution. |
| [WAITFOR](waitfor.md) | Suspends script execution for a duration, until a specific time, or until a condition becomes true. |
| [while](while.md) | WHILE repeats a block as long as a condition remains TRUE. The condition is evaluated before each iteration — if FALSE on entry, the block never runs. |
