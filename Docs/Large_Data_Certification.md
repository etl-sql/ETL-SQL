# Large Data Certification

This document describes which large-data scenarios are certified, at which scale tiers, and what execution paths are exercised.

Current status: the implemented lane is a **Smoke-tier certification harness**. It verifies core spill/streaming mechanics and produces reusable report artifacts, but it is not yet a complete large-data product certification across every connector and statement type.

---

## Certification Tiers

| Tier | Row Count | Purpose | How to Run |
| :--- | :--- | :--- | :--- |
| **Smoke** | 50k–100k rows | PR/local validation — fast, spill forced by threshold override | `dotnet test --filter "Category=ScaleCertification&Tier=Smoke"` |
| **Standard** | 1M+ rows | Release validation — real memory pressure using the same smoke scenarios at higher row scale | `.\scripts\Test-ScaleCertification.ps1 -Tier Smoke -RowCountScale 10` |
| **Stress** | 10M+ rows | Nightly / manual only — OOM risk on small machines; scenario coverage still pending | `.\scripts\Test-ScaleCertification.ps1 -Tier Smoke -RowCountScale 100` |

Run the full certification lane and produce a report with:

```powershell
.\scripts\Test-ScaleCertification.ps1
```

Reports are written to `./certification-results/`.

---

## Certified Scenarios (Smoke Tier)

| Scenario | Rows | Operator | Spill Path | Assertions |
| :--- | ---: | :--- | :--- | :--- |
| External Sort (DESC) | 50k × scale | `ORDER BY` → ExternalSortEngine | Sort chunk spill forced (5k/chunk) | Row count, min/max/sum, spill bytes > 0 |
| External Aggregate | 100k × scale | `GROUP BY SUM/COUNT` → ExternalAggregateEngine | Operator memory grant forced to 1 MB | Group count, total row count, positive sum, spill bytes > 0 |
| External Hash Join | 50k × scale | `JOIN ON` → ExternalJoinEngine | Join spill threshold forced to 5k | Row count, min/max id, checksum, spill bytes > 0 |
| Temp Table Spill | 50k × scale | `SELECT INTO #temp` → temp spill | Spill threshold forced to 10k rows | Exact row count, spill bytes > 0 |
| Streaming Result Cap | 100k | `SELECT *` | MaxLastResultRows cap at 50k | Result ≤ cap (streaming stops cleanly) |
| Window ROW_NUMBER | 50k × scale | `ROW_NUMBER() OVER(ORDER BY)` → ExternalWindowEngine | Window spill threshold forced to 5k | Row count, min/max/sum, spill bytes > 0 |
| CSV Ingest | 50k × scale | `FLATFILE`/CSV `ReadBatches` | Connector batch read | Row count and checksum |
| Parquet Round Trip | 50k × scale | `PARQUET` `WriteBatches`/`ReadBatches` | Connector batch write/read | Row count and checksum |
| Report `CREATE DATASET` Snapshot/Reload | 50k × scale | Query → Parquet cache → reload | Portal dataset cache | Row count and checksum after cached reload |
| CUBE Grouping Sets | 50k × scale | `GROUP BY CUBE(grp, bucket)` → ExternalAggregateEngine | Operator memory grant forced to 1 MB | Expanded row count, checksum, spill bytes > 0 |
| Scalar Subquery Cache | 50k × scale | Correlated scalar subquery | LRU subquery cache | Row count, checksum, exact hit/miss counts |
| Spill Cleanup Success | 50k × scale | Non-persistent `SELECT INTO #temp` spill | Temp-table spill files | Spill files exist before dispose; spill directory removed after dispose |
| Spill Cleanup Failure | 50k × scale | Non-persistent `SELECT INTO #temp` with forced source failure | Temp-table spill files | Spill files exist after failure; spill directory removed after dispose |

---

## Operator Execution Mode Inventory

| Operator / Statement | Execution Mode | Fully Materializes? | Notes |
| :--- | :--- | :--- | :--- |
| `SELECT` (no aggregation, no sort) | Streaming | No | Bounded by `MaxLastResultRows` |
| `SELECT … ORDER BY` | External Sort (chunk-based) | Yes (sorted chunks on disk) | `ExternalSortEngine`; chunk size configurable |
| `SELECT … GROUP BY` | In-memory → External Aggregate | Yes | Switches at `OperatorMemoryGrantMB` threshold |
| `SELECT … JOIN` | Hash Join → External Hash Join | Yes (hash table partitions) | Switches at `JoinSpillThreshold` |
| `SELECT … WINDOW` | External Window Engine | Partition-at-a-time | Switches at `WindowSpillThreshold` |
| `SELECT INTO #temp` | In-memory → Disk-spill temp table | Partial | Spills at `TempTableSpillThresholdRows` |
| `MERGE` | Fully materializes source | Yes | No spill path — document practical limit |
| `UPDATE` | Fully materializes match | Yes | No spill path — document practical limit |
| `DELETE` | Fully materializes match | Yes | No spill path — document practical limit |
| `CREATE DATASET` | Parquet snapshot | Batch streaming | Re-executes query; result cached to disk |
| Report dataset refresh | Query → Parquet cache | Batch streaming | Portal-managed; bounded by query result size |

### Fully Materializing Operators

`MERGE`, `UPDATE`, and `DELETE` load the full match set into memory. For large tables:
- Prefer `MERGE`/`UPDATE`/`DELETE` with selective `WHERE` clauses.
- Use batched loops for large-scale mutations.
- Report a bug if memory usage exceeds 2× the dataset size.

---

## Running the Certification Lane

```powershell
# Smoke tier (developer laptop, seconds)
dotnet test ETL-SQL.slnx --filter "Category=ScaleCertification"

# Smoke tier with report artifact
.\scripts\Test-ScaleCertification.ps1 -Tier Smoke

# Standard-scale (10× row counts) — suitable for CI release agents
.\scripts\Test-ScaleCertification.ps1 -Tier Smoke -RowCountScale 10

# Full report with all tiers
.\scripts\Test-ScaleCertification.ps1 -Tier All
```

### Environment Tuning

Set `CERT_ROW_SCALE` environment variable or pass `-RowCountScale` to the script to scale row counts for the target machine profile. Default is 1.0 (Smoke tier as defined above).

The test suite reads `CERT_ROW_SCALE` directly; the PowerShell runner sets it from `-RowCountScale` before invoking `dotnet test`.

---

## Pending Certification Coverage

The following acceptance items remain open before ETL-SQL can claim complete large-data certification:

- Provider-backed large-data runs beyond in-memory sources.
- Documented memory bounds enforced by test assertions.
- `MERGE`, `UPDATE`, and `DELETE` boundedness certification or explicit warnings in the linter/runtime.

---

## Reference Machine Profile (Smoke baseline)

Tests were validated on a developer workstation with no specific memory constraints. Spill is forced by overriding engine thresholds within each test — spill behaviour is verified regardless of available RAM.

For release certification, run Standard tier on a machine with ≥ 8 GB available RAM to avoid OS-level swap interference.
