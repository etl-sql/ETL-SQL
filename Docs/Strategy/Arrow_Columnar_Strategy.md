# Arrow Columnar Format Strategy

> [!NOTE]
> **Implemented design note.** The columnar spill format described here shipped in ETL-SQL 0.7.0. Use this file to understand the design rationale. For current spill behavior and configuration, see `Docs/Architecture/Engine.md` and `Docs/Strategy/LargeDatasets.md`.

**Status:** Implemented for ETL-SQL 0.7.0 spill workloads
**Phase:** 0.7.0  
**Date:** 2026-04-23  

---

## 1. Problem Statement

ETL-SQL's SpillStore serializes rows as newline-delimited JSON (one `Dictionary<string, object?>` per line), then optionally GZip-compresses and AES-encrypts the stream before writing to disk. This works correctly but carries three costs that compound on large spill workloads:

| Problem | Root cause | Symptom |
|---|---|---|
| Slow spill write/read | Text serialization via `JsonSerializer` | 50M-row sort or join spends measurable wall time in JSON encoding |
| Type loss on round-trip | `UnwrapJsonValue` guesses types from string content | A `decimal` value spilled and re-read may come back as `double`; date strings may conflict with numeric strings |
| Poor compression ratio for numeric columns | JSON text is row-oriented; numbers are encoded as ASCII digits | A column of `int` values requires ~10× more bytes as JSON text than as binary |

The four external engines that use SpillStore — `ExternalSortEngine`, `ExternalJoinEngine`, `ExternalAggregateEngine`, `ExternalWindowEngine` — all write and read spill files exclusively through `ISpillWriter` / `ISpillReader`. This interface boundary is the only surface that needs to change.

---

## 2. What We Are NOT Doing

Two larger Arrow approaches were considered and explicitly rejected for this phase.

### 2.1 `CREATE COLUMNAR TABLE #temp` — User-explicit opt-in (Rejected)

The intuition was: let users choose Arrow for analytics-heavy temp tables without forcing a full engine rewrite. This is rejected for these reasons:

- **Leaks physical layout into user syntax.** SQL users should not manage storage representation. `CREATE COLUMNAR TABLE` is a physical detail, not a logical one. Every script author would need to understand when columnar is beneficial and when it hurts.
- **Combinatorial handler complexity.** Every join, aggregate, sort, and window handler would need to handle row×row, row×columnar, columnar×row, and columnar×columnar combinations. That quadruples the internal branching of every hot path.
- **Silent conversion overhead.** When a columnar `#temp` is joined to a row-oriented `#temp`, a silent format conversion is required at the boundary. For ETL-scale row counts this conversion can cost more than the vectorized speedup earns.
- **Mutations break the model.** Arrow `RecordBatch` is immutable. `UPDATE #temp` and `DELETE FROM #temp` require copy-on-write batch reconstruction, so user-visible columnar temp-table syntax is intentionally not exposed.

### 2.2 Replace `DataTable` engine-wide with `RecordBatch` (Rejected)

Replacing `DataTable` as the universal batch format across all 40+ handlers and the `IDataSource` interface would yield the largest structural Arrow benefit. It is deferred because:

- The C# `Apache.Arrow` library does not include vectorized compute kernels. The 10–50× SIMD speedup frequently cited for Arrow requires either C++ `arrow::compute` (native interop) or writing custom `System.Runtime.Intrinsics` kernels. Neither is in scope.
- Without compute kernels, replacing `DataTable` with `RecordBatch` improves memory density and I/O but does not materially change CPU-bound aggregation speed. The realistic gain is 2–4× from cache locality, not 10–50×.
- It is a major breaking change to `IDataSource.ReadBatches()`, `IDataSource.WriteBatches()`, and every handler. Requires a coordinated 1.0-milestone effort, not a point release.
- `DataTable` has 20+ years of documentation, tooling, and predictable behavior. The C# Arrow library is less mature, has a smaller community, and has known gaps in nullable types and dictionary-encoded string handling.

