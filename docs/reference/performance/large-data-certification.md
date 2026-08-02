# Large Data Certification

This document describes which large-data scenarios are certified, at which scale tiers, and what execution paths are exercised.

Current status: the implemented lane is a **Smoke-tier certification harness**. It verifies core spill/streaming mechanics and produces reusable report artifacts, but it is not yet a complete large-data product certification across every connector and statement type.

---

## Certification Tiers

| Tier | Row Count | Memory Ceiling | Purpose | How to Run |
| :--- | :--- | :--- | :--- | :--- |
| **Smoke** | 50k–100k rows | 1 GB | PR/local validation — fast, spill forced by threshold override | `dotnet test --filter "Category=ScaleCertification&Tier=Smoke"` |
| **Standard** | 500k–1M rows | 4 GB | Release validation — real memory pressure using standard scaling | `.\scripts\Test-ScaleCertification.ps1 -Tier Standard` |
| **Stress** | 5M–10M rows | 8 GB | Nightly / manual only — validates deeper resource usage | `.\scripts\Test-ScaleCertification.ps1 -Tier Stress` |
| **Huge** | 25M–50M rows | 16 GB | Maximum load capacity planning / release gate validation | `.\scripts\Test-ScaleCertification.ps1 -Tier Huge` |
| **Provider** | 50k+ rows | 1 GB | Real local connector validation for providers available without external services | `.\scripts\Test-ScaleCertification.ps1 -Tier Provider` |

Run the full certification lane and produce a report with:

```powershell
.\scripts\Test-ScaleCertification.ps1
```

Reports are written to `./certification-results/`.

---

## Billion-Row Operator Matrix

The billion-row claim is operator-specific. The checked-in manifest at
`certification-results/billion-row-operator-scenarios.json` is the source of truth for each
scenario's state, admission criteria, success criteria, telemetry contract, resume key, artifact,
and non-goals.

| Scenario | Operator | State | Artifact |
| :--- | :--- | :--- | :--- |
| `GateF_NativeScanFilterProjectionAggregate_1B` | Columnar scan/filter/projection/low-cardinality aggregate | Certified | `certification-results/gate-f-1b/gate-f-report.json#columnarCore` |
| `GateF_TempTableRoundTrip_1B` | Temp-table spill round trip | Certified | `certification-results/gate-f-1b/gate-f-report.json#tempTableRoundTrip` |
| `ExternalSort_MultiKey_1B` | External sort | Candidate | Pending operator-run artifact |
| `ExternalEquiJoin_ControlledSkew_1B` | External equi-join | Candidate | Pending operator-run artifact |
| `HighCardinalityGrouping_1B` | External aggregate | Candidate | Pending operator-run artifact at `certification-results/gate-f-1b/gate-f-report.json#highCardinalityGrouping` |
| `EligibleWindowRowNumber_1B` | External window | Candidate | Pending operator-run artifact at `certification-results/gate-f-1b/gate-f-report.json#eligibleWindowRowNumber` |
| `HolisticAggregates_1B` | Holistic aggregates | Not certified | No 1B claim |
| `HeterogeneousMerge_1B` | Heterogeneous `MERGE` | Not certified | No 1B claim |

Candidate rows are not product claims. They define the admission and telemetry contract that must be
satisfied before an operator can move to certified. Release notes must cite a certified scenario,
machine class, artifact, and non-goal list rather than making a broad billion-row SQL claim.

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
| Neo4j Batched Graph Load | `NEO4J_SCALE_ROWS` nodes + relationships | `NEO4J` Docker connector `WriteBatches` | Keyed node `MERGE`, keyed endpoint relationship `MERGE` | Node count, edge count, score checksum, relationship checksum |

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
- Treat these as uncapped operations until they have a spill-backed implementation.

The linter emits `FullyMaterializingDml` warnings for these statements so scripts do not accidentally rely on large-data boundedness that is not certified.

---

## Running the Certification Lane

```powershell
# Smoke tier (developer laptop, seconds)
dotnet test ETL-SQL.slnx --filter "Category=ScaleCertification"

# Smoke tier with report artifact
.\scripts\Test-ScaleCertification.ps1 -Tier Smoke

# Standard-scale (10× row counts by default) — suitable for CI release agents
.\scripts\Test-ScaleCertification.ps1 -Tier Standard

# Stress-scale (100× row counts by default) — manual/nightly only
.\scripts\Test-ScaleCertification.ps1 -Tier Stress

# Provider-backed local connector lane
.\scripts\Test-ScaleCertification.ps1 -Tier Provider

# Neo4j Docker graph-load certification
$env:NEO4J_SCALE_ROWS='50000'
dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --filter "Connector=NEO4J&Category=ScaleCertification" --no-restore -m:1 --logger "console;verbosity=detailed"
Remove-Item Env:\NEO4J_SCALE_ROWS

# Full report with all tiers
.\scripts\Test-ScaleCertification.ps1 -Tier All
```

For a commit-to-commit decision, use the same-worktree comparison harness instead of comparing
reports from separate checkout directories:

```powershell
.\scripts\Test-ScaleCommitComparison.ps1 `
  -BaselineRef v0.17.0 `
  -CandidateRef HEAD `
  -Tier Standard `
  -Scenario StreamingSelect `
  -Samples 3
```

The harness requires a clean tracked worktree because it switches both refs through the same
directory. It interleaves A/B arms, rebuilds and discards a warm-up for every sample, restores the
original branch in a `finally` block, and records within-arm spread beside the median delta. Use
`-PlanOnly` to validate refs and view the sequence without switching commits.

### Environment Tuning

Set `CERT_ROW_SCALE` environment variable or pass `-RowCountScale` to the script to scale row counts for the target machine profile. Script defaults are tier-specific: Smoke = 1.0, Standard = 10.0, Stress = 100.0.

The test suite reads `CERT_ROW_SCALE` directly for Smoke tests. The Standard, Stress, and Provider trait wrappers use `CERT_STANDARD_ROW_SCALE`, `CERT_STRESS_ROW_SCALE`, and `CERT_PROVIDER_ROW_SCALE` so release gates can select those tiers without reusing the Smoke trait.

### Memory Bounds

Every certification scenario asserts that managed memory remains below a documented tier bound. The emitted `CERT_METRIC` JSON and generated Markdown report include both observed managed memory and the enforced bound.

Default bounds are now fixed ceilings to enforce machine-independent memory constraints:

| Row Scale | Effective Memory Tier | Default Bound |
| :--- | :--- | :--- |
| `<= 1` | Smoke | 1 GB (1,024 MB) |
| `> 1` and `<= 10` | Standard | 4 GB (4,096 MB) |
| `> 10` and `<= 100` | Stress | 8 GB (8,192 MB) |
| `> 100` | Huge | 16 GB (16,384 MB) |

Set `CERT_MEMORY_BOUND_MB` to override the default when certifying on a constrained or intentionally oversized validation agent. The override is treated as the hard assertion limit for every scenario in that run.

---

## Pending Certification Coverage

The scale harness now has selectable Smoke, Standard, Stress, and local Provider lanes. External database/provider certification remains tracked under connector certification because it depends on service availability and credentials outside the self-contained scale harness.

---

## Reference Machine Profile (Smoke baseline)

Tests were validated on a developer workstation with no specific memory constraints. Spill is forced by overriding engine thresholds within each test — spill behaviour is verified regardless of available RAM.

For release certification, run Standard tier on a machine with ≥ 8 GB available RAM to avoid OS-level swap interference.

## References
- [User Manual](../../guides/getting-started.md)
