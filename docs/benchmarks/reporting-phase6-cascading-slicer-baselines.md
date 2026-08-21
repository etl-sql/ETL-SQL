# Phase 6 Cascading Slicer & Parameter Dependency Baseline Report

> **Timestamp (UTC):** 2026-08-21 14:10:58 | **Branch:** `test/reporting-phase6-cascading-slicer-baselines`

---

## 1. Existing Test & Feature Inventory

| Category | Test / Source Location | Target | Description |
| :--- | :--- | :--- | :--- |
| **Parameter Declaration & AST** | `tests/ETL-SQL.Tests/Core/Parser/ReportSqlParserTests.cs` | `Parse_VisualWithActions_SetsParameter` | Parses SET_PARAMETER actions on BAR and SLICER visuals |
| **Action Manifest Binding** | `tests/ETL-SQL.Tests/Reporting/ReportingEndToEndTests.cs` | `VisualActions_SetParameter_BindsToManifest` | Verifies action manifest emission for @Region, @Search, @Limit, @Active |
| **Designer Roundtrip AST** | `tests/ETL-SQL.Tests/Portal/Fixtures/ReportDesignerRoundTripFixtures.cs` | `Roundtrip_PreservesSetParameterAction` | Validates that Visual Report Builder script synchronization preserves SET_PARAMETER bindings |
| **MultiSelect Visual Syntax** | `tests/ETL-SQL.Tests/Core/Parser/ReportSqlParserTests.cs` | `Parse_MultiSelectVisual_ExtractsOptionsSource` | Parses MULTISELECT visual type with mandatory SOURCE clause |
| **Offline Snapshot Packaging** | `src/ETL-SQL.Reporting/SnapshotPackageService.cs` | `ReadManifestFromPackageAsync` | Serializes and deserializes ReportManifest and table data into standalone .etlsnap packages |
| **Interactive Execution Engine** | `src/ETL-SQL.ReportHosting/InteractiveSessionManager.cs` | `ExecuteInteractionQueryAsync` | Executes parameterized visual SQL queries during live interactive portal sessions |
| **Terminal Slicer Rendering** | `tests/ETL-SQL.Tests/Reporting/TerminalRendererTests.cs` | `RenderSlicer_EmitsInteractiveControl` | Renders interactive slicers in terminal ANSI / Spectre output |

---

## 2. Capabilities: Runnable Today vs Pending Phase 6 Accepted Design

| Capability Area | Status | Operational Details |
| :--- | :---: | :--- |
| **AST Parsing & Diagnostics** | ✅ **Runnable Today** | Runnable Today: All standard Report-SQL slicers parse without error diagnostics. |
| **Action Manifest Metadata** | ✅ **Runnable Today** | Runnable Today: ACTIONS (ON_CHANGE = SET_PARAMETER(@var, val)) correctly recorded on VisualManifest. |
| **Manual Parameter Ingestion** | ✅ **Runnable Today** | Runnable Today: Evaluator correctly accepts and injects external @parameters during live re-execution. |
| **Reactive Client Graph Propagation** | ⏳ **Pending Design** | Pending Phase 6 Design: Client runtime does not automatically cascade parameter changes down the DAG. |
| **Automatic Descendant State Invalidation** | ⏳ **Pending Design** | Pending Phase 6 Design: Invalidation of stale child selections upon parent mutation is not yet automated. |
| **Compile-Time Cycle Detection** | ⏳ **Pending Design** | Pending Phase 6 Design: Compiler currently allows cyclic parameter source queries without diagnostics. |

---

## 3. Representative Scenarios & State Transition Baselines

### `SCENARIO_1_PARENT_CHILD` — One Parent and One Child Cascade (@country -> @state)

- **Fixture File:** `tests/fixtures/reporting/cascading-slicers/parent_child_cascade.rptsql`
- **Status:** ✅ Supported Today
- **Trigger Action:** `SET_PARAMETER(@country, 'Canada')`
- **Invalidated Descendants:** `@state`
- **Expected Query Refreshes:** `2`
- **Reset Policy:** `RetainIfEligibleElseResetToFirst`
- **Notes:** Runnable today via sequential query execution; automated reactive client propagation pending Phase 6

**Initial State:**
```json
{
  "ParameterValues": {
    "@country": "USA",
    "@state": "CA"
  },
  "EligibleOptionSets": {
    "@country": [
      "USA",
      "Canada"
    ],
    "@state": [
      "CA",
      "NY",
      "TX"
    ]
  }
}
```

**Expected Final State:**
```json
{
  "ParameterValues": {
    "@country": "Canada",
    "@state": "ON"
  },
  "EligibleOptionSets": {
    "@country": [
      "USA",
      "Canada"
    ],
    "@state": [
      "ON",
      "BC"
    ]
  }
}
```

### `SCENARIO_2_TWO_PARENTS_ONE_CHILD` — Two Parents Feeding One Child (@region + @year -> @category)

