# VARIABLES-PARAMETERS Reference

[« Back to parent](../README.md)

| Page | Description |
| :--- | :--- |
| [@@CURRENT_USER](@@current_user.md) | Returns the username of the current execution identity. |
| [@@CURRENT_USER_ID](@@current_user_id.md) | Returns the stable, unique identifier of the current execution identity. |
| [@@ERROR](@@error.md) | Integer error code from the most recently executed statement. 0 means the statement succeeded; any other value indicates an error. |
| [@@FETCH_STATUS](@@fetch_status.md) | Status of the most recent FOREACH or cursor fetch operation. |
| [@@IS_ADMIN](@@is_admin.md) | Returns whether the current execution identity has administrator privileges. |
| [@@LAST_EXEC_MS](@@last_exec_ms.md) | Elapsed time in milliseconds for the most recently completed statement. |
| [@@PEAK_MEMORY_MB](@@peak_memory_mb.md) | Peak working-set memory in MB used by the engine process since the script started. Useful for monitoring memory pressure during large data operations. |
| [@@REAL_USER](@@real_user.md) | Returns the username of the actual authenticated session user. |
| [@@ROWCOUNT](@@rowcount.md) | Number of rows affected by the last DML statement (INSERT, UPDATE, DELETE, MERGE) or returned by the last SELECT. |
| [@@SORT_SPILLS](@@sort_spills.md) | Count of external sort runs that have spilled to disk in the current session. Spills occur when an ORDER BY or window function sort exceeds the in-... |
| [@@SUBQUERY_CACHE_HITS](@@subquery_cache_hits.md) | Number of scalar subquery results retrieved from the session cache rather than being re-evaluated. Indicates effective subquery memoization. |
| [@@SUBQUERY_CACHE_MISSES](@@subquery_cache_misses.md) | Number of scalar subquery evaluations that could not be served from the cache and required a full execution. Paired with @@SUBQUERY_CACHE_HITS to a... |
| [@@TOTAL_SPILLED_BYTES](@@total_spilled_bytes.md) | Cumulative bytes written to disk for all spill operations (sorts, joins, aggregations) in the current session. |
| [@@TRANCOUNT](@@trancount.md) | Current transaction nesting depth. 0 means no active transaction; 1 means one open transaction; values greater than 1 mean nested transactions. |
| [@@VERSION](@@version.md) | Full engine version string including the build number, target framework, and runtime information. |
| [@@PARTITIONS_COUNT](@@partitions_count.md) | Count of external spill partitions created during the most recently completed sort, hash-join, or aggregation. 0 means the operation fit in memory. |
| [@@RESULTSETS](@@resultsets.md) | Count of distinct result sets returned by the most recently executed statement or stored procedure call. |
| [CREATE SETS](create-sets.md) | Defines a named, reusable group of variable assignments that can be activated as a unit (USE SETS) to switch between environments or configuration profiles. |
| [declare](declare.md) | DECLARE creates a named variable in the current scope. Variables are scoped to the procedure, script, or block in which they are declared. |
| [USE](use.md) | Applies session-level settings: encryption passwords or named sets. |
