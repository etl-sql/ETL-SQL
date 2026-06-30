# ETL-SQL Isolated 10M Baseline

Generated: 2026-06-30 06:27:24 | Matrix: **Core** | Process isolation: **one Release test host per scenario**

| Scenario | Rows | Rows/s | Elapsed | Peak WS MB | Private MB | Heap MB | Allocated MB | GC Pause ms | Spill Write | Pass |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :---: |
| TempTableSpill_10000000_SELECT_INTO | 10000000 | 226034.7 | 44241 | 449.7 | 448.3 | 127.6 | 37613.4 | 5385 | 1119680000 | OK |
| StreamingSelect_10000000_cap50000 | 10000000 | 1100473.2 | 9087 | 277 | 241.1 | 125 | 571.9 | 140.8 | 0 | OK |
| ExternalAggregate_10000000_10grps | 10000000 | 292688.6 | 34166 | 502.9 | 745.5 | 183.7 | 34400.9 | 5334.4 | 800000000 | OK |
| ExternalJoin_10000000_equality | 10000000 | 39398.5 | 253817 | 4149.9 | 4396.1 | 3188.2 | 86666.5 | 15057.4 | 4608000000 | OK |
| ExternalSort_10000000_DESC | 10000000 | 83633 | 119570 | 959.1 | 1080.9 | 557.4 | 141934.4 | 9145 | 3488000000 | OK |