This path is not part of the 0.7.0 engine contract.

---

## 3. Phase A: Arrow IPC as the SpillStore Format

### 3.1 What Changes

The encrypted/compressed stream stack is unchanged. What changes is the payload format written into that stream:

**Current:**
```
Row data (JsonSerializer line)
  → GZipStream (if SpillCompressionEnabled)
  → AES-256-CBC CryptoStream (if SpillEncryptionEnabled)
  → FileStream
```

**Phase A:**
```
Row data (Arrow IPC RecordBatch)
  → GZipStream (if SpillCompressionEnabled)
  → AES-256-CBC CryptoStream (if SpillEncryptionEnabled)
  → FileStream
```

The `ISpillWriter` and `ISpillReader` interfaces are unchanged. `WriteRowAsync(Row)` and `ReadRowAsync()` still work with `Row` objects. The Arrow IPC encoding/decoding is internal to the new writer/reader implementations.

### 3.2 Why Arrow IPC Specifically

Arrow IPC (the streaming variant, not the file format) is chosen over Parquet for spill because:

- **Designed for streaming, not file storage.** Arrow IPC has no file-level metadata footer; it can be written and read as a pure stream — which is exactly the GZip/CryptoStream pipeline already in place.
- **No schema required upfront at the call site.** Schema is inferred from the first row and embedded in the IPC stream header.
- **Already adjacent to the project.** `Parquet.Net` (v5.5.0, already in Connectors) can bridge Arrow format when needed; the `Apache.Arrow` NuGet is Apache 2.0 licensed with no commercial restrictions.
- **Parquet is overkill for spill.** Parquet adds row-group metadata, statistics, and a file footer that require buffering a complete chunk before writing. For a spill file that is written once and read once, that overhead is wasted.

### 3.3 Schema Inference

Arrow requires a typed schema before writing a `RecordBatch`. The engines that use `SpillStore` all operate on rows derived from `DataTable`, which has a defined schema. The writer will infer schema from the first call to `WriteRowAsync` and lock it in:

```
First WriteRowAsync(row):
  → Infer Arrow schema from row.Columns key names and CLR types
  → Write IPC stream header (schema message)
  → Buffer row into current RecordBatch

Subsequent WriteRowAsync(row):
  → Buffer row into current RecordBatch
  → When buffer reaches FlushBatchSize (default: 10,000 rows), write RecordBatch message and reset

DisposeAsync():
  → Flush any remaining buffered rows as a final RecordBatch
  → Finalize IPC stream (EOS marker)
```

Type mapping from CLR to Arrow:

| CLR type | Arrow type |
|---|---|
| `int`, `long` | `Int64` |
| `decimal` | `Decimal128` |
| `double`, `float` | `Double` |
| `bool` | `Boolean` |
| `DateTime` | `Timestamp(Microseconds, UTC)` |
| `string`, unknown | `Utf8` |
| `null` (first occurrence) | `Utf8` (widest safe default) |

### 3.4 Type Fidelity Improvement

The current JSON path applies `UnwrapJsonValue` on read, which guesses types via `decimal.TryParse` and `DateTime.TryParse`. A string column containing `"123.45"` will be silently promoted to `decimal`. A `DateTime` value round-trips through ISO-8601 but is subject to precision loss.

Arrow IPC preserves the exact CLR type written. No guessing on read. This is a correctness improvement in addition to the performance improvement.

### 3.5 Encryption and Compression Compatibility

The existing AES-256-CBC + IV-prefix scheme and GZip compression layer are unchanged. `Apache.Arrow`'s `ArrowStreamWriter` writes to any `Stream` — the GZip and CryptoStream wrappers are transparent to it. No security model changes are required.

### 3.6 Performance Expectation

Realistic expected improvement for pure spill I/O (write + read round-trip):

