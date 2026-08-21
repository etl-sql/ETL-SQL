# Phase 7 Semantic Authoring Readiness & Capability Inventory

> **Timestamp (UTC):** 2026-08-21 14:53:59 | **Branch:** `test/reporting-phase7-semantic-readiness`

---

## 1. Semantic Capability & Gap Inventory

| Concept | Category | Contracts | Renderers | Report-SQL | Status & Technical Detail |
| :--- | :--- | :---: | :---: | :---: | :--- |
| **Ordered Multi-Layer Marks (RECT, LINE, POINT, RULE, AREA, ARC)** | `Layer Composition` | ✅ | ✅ | ❌ | **Supported in ChartSpec & PlotPlan contracts; limited to named COMBO and OVERLAYS in Report-SQL syntax**<br/>_Report-SQL parser cannot currently declare arbitrary multi-layer specs (e.g. LAYER RECT + LAYER LINE + LAYER POINT in a single visual body)_ |
| **Dual-Axis Bindings (Primary Y vs Secondary Y2)** | `Scales & Axes` | ✅ | ✅ | ✅ | **Supported in contracts and exposed via COMBO (Y on Left, Y2 on Right)**<br/>_Fully functional in ChartSpec, ECharts, and SVG renderers; terminal renderer provides placeholder/table fallback_ |
| **Scale Resolution Policies (Shared vs Independent)** | `Faceting & Resolution` | ✅ | ❌ | ❌ | **Declared in ScaleResolutionSpec (Shared vs Independent); multi-facet splitting is not yet implemented in PlotPlanResolver**<br/>_PlotPlanResolver currently resolves single-panel bounds and does not split datasets into sub-panel coordinate spaces_ |
| **1D and 2D Facet Grid Specifications** | `Faceting & Resolution` | ✅ | ❌ | ❌ | **FacetSpec model exists in contracts; multi-panel layout resolution pending Phase 7**<br/>_Requires facet-aware canvas grid partitioning in SVG and ECharts lowerers_ |
| **Coordinate Systems (Cartesian, Transposed, Polar)** | `Coordinates` | ✅ | ✅ | ✅ | **Fully supported across contracts, ECharts, and SVG renderers (BAR, HBAR, PIE, DONUT)**<br/>_Complete across contracts and native SVG; terminal renderer maps Polar to proportional Spectre breakdown tables_ |
| **Layer-Level Independent Data Source Overrides** | `Layer Composition` | ❌ | ❌ | ❌ | **Absent in contracts; all layers currently bind to the single root dataReference**<br/>_MarkLayerSpec does not contain an optional DataReference override property_ |
| **In-Line Binding Expressions & Transforms** | `Data Transformation` | ❌ | ❌ | ❌ | **Absent in contracts; transformations must be pre-staged via SQL queries into #temp tables**<br/>_FieldBinding only accepts simple column identifier strings, not scalar expression ASTs_ |
| **Conditional Visual Mark Encodings** | `Encodings & Styles` | ❌ | ❌ | ❌ | **Absent in contracts; mark styles are static StyleToken lists per layer**<br/>_No contract model for condition-driven style rules (e.g. IF value < 0 THEN color='#EF4444')_ |
| **Accessible Semantic Fallbacks & Plain-Text Summaries** | `Accessibility & Governance` | ✅ | ✅ | ✅ | **Fully supported via SemanticFallback and AccessibleSummary generation in PlotPlanResolver**<br/>_Production-ready: produces deterministic structured text tables and screen-reader narratives_ |

---

## 2. Multi-Surface Semantic Conformance Matrix

| Rendering Surface | Operational Conformance Level |
| :--- | :--- |
| **ECharts Browser Runtime** | Full support for Cartesian, Transposed, Polar, Multi-Layer, Dual-Axis, and Series Palette. |
| **Native PlotPlan SVG** | Full support for Native Vector Cartesian, Dual-Axis, Lines, Rects, Points, Rules, Arcs, Micro-Sparklines, and Micro-Progress without browser/V8 dependencies. |
| **Spectre Terminal Output** | Support for Braille Continuous Curves, Bar Panels, Slicers, and Semantic Plain-Text Breakdown Tables. |
| **Accessible Screen-Reader Fallbacks** | Deterministic SemanticFallbackItem tables, Summary narratives, and GFM tables. |

---

## 3. Key Architectural Findings

- ChartSpec and PlotPlan provide a robust semantic foundation capable of multi-layer composition, dual-scale coordinates, and rich accessibility.
- Primary semantic gaps for Phase 7 are at the authoring/syntax layer (Report-SQL grammar for layer definitions) and client-side reactive facet splitting.
- Existing rendering pipelines (ECharts and Native SVG) cleanly decouple from Report-SQL syntax and can consume future advanced authoring specs without architectural redesign.