- **Fixture File:** `tests/fixtures/reporting/cascading-slicers/two_parents_one_child_cascade.rptsql`
- **Status:** ✅ Supported Today
- **Trigger Action:** `SET_PARAMETER(@region, 'EMEA')`
- **Invalidated Descendants:** `@category`
- **Expected Query Refreshes:** `2`
- **Reset Policy:** `RetainIfEligibleElseResetToFirst`
- **Notes:** Runnable today; @category remains 'Hardware' as it is eligible in EMEA 2026

**Initial State:**
```json
{
  "ParameterValues": {
    "@region": "North America",
    "@year": "2026",
    "@category": "Hardware"
  },
  "EligibleOptionSets": {
    "@region": [
      "North America",
      "EMEA"
    ],
    "@year": [
      "2025",
      "2026"
    ],
    "@category": [
      "Hardware",
      "Cloud Services"
    ]
  }
}
```

**Expected Final State:**
```json
{
  "ParameterValues": {
    "@region": "EMEA",
    "@year": "2026",
    "@category": "Hardware"
  },
  "EligibleOptionSets": {
    "@region": [
      "North America",
      "EMEA"
    ],
    "@year": [
      "2025",
      "2026"
    ],
    "@category": [
      "Hardware",
      "Security"
    ]
  }
}
```

### `SCENARIO_3_THREE_LEVEL_CASCADE` — Three-Level Hierarchy Cascade (@division -> @department -> @team)

- **Fixture File:** `tests/fixtures/reporting/cascading-slicers/three_level_cascade.rptsql`
- **Status:** ✅ Supported Today
- **Trigger Action:** `SET_PARAMETER(@division, 'Sales')`
- **Invalidated Descendants:** `@department, @team`
- **Expected Query Refreshes:** `3`
- **Reset Policy:** `AlwaysResetToFirst`
- **Notes:** Runnable today via pipeline cascade; atomic multi-level client batching pending Phase 6

**Initial State:**
```json
{
  "ParameterValues": {
    "@division": "Engineering",
    "@department": "Core Platform",
    "@team": "Storage Engine"
  },
  "EligibleOptionSets": {
    "@division": [
      "Engineering",
      "Sales"
    ],
    "@department": [
      "Core Platform",
      "Security"
    ],
    "@team": [
      "Storage Engine",
      "Query Optimizer"
    ]
  }
}
```

**Expected Final State:**
```json
{
  "ParameterValues": {
    "@division": "Sales",
    "@department": "Enterprise",
    "@team": "Strategic Accounts"
  },
  "EligibleOptionSets": {
    "@division": [
      "Engineering",
      "Sales"
    ],
    "@department": [
      "Enterprise",
      "SMB"
    ],
    "@team": [
      "Strategic Accounts"
    ]
  }
}
```

### `SCENARIO_4_NULL_AND_ALL` — Null and All Option Selections (__ALL__ / NULL Wildcards)

- **Fixture File:** `tests/fixtures/reporting/cascading-slicers/null_and_all_selections.rptsql`
- **Status:** ✅ Supported Today
- **Trigger Action:** `SET_PARAMETER(@selected_channel, 'Digital')`
- **Invalidated Descendants:** `@selected_campaign`
- **Expected Query Refreshes:** `2`
- **Reset Policy:** `RetainIfEligibleElseResetToFirst`
- **Notes:** Runnable today; wildcard pattern matching supported via SQL COALESCE / OR clauses

**Initial State:**
```json
{
  "ParameterValues": {
    "@selected_channel": "__ALL__",
    "@selected_campaign": "__ALL__"
  },
  "EligibleOptionSets": {
    "@selected_channel": [
      "__ALL__",
      "Digital",
      "Direct",
      "Partner"
    ],
    "@selected_campaign": [
      "__ALL__",
      "Search_SEM",
      "Social_Paid",
      "Direct_Mail",
      "Affiliate_Network"
    ]
  }
}
```

**Expected Final State:**
```json
{
  "ParameterValues": {
    "@selected_channel": "Digital",
    "@selected_campaign": "__ALL__"
  },
  "EligibleOptionSets": {
    "@selected_channel": [
      "__ALL__",
      "Digital",
      "Direct",
      "Partner"
    ],
    "@selected_campaign": [
      "__ALL__",
      "Search_SEM",
      "Social_Paid"
    ]
  }
}
```

### `SCENARIO_5_MULTISELECT_PARENT` — Multi-Select Parent Values (@regions -> @territory)

- **Fixture File:** `tests/fixtures/reporting/cascading-slicers/multiselect_parent_cascade.rptsql`
- **Status:** ✅ Supported Today
- **Trigger Action:** `SET_PARAMETER(@regions, 'West')`
- **Invalidated Descendants:** `@territory`
- **Expected Query Refreshes:** `2`
- **Reset Policy:** `AlwaysResetToFirst`
- **Notes:** Runnable today via CSV string matching; native array-typed parameter binding pending Phase 6

**Initial State:**
```json
{
  "ParameterValues": {
    "@regions": "North,East",
    "@territory": "Boston"
  },
  "EligibleOptionSets": {
    "@regions": [
      "North",
      "East",
      "West"
    ],
    "@territory": [
      "Boston",
      "Manchester",
      "New York City",
      "Philadelphia"
    ]
  }
}
```

