# Phase 4 Visual Data Payload Crossover Benchmark Report

> **Timestamp (UTC):** 2026-08-21 13:12:33 | **Branch:** `test/reporting-phase4-payload-crossover`
> **OS:** Microsoft Windows 10.0.26200 (X64) | **Runtime:** .NET 10.0.11 | **Cores:** 24 | **Memory:** 32129 MB

---

## 1. Executive Summary & Crossover Findings

This benchmark compares **JSON Row-Oriented** (standard ETL-SQL `VisualManifest.Rows`), **JSON Columnar**, and **Apache Arrow IPC Stream** representations across 5 representative visual workloads spanning **500 to 100,000 rows**.

### Crossover Ranges by Workload (Empirical Evidence)

| Workload | Raw Size Crossover | Gzip Compressed Winner | Decode Speed Crossover | Memory Allocation Winner |
| :--- | :---: | :---: | :---: | :---: |
| **DenseNumeric** | **500 rows** | `JsonColumnar` | **500 rows** | See allocation caveat |
| **MixedTyped** | **500 rows** | `JsonColumnar` | **500 rows** | See allocation caveat |
| **NullableSparse** | **2,500 rows** | `JsonColumnar` | **500 rows** | See allocation caveat |
| **TemporalEvents** | **500 rows** | `JsonColumnar` | **500 rows** | See allocation caveat |
| **StringHeavy** | **500 rows** | `JsonRowOriented` | **500 rows** | See allocation caveat |

The Arrow decode-allocation column measures managed allocations made while opening an IPC stream and
reading a record-batch wrapper over the input byte buffer. Arrow retains column buffers rather than
materializing row objects, so the very small values are expected, but they are **not** a measurement of
total resident payload memory. Use them only as managed materialization-cost evidence; browser heap and
resident-set measurements remain necessary before selecting a production transport.

These observations do not justify a permanent row-count switch. Compression changes the winner by
workload—especially for repetitive strings—and interaction-query timings vary. A production decision
must include browser decode/heap measurements and transport negotiation; the checked-in evidence is a
reproducible comparison harness, not a shipping threshold.

The regression suite applies format-neutral, per-row budgets at the 10,000-row representative point:
no measured format may exceed 200 raw bytes per row or 40 gzip-compressed bytes per row. Per-row budgets
catch payload-shape regressions without turning one sampled row count into a permanent transport switch.

---

## 2. Detailed Workload Measurements

### Workload: `DenseNumeric`

| Rows | Format | Raw Size | Gzip Size | Brotli Size | Encode Time | Decode Time | Decode Memory | Filter Query | Checksum |
| :---: | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| 500 | `JsonRowOriented` | 31.7 KB | 11.5 KB | 11.0 KB | 0.70 ms | **3.30 ms** | 209.0 KB | 1.90 ms | `41BC09AD` |
| 500 | `JsonColumnar` | 24.8 KB | 10.0 KB | 8.6 KB | 0.38 ms | **0.47 ms** | 94.9 KB | 2.67 ms | `41BC09AD` |
| 500 | `ArrowIpcStream` | 24.4 KB | 12.9 KB | 12.8 KB | 0.34 ms | **0.04 ms** | 4.2 KB | 1.44 ms | `41BC09AD` |
| 2,500 | `JsonRowOriented` | 157.9 KB | 55.6 KB | 55.1 KB | 2.23 ms | **5.13 ms** | 1.02 MB | 3.28 ms | `830884BB` |
| 2,500 | `JsonColumnar` | 123.8 KB | 48.4 KB | 41.4 KB | 1.66 ms | **2.15 ms** | 469.9 KB | 3.90 ms | `830884BB` |
| 2,500 | `ArrowIpcStream` | 118.1 KB | 54.5 KB | 52.7 KB | 1.12 ms | **0.05 ms** | 4.2 KB | 1.43 ms | `830884BB` |
| 10,000 | `JsonRowOriented` | 631.5 KB | 219.9 KB | 223.0 KB | 10.17 ms | **23.55 ms** | 4.08 MB | 12.50 ms | `56827693` |
| 10,000 | `JsonColumnar` | 494.7 KB | 183.1 KB | 163.5 KB | 6.79 ms | **9.20 ms** | 1.83 MB | 16.50 ms | `56827693` |
| 10,000 | `ArrowIpcStream` | 469.5 KB | 203.0 KB | 199.0 KB | 4.58 ms | **0.13 ms** | 4.2 KB | 5.52 ms | `56827693` |
| 25,000 | `JsonRowOriented` | 1.54 MB | 548.9 KB | 516.9 KB | 36.68 ms | **35.97 ms** | 10.20 MB | 19.76 ms | `7F0CF825` |
| 25,000 | `JsonColumnar` | 1.21 MB | 453.2 KB | 394.0 KB | 24.39 ms | **17.13 ms** | 4.58 MB | 39.45 ms | `7F0CF825` |
| 25,000 | `ArrowIpcStream` | 1.15 MB | 497.6 KB | 570.8 KB | 14.06 ms | **0.39 ms** | 4.2 KB | 27.84 ms | `7F0CF825` |
| 50,000 | `JsonRowOriented` | 3.08 MB | 1.07 MB | 1.01 MB | 61.64 ms | **52.85 ms** | 20.41 MB | 128.66 ms | `AA1C3F75` |
| 50,000 | `JsonColumnar` | 2.42 MB | 901.1 KB | 773.5 KB | 22.67 ms | **33.95 ms** | 9.16 MB | 79.39 ms | `AA1C3F75` |
| 50,000 | `ArrowIpcStream` | 2.29 MB | 989.2 KB | 1.05 MB | 11.87 ms | **0.62 ms** | 4.2 KB | 17.86 ms | `AA1C3F75` |
| 100,000 | `JsonRowOriented` | 6.17 MB | 2.14 MB | 2.03 MB | 131.77 ms | **104.76 ms** | 40.81 MB | 119.36 ms | `48C3F662` |
| 100,000 | `JsonColumnar` | 4.83 MB | 1.76 MB | 1.49 MB | 47.33 ms | **56.21 ms** | 18.31 MB | 132.90 ms | `48C3F662` |
| 100,000 | `ArrowIpcStream` | 4.58 MB | 1.93 MB | 2.04 MB | 18.39 ms | **1.42 ms** | 4.2 KB | 29.05 ms | `48C3F662` |

