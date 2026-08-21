# CASCADE

Defines a dependent `SLICER` or `MULTISELECT` option set and the atomic policy used when parent parameter changes invalidate a descendant selection.

## Syntax

```sql
CASCADE (
  MODE = LOCAL | LIVE,
  [PARENTS (@parent_parameter = source_column, ...),]
  [INVALID = CLEAR | FIRST | ERROR,]
  [NULL = ALL | MATCH,]
  [ALL_VALUE = '*',]
  [MULTISELECT = ANY | ALL]
)
```

- **MODE = LOCAL** — Filters the option rows retained in the report manifest. It works offline and in snapshots and requires `PARENTS`.
- **MODE = LIVE** — Re-runs the visual's inline `SELECT`. Parent dependencies are inferred from parameter references in that query; `PARENTS` is not allowed.
- **PARENTS (@parameter = column)** — Maps each parent parameter to the column used to filter the local option vector. Multiple mappings are combined.
- **INVALID = CLEAR** — Clears a descendant selection that is no longer in its option set. This is the default.
- **INVALID = FIRST** — Selects the first remaining option when the old selection becomes invalid.
- **INVALID = ERROR** — Rejects and rolls back the complete parameter transaction when a descendant becomes invalid.
- **NULL = ALL** — Treats a null, empty, or `ALL_VALUE` parent selection as no filter. This is the default.
- **NULL = MATCH** — Matches a null parent selection only to null source values.
- **ALL_VALUE = value** — Defines the scalar sentinel used for the All selection. The default is `*`.
- **MULTISELECT = ANY** — Keeps a row that matches any selected parent value. This is the default.
- **MULTISELECT = ALL** — Requires the source value to satisfy every selected parent value.

Multi-select parameter values are emitted as JSON string arrays, such as `["north","south"]`. Legacy comma-separated input is accepted and canonicalized on the next committed transition. Dependencies are evaluated in stable topological order. Cycles, missing producers, and duplicate producers are authoring errors.

Parameter changes are transactional: the engine stages the complete parent/descendant state, refreshes LOCAL or LIVE option sets in dependency order, reconciles invalid selections, refreshes dependent visuals, and then publishes one manifest. `INVALID = ERROR` or a query failure restores the previous parameter and visual state.

## Examples

```sql
CREATE VISUAL CityFilter AS SLICER (
  SOURCE = #city_options,
  MAPPINGS (VALUE = CityCode, LABEL = CityName),
  ACTIONS (ON_CHANGE = SET_PARAMETER(@City, CityCode)),
  CASCADE (
    MODE = LOCAL,
    PARENTS (@Region = RegionCode),
    INVALID = CLEAR,
    NULL = ALL
  )
);
```

```sql
CREATE VISUAL CityFilter AS MULTISELECT (
  SOURCE = (
    SELECT CityCode, CityName
    FROM #city_options
    WHERE RegionCode = @Region AND SegmentCode = @Segment
  ),
  MAPPINGS (VALUE = CityCode, LABEL = CityName),
  ACTIONS (ON_CHANGE = SET_PARAMETER(@Cities, CityCode)),
  CASCADE (
    MODE = LIVE,
    INVALID = FIRST,
    MULTISELECT = ANY
  )
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
- [VISUAL Reference](visual.md)
