# ETL-SQL Development TODO List

## Bugs
### Report Portal
- [x] Structure DAG jumbled on large maps → Fixed: `render()` in `designer.js` now computes a fit-to-view zoom from the actual node bounding box (`min(containerW/dataW, containerH/dataH, 1.0)`, floor 0.15) so all nodes are visible on first open regardless of graph size. `roam: true` still lets users pan/zoom further.
- [x] Dependencies transformation column shows only category name → Fixed: `formatTransformationKind(kind, expr, fns)` now shows the actual function names for Aggregation/FunctionCall (e.g. "SUM", "COUNT", "UPPER()"), extracts the target type for Cast (e.g. "→ INT"), and keeps the full expression as a hover tooltip.