### Workload: `MixedTyped`

| Rows | Format | Raw Size | Gzip Size | Brotli Size | Encode Time | Decode Time | Decode Memory | Filter Query | Checksum |
| :---: | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| 500 | `JsonRowOriented` | 35.2 KB | 6.7 KB | 6.8 KB | 3.81 ms | **0.17 ms** | 231.9 KB | 0.23 ms | `411A8D41` |
| 500 | `JsonColumnar` | 29.4 KB | 3.8 KB | 2.9 KB | 0.13 ms | **0.15 ms** | 153.3 KB | 0.28 ms | `411A8D41` |
| 500 | `ArrowIpcStream` | 31.3 KB | 8.3 KB | 7.6 KB | 0.21 ms | **0.02 ms** | 5.4 KB | 0.64 ms | `411A8D41` |
| 2,500 | `JsonRowOriented` | 175.7 KB | 31.0 KB | 32.6 KB | 0.67 ms | **0.79 ms** | 1.13 MB | 0.95 ms | `40950FF0` |
| 2,500 | `JsonColumnar` | 146.4 KB | 17.5 KB | 11.2 KB | 0.70 ms | **0.75 ms** | 759.3 KB | 1.23 ms | `40950FF0` |
| 2,500 | `ArrowIpcStream` | 151.1 KB | 37.3 KB | 33.8 KB | 1.05 ms | **0.06 ms** | 5.4 KB | 0.80 ms | `40950FF0` |
| 10,000 | `JsonRowOriented` | 702.6 KB | 121.7 KB | 130.2 KB | 3.82 ms | **5.17 ms** | 4.53 MB | 4.17 ms | `909E2ABE` |
| 10,000 | `JsonColumnar` | 585.4 KB | 64.2 KB | 41.9 KB | 2.70 ms | **4.15 ms** | 2.96 MB | 4.84 ms | `909E2ABE` |
| 10,000 | `ArrowIpcStream` | 600.3 KB | 142.4 KB | 133.2 KB | 4.49 ms | **0.19 ms** | 5.4 KB | 5.96 ms | `909E2ABE` |
| 25,000 | `JsonRowOriented` | 1.72 MB | 303.1 KB | 299.4 KB | 21.30 ms | **36.45 ms** | 11.32 MB | 44.06 ms | `5B47241C` |
| 25,000 | `JsonColumnar` | 1.43 MB | 155.5 KB | 152.5 KB | 7.16 ms | **21.60 ms** | 7.40 MB | 12.70 ms | `5B47241C` |
| 25,000 | `ArrowIpcStream` | 1.46 MB | 362.2 KB | 347.8 KB | 5.72 ms | **0.40 ms** | 5.4 KB | 24.57 ms | `5B47241C` |
| 50,000 | `JsonRowOriented` | 3.43 MB | 605.3 KB | 597.1 KB | 37.46 ms | **67.08 ms** | 22.64 MB | 59.85 ms | `41B857FD` |
| 50,000 | `JsonColumnar` | 2.86 MB | 306.4 KB | 299.7 KB | 15.88 ms | **39.58 ms** | 14.80 MB | 69.86 ms | `41B857FD` |
| 50,000 | `ArrowIpcStream` | 2.93 MB | 723.7 KB | 732.1 KB | 13.09 ms | **1.04 ms** | 6.2 KB | 14.85 ms | `41B857FD` |
| 100,000 | `JsonRowOriented` | 6.87 MB | 1.18 MB | 1.17 MB | 84.78 ms | **119.07 ms** | 45.35 MB | 99.13 ms | `CB983CF0` |
| 100,000 | `JsonColumnar` | 5.73 MB | 606.4 KB | 590.2 KB | 36.53 ms | **81.36 ms** | 29.59 MB | 158.69 ms | `CB983CF0` |
| 100,000 | `ArrowIpcStream` | 5.85 MB | 1.43 MB | 1.47 MB | 23.78 ms | **1.73 ms** | 5.4 KB | 23.55 ms | `CB983CF0` |