**Expected Final State:**
```json
{
  "ParameterValues": {
    "@regions": "West",
    "@territory": "Seattle"
  },
  "EligibleOptionSets": {
    "@regions": [
      "North",
      "East",
      "West"
    ],
    "@territory": [
      "Seattle",
      "San Francisco"
    ]
  }
}
```

### `SCENARIO_6_INVALID_DESCENDANT_RESET` — Invalid Descendant Selection & Auto-Reset

- **Fixture File:** `tests/fixtures/reporting/cascading-slicers/invalid_descendant_selection.rptsql`
- **Status:** ⏳ Pending Phase 6
- **Trigger Action:** `SET_PARAMETER(@country, 'Germany')`
- **Invalidated Descendants:** `@state`
- **Expected Query Refreshes:** `2`
- **Reset Policy:** `RetainIfEligibleElseResetToFirst`
- **Notes:** Pending Phase 6; current client keeps stale '@state=TX' value until manual interaction

**Initial State:**
```json
{
  "ParameterValues": {
    "@country": "USA",
    "@state": "TX"
  },
  "EligibleOptionSets": {
    "@country": [
      "USA",
      "Germany"
    ],
    "@state": [
      "TX",
      "CA"
    ]
  }
}
```

**Expected Final State:**
```json
{
  "ParameterValues": {
    "@country": "Germany",
    "@state": "BY"
  },
  "EligibleOptionSets": {
    "@country": [
      "USA",
      "Germany"
    ],
    "@state": [
      "BY",
      "BE"
    ]
  }
}
```

### `SCENARIO_7_RAPID_TRANSITIONS` — Rapid Consecutive Parent Changes (Debounce & Convergence)

- **Fixture File:** `tests/fixtures/reporting/cascading-slicers/rapid_parent_transitions.rptsql`
- **Status:** ⏳ Pending Phase 6
- **Trigger Action:** `SET_PARAMETER(@dept, 'D3')`
- **Invalidated Descendants:** `@role`
- **Expected Query Refreshes:** `2`
- **Reset Policy:** `AlwaysResetToFirst`
- **Notes:** Pending Phase 6; debounce and cancellation tokens will coalesce rapid burst queries

**Initial State:**
```json
{
  "ParameterValues": {
    "@dept": "D1",
    "@role": "R1_Lead"
  },
  "EligibleOptionSets": {
    "@dept": [
      "D1",
      "D2",
      "D3"
    ],
    "@role": [
      "R1_Lead",
      "R1_Senior"
    ]
  }
}
```

**Expected Final State:**
```json
{
  "ParameterValues": {
    "@dept": "D3",
    "@role": "R3_Architect"
  },
  "EligibleOptionSets": {
    "@dept": [
      "D1",
      "D2",
      "D3"
    ],
    "@role": [
      "R3_Architect",
      "R3_Consultant"
    ]
  }
}
```

### `SCENARIO_8_CYCLIC_DEPENDENCY` — Cyclic Dependency Cascade (Diagnostic Target)

- **Fixture File:** `tests/fixtures/reporting/cascading-slicers/cyclic_dependency_cascade.rptsql`
- **Status:** ⏳ Pending Phase 6
- **Trigger Action:** `SET_PARAMETER(@paramA, 'A2')`
- **Invalidated Descendants:** `@paramB, @paramA`
- **Expected Query Refreshes:** `0`
- **Reset Policy:** `RetainValueEvenIfInvalid`
- **Notes:** Pending Phase 6; future compiler linter will emit diagnostic error on cyclic parameter graph

**Initial State:**
```json
{
  "ParameterValues": {
    "@paramA": "A1",
    "@paramB": "B1"
  },
  "EligibleOptionSets": {
    "@paramA": [
      "A1",
      "A2"
    ],
    "@paramB": [
      "B1",
      "B2"
    ]
  }
}
```

**Expected Final State:**
```json
{
  "ParameterValues": {
    "@paramA": "A2",
    "@paramB": "B1"
  },
  "EligibleOptionSets": {
    "@paramA": [
      "A1",
      "A2"
    ],
    "@paramB": [
      "B1",
      "B2"
    ]
  }
}
```

---

## 4. Dependency Graph Topological Ordering & Cycles

### Fixture: `parent_child_cascade.rptsql`
- **Root Parameters:** `@country`
- **Topological Execution Order:** `@country -> @state`
- **Has Cycles:** `NO`

### Fixture: `two_parents_one_child_cascade.rptsql`
- **Root Parameters:** `@region, @year`
- **Topological Execution Order:** `@region -> @year -> @category`
- **Has Cycles:** `NO`

### Fixture: `three_level_cascade.rptsql`
- **Root Parameters:** `@division`
- **Topological Execution Order:** `@division -> @department -> @team`
- **Has Cycles:** `NO`

### Fixture: `cyclic_dependency_cascade.rptsql`
- **Root Parameters:** ``
- **Topological Execution Order:** ``
- **Has Cycles:** `YES (Cycle Detected)`
  - Detected Cycle: `@paramA -> @paramB -> @paramA`