| Metric | JSON lines | Arrow IPC | Expected gain |
|---|---|---|---|
| Write throughput (numeric-heavy) | ~200 MB/s | ~600–900 MB/s | 3–5× |
| Read throughput (numeric-heavy) | ~150 MB/s | ~500–800 MB/s | 3–5× |
| Spill file size (before compression) | ~10 bytes/int field | ~8 bytes/int field | ~20% smaller |
| Spill file size (after GZip) | moderate | better (columnar compresses tighter) | ~30–40% smaller |
| Type round-trip fidelity | lossy (string guessing) | exact | correctness fix |

These figures apply to spill I/O only. Aggregation, join, and sort CPU time is not affected by this change — those kernels still operate on `Row` objects after deserialization.

---

## 4. Implementation Plan

### 4.1 New Package

Add `Apache.Arrow` (Apache 2.0) to `ETL-SQL.Engine.csproj`. Version to pin: latest stable as of implementation date. No other project references need updating.

### 4.2 New Classes

**`ArrowSpillWriter`** (replaces `SecureSpillWriter` internal class in `SpillStore.cs`)

- Implements `ISpillWriter`
- Buffers rows internally; infers schema on first write
- Writes Arrow IPC stream via `ArrowStreamWriter` into the existing encrypted/compressed `Stream`
- `FlushBatchSize` defaults to 10,000 (same as engine batch size)

**`ArrowSpillReader`** (replaces `SecureSpillReader`)

- Implements `ISpillReader`
- Reads Arrow IPC stream via `ArrowStreamReader`
- Yields `Row` objects converted from `RecordBatch` columns
- Returns `null` from `ReadRowAsync()` on an empty/missing file (existing contract preserved)

### 4.3 Configuration

Add one new key to `appsettings.json`:

```json
"Security": {
  "SpillEncryptionEnabled": true,
  "SpillCompressionEnabled": true,
  "SpillFormat": "Arrow"
}
```

`SpillFormat` accepts `"Arrow"` (default) or `"Json"` (legacy fallback). The `"Json"` path is retained so persistent sessions with existing spill files can still be read.

### 4.4 Files Changed

| File | Change |
|---|---|
| `src/ETL-SQL.Engine/ETL-SQL.Engine.csproj` | Add `Apache.Arrow` package reference |
| `src/ETL-SQL.Engine/Spill/SpillStore.cs` | Replace `SecureSpillWriter`/`SecureSpillReader` with Arrow variants; add `SpillFormat` config branch |
| `src/appsettings.json` | Add `Security:SpillFormat` key |

No changes to `ISpillStore`, `ISpillWriter`, `ISpillReader`, any external engine, or any handler.

---

## 5. Non-Goals for Phase A

- Replacing `DataTable` as the engine's in-memory batch format.
- Adding `CREATE COLUMNAR TABLE` or any user-visible Arrow syntax.
- Vectorized/SIMD aggregation kernels.
- Arrow Flight or zero-copy Python interop.
- Spill-to-disk for `#temp` tables (`InMemoryDataSource`) — that is a separate item (LargeDatasets.md §5).
- Changing the `IDataSource.ReadBatches()` contract.

---

## 7. Acceptance Criteria

- [ ] All existing tests pass — no regression in any engine, connector, or handler test.
- [ ] A spill-heavy query (`SELECT Region, SUM(Revenue) FROM #t GROUP BY Region` on a 10M-row source) shows measurable wall-time improvement vs the JSON baseline (benchmark in `ETL-SQL.Benchmarks`).
- [ ] Type round-trip fidelity: a `decimal`, `DateTime`, and `bool` column spilled and re-read return the exact input value with no type coercion.
- [ ] Persistent session spill files written with the old `"Json"` format can still be read after upgrading (legacy fallback path).
- [ ] `Security:SpillFormat: "Json"` reverts to the previous behavior without any other code changes.
- [ ] Spill file cleanup behavior is identical to today — no orphaned Arrow IPC files on session end.