### Workload: `NullableSparse`

| Rows | Format | Raw Size | Gzip Size | Brotli Size | Encode Time | Decode Time | Decode Memory | Filter Query | Checksum |
| :---: | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| 500 | `JsonRowOriented` | 27.3 KB | 6.1 KB | 6.1 KB | 0.13 ms | **0.32 ms** | 180.9 KB | 0.15 ms | `4AB6B460` |
| 500 | `JsonColumnar` | 23.4 KB | 4.7 KB | 4.5 KB | 0.10 ms | **0.11 ms** | 97.4 KB | 0.20 ms | `4AB6B460` |
| 500 | `ArrowIpcStream` | 27.3 KB | 8.3 KB | 7.8 KB | 0.05 ms | **0.01 ms** | 4.2 KB | 0.12 ms | `4AB6B460` |
| 2,500 | `JsonRowOriented` | 136.1 KB | 28.5 KB | 29.4 KB | 0.61 ms | **0.62 ms** | 904.1 KB | 0.72 ms | `FBCAF777` |
| 2,500 | `JsonColumnar` | 116.7 KB | 22.5 KB | 20.9 KB | 0.71 ms | **0.59 ms** | 482.6 KB | 0.95 ms | `FBCAF777` |
| 2,500 | `ArrowIpcStream` | 132.3 KB | 35.9 KB | 34.5 KB | 0.28 ms | **0.04 ms** | 4.2 KB | 0.55 ms | `FBCAF777` |
| 10,000 | `JsonRowOriented` | 545.1 KB | 112.2 KB | 116.5 KB | 2.43 ms | **4.38 ms** | 3.53 MB | 2.64 ms | `C1709680` |
| 10,000 | `JsonColumnar` | 467.6 KB | 83.2 KB | 79.2 KB | 2.12 ms | **2.75 ms** | 1.88 MB | 3.89 ms | `C1709680` |
| 10,000 | `ArrowIpcStream` | 526.0 KB | 136.8 KB | 135.3 KB | 1.19 ms | **0.16 ms** | 4.2 KB | 2.15 ms | `C1709680` |
| 25,000 | `JsonRowOriented` | 1.34 MB | 279.9 KB | 270.5 KB | 14.91 ms | **15.60 ms** | 8.83 MB | 50.47 ms | `B2FC1068` |
| 25,000 | `JsonColumnar` | 1.15 MB | 203.0 KB | 196.5 KB | 5.30 ms | **9.01 ms** | 4.70 MB | 41.75 ms | `B2FC1068` |
| 25,000 | `ArrowIpcStream` | 1.28 MB | 337.5 KB | 386.9 KB | 4.72 ms | **0.39 ms** | 4.2 KB | 5.58 ms | `B2FC1068` |
| 50,000 | `JsonRowOriented` | 2.70 MB | 559.4 KB | 538.6 KB | 45.99 ms | **50.66 ms** | 17.65 MB | 28.05 ms | `9DD4D2B9` |
| 50,000 | `JsonColumnar` | 2.32 MB | 403.3 KB | 393.3 KB | 11.82 ms | **22.93 ms** | 9.40 MB | 36.75 ms | `9DD4D2B9` |
| 50,000 | `ArrowIpcStream` | 2.56 MB | 674.6 KB | 745.4 KB | 8.99 ms | **0.78 ms** | 4.2 KB | 10.56 ms | `9DD4D2B9` |
| 100,000 | `JsonRowOriented` | 5.40 MB | 1.09 MB | 1.05 MB | 74.53 ms | **88.77 ms** | 35.32 MB | 208.22 ms | `97BD77E9` |
| 100,000 | `JsonColumnar` | 4.64 MB | 801.0 KB | 789.3 KB | 26.15 ms | **51.70 ms** | 18.81 MB | 103.25 ms | `97BD77E9` |
| 100,000 | `ArrowIpcStream` | 5.13 MB | 1.32 MB | 1.44 MB | 16.46 ms | **1.52 ms** | 4.2 KB | 24.67 ms | `97BD77E9` |

### Workload: `TemporalEvents`

| Rows | Format | Raw Size | Gzip Size | Brotli Size | Encode Time | Decode Time | Decode Memory | Filter Query | Checksum |
| :---: | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| 500 | `JsonRowOriented` | 34.5 KB | 5.6 KB | 4.9 KB | 0.09 ms | **0.11 ms** | 204.3 KB | 0.14 ms | `E7E74356` |
| 500 | `JsonColumnar` | 31.5 KB | 4.0 KB | 3.0 KB | 0.08 ms | **0.09 ms** | 123.7 KB | 0.17 ms | `E7E74356` |
| 500 | `ArrowIpcStream` | 34.4 KB | 9.0 KB | 7.2 KB | 0.06 ms | **0.02 ms** | 3.8 KB | 0.11 ms | `E7E74356` |
| 2,500 | `JsonRowOriented` | 172.0 KB | 25.2 KB | 24.6 KB | 0.62 ms | **0.55 ms** | 1020.9 KB | 0.61 ms | `A0B67919` |
| 2,500 | `JsonColumnar` | 157.3 KB | 18.9 KB | 11.7 KB | 0.39 ms | **0.45 ms** | 614.3 KB | 0.81 ms | `A0B67919` |
| 2,500 | `ArrowIpcStream` | 168.0 KB | 38.6 KB | 29.2 KB | 0.38 ms | **0.05 ms** | 3.8 KB | 0.50 ms | `A0B67919` |
| 10,000 | `JsonRowOriented` | 687.7 KB | 98.3 KB | 98.4 KB | 3.38 ms | **3.69 ms** | 3.99 MB | 9.22 ms | `23FE79AD` |
| 10,000 | `JsonColumnar` | 629.1 KB | 69.0 KB | 34.5 KB | 1.65 ms | **2.70 ms** | 2.40 MB | 3.36 ms | `23FE79AD` |
| 10,000 | `ArrowIpcStream` | 668.9 KB | 150.3 KB | 107.5 KB | 1.55 ms | **0.20 ms** | 3.8 KB | 2.51 ms | `23FE79AD` |
| 25,000 | `JsonRowOriented` | 1.68 MB | 244.4 KB | 233.5 KB | 10.33 ms | **34.41 ms** | 9.97 MB | 38.26 ms | `F7F98861` |
| 25,000 | `JsonColumnar` | 1.54 MB | 167.1 KB | 78.6 KB | 4.06 ms | **13.99 ms** | 5.99 MB | 8.80 ms | `F7F98861` |
| 25,000 | `ArrowIpcStream` | 1.63 MB | 370.7 KB | 291.9 KB | 3.23 ms | **0.47 ms** | 3.8 KB | 5.10 ms | `F7F98861` |
| 50,000 | `JsonRowOriented` | 3.36 MB | 488.0 KB | 465.3 KB | 21.11 ms | **42.31 ms** | 19.94 MB | 109.23 ms | `A3CB29BF` |
| 50,000 | `JsonColumnar` | 3.07 MB | 327.9 KB | 148.5 KB | 9.22 ms | **26.59 ms** | 11.98 MB | 63.04 ms | `A3CB29BF` |
| 50,000 | `ArrowIpcStream` | 3.26 MB | 740.0 KB | 600.8 KB | 9.23 ms | **0.94 ms** | 3.8 KB | 9.84 ms | `A3CB29BF` |
| 100,000 | `JsonRowOriented` | 6.71 MB | 975.1 KB | 928.8 KB | 59.14 ms | **100.49 ms** | 39.87 MB | 102.55 ms | `2520ACCA` |
| 100,000 | `JsonColumnar` | 6.14 MB | 650.4 KB | 285.3 KB | 23.17 ms | **60.24 ms** | 23.96 MB | 68.83 ms | `2520ACCA` |
| 100,000 | `ArrowIpcStream` | 6.52 MB | 1.44 MB | 1.15 MB | 16.61 ms | **1.93 ms** | 3.8 KB | 40.47 ms | `2520ACCA` |

### Workload: `StringHeavy`

| Rows | Format | Raw Size | Gzip Size | Brotli Size | Encode Time | Decode Time | Decode Memory | Filter Query | Checksum |
| :---: | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| 500 | `JsonRowOriented` | 83.7 KB | 2.4 KB | 1.3 KB | 0.07 ms | **0.15 ms** | 299.4 KB | 0.19 ms | `595F340C` |
| 500 | `JsonColumnar` | 81.7 KB | 2.3 KB | 867 B | 0.06 ms | **0.10 ms** | 226.0 KB | 0.56 ms | `595F340C` |
| 500 | `ArrowIpcStream` | 83.7 KB | 7.6 KB | 6.4 KB | 0.10 ms | **0.03 ms** | 3.8 KB | 0.14 ms | `595F340C` |
| 2,500 | `JsonRowOriented` | 418.2 KB | 8.9 KB | 3.7 KB | 0.35 ms | **1.33 ms** | 1.46 MB | 0.85 ms | `EF26E0CF` |
| 2,500 | `JsonColumnar` | 408.4 KB | 9.5 KB | 1.5 KB | 0.32 ms | **0.44 ms** | 1.10 MB | 0.89 ms | `EF26E0CF` |
| 2,500 | `ArrowIpcStream` | 414.2 KB | 35.3 KB | 28.7 KB | 1.52 ms | **0.13 ms** | 3.8 KB | 8.36 ms | `EF26E0CF` |
| 10,000 | `JsonRowOriented` | 1.63 MB | 32.6 KB | 12.0 KB | 2.12 ms | **7.15 ms** | 5.84 MB | 3.72 ms | `000E79FE` |
| 10,000 | `JsonColumnar` | 1.60 MB | 33.5 KB | 24.4 KB | 1.42 ms | **2.62 ms** | 4.40 MB | 8.13 ms | `000E79FE` |
| 10,000 | `ArrowIpcStream` | 1.62 MB | 144.6 KB | 117.0 KB | 2.93 ms | **0.51 ms** | 3.8 KB | 15.14 ms | `000E79FE` |
| 25,000 | `JsonRowOriented` | 4.08 MB | 79.9 KB | 28.9 KB | 7.85 ms | **38.06 ms** | 14.61 MB | 55.09 ms | `60224FDC` |
| 25,000 | `JsonColumnar` | 3.99 MB | 81.9 KB | 59.7 KB | 3.75 ms | **18.04 ms** | 10.99 MB | 42.62 ms | `60224FDC` |
| 25,000 | `ArrowIpcStream` | 4.04 MB | 362.7 KB | 311.5 KB | 5.96 ms | **1.22 ms** | 3.8 KB | 8.96 ms | `60224FDC` |
| 50,000 | `JsonRowOriented` | 8.17 MB | 158.7 KB | 56.3 KB | 14.79 ms | **70.59 ms** | 29.22 MB | 103.10 ms | `2B7DB251` |
| 50,000 | `JsonColumnar` | 7.98 MB | 162.5 KB | 118.3 KB | 7.20 ms | **36.02 ms** | 21.97 MB | 122.45 ms | `2B7DB251` |
| 50,000 | `ArrowIpcStream` | 8.07 MB | 730.1 KB | 675.3 KB | 13.80 ms | **2.39 ms** | 3.8 KB | 28.56 ms | `2B7DB251` |
| 100,000 | `JsonRowOriented` | 16.38 MB | 316.2 KB | 113.7 KB | 44.11 ms | **144.74 ms** | 58.82 MB | 228.61 ms | `2FB8DDBE` |
| 100,000 | `JsonColumnar` | 16.00 MB | 324.7 KB | 190.2 KB | 18.70 ms | **84.16 ms** | 43.95 MB | 144.13 ms | `2FB8DDBE` |
| 100,000 | `ArrowIpcStream` | 16.14 MB | 1.43 MB | 1.32 MB | 33.60 ms | **5.18 ms** | 3.8 KB | 30.79 ms | `2FB8DDBE` |

---

## 3. How to Run the Benchmark Harness Deterministically

```powershell
pwsh -File ./scripts/Measure-ReportingPayloadCrossover.ps1
```

To execute the fast non-timing correctness test suite in CI:

```powershell
dotnet test tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj --filter "FullyQualifiedName~PayloadCrossoverTests"
```
