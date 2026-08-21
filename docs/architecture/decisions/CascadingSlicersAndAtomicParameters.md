# Design Spec: Cascading Slicers and Atomic Parameter State

**Status:** Accepted
**Date:** 2026-08-21
**Owners:** Reporting, Report Hosting, Portal, and Language Tooling

## 1. Decision

Report-SQL compiles filter dependencies into a directed acyclic graph. A filter visual produces the
parameter named by its `ON_CHANGE = SET_PARAMETER(...)` action. A dependent visual consumes that
parameter in one of two explicit execution modes:

- **LOCAL** — `CASCADE PARENTS` maps parent parameters to columns in the visual's retained source
  vector. Filtering works in live sessions, offline snapshots, and other no-query consumers.
- **LIVE** — dependencies are inferred from parameter references in the visual's inline `SOURCE`
  query. The query is refreshed after all of its parents have reached their transaction value.

All changes, descendant option refreshes, and invalid-selection repairs form one transaction. A
consumer observes either the previous manifest/parameter state or the final state; intermediate
parent/child combinations are never published.

## 2. Accepted Syntax

```sql
CREATE VISUAL CountryFilter AS SLICER (
  SOURCE = &country_options,
  MAPPINGS (VALUE = CountryCode, LABEL = CountryName),
  CASCADE (
    MODE = LOCAL,
    PARENTS (@Region = RegionCode),
    INVALID = CLEAR,
    NULL = ALL,
    ALL_VALUE = '*',
    MULTISELECT = ANY
  ),
  ACTIONS (ON_CHANGE = SET_PARAMETER(@Country, CountryCode))
);
```

```sql
CREATE VISUAL CityFilter AS MULTISELECT (
  SOURCE = (
    SELECT CityCode, CityName
    FROM location.City
    WHERE CountryCode = @Country
  ),
  MAPPINGS (VALUE = CityCode, LABEL = CityName),
  CASCADE (
    MODE = LIVE,
    INVALID = FIRST,
    NULL = ALL,
    ALL_VALUE = '*',
    MULTISELECT = ANY
  ),
  ACTIONS (ON_CHANGE = SET_PARAMETER(@Cities, CityCode))
);
```

The `CASCADE` options are:

- **MODE = LOCAL | LIVE** — Required. `LOCAL` filters retained rows; `LIVE` re-evaluates an inline
  query. There is no heuristic mode switch.
- **PARENTS (@Parameter = Column, ...)** — Required for `LOCAL`; forbidden for `LIVE`. Each mapping
  says which retained source column is constrained by a parent parameter.
- **INVALID = CLEAR | FIRST | ERROR** — `CLEAR` removes invalid child selections, `FIRST` selects the
  first eligible value in stable source order, and `ERROR` rejects the complete transaction.
- **NULL = ALL | MATCH** — `ALL` makes a null/empty parent non-constraining. `MATCH` constrains it to
  null/empty source values.
- **ALL_VALUE = 'literal'** — A selected sentinel with the same non-constraining meaning as `NULL =
  ALL`. The default is `'*'`.
- **MULTISELECT = ANY | ALL** — For a multi-valued parent, `ANY` retains options associated with at
  least one selected value. `ALL` retains an option only when its retained source rows associate it
  with every selected parent value.

Dependencies remain inferred from the live query rather than repeated in syntax. A LIVE visual whose
query references no parameter is an authoring error. A LOCAL visual may have multiple parents and may
participate at any depth.

## 3. Canonical Parameter Values

Parameter names are case-insensitive and canonicalized with a leading `@`.

- A scalar selection is stored as its invariant string value.
- Null/clear is stored as the empty string for compatibility with current session variables.
- A multi-selection is stored as a JSON string array, such as `["US","CA"]`. The server accepts the
  previous comma-separated representation during graph evaluation. New browser clients send JSON
  arrays, which preserves commas inside values; repaired descendant values are emitted canonically.
- Duplicate multi-values are removed while preserving first occurrence.
- `ALL_VALUE` is represented by its literal scalar and is never mixed with ordinary multi-values.

## 4. Graph Compilation

The compiler indexes every filter visual that has exactly one `ON_CHANGE SET_PARAMETER` producer.
For each cascading visual it resolves parent parameters:

- LOCAL: the keys in `PARENTS`.
- LIVE: `ParameterScanner` output from its inline `SOURCE` query.

An edge connects the visual producing a parent parameter to the consuming visual. Missing producers,
duplicate producers, self-dependencies, cycles, invalid modes, and invalid LOCAL columns are authoring
diagnostics. Topological order is stable: source declaration order breaks ties. A cycle diagnostic
prints the complete parameter path, for example `@Region -> @Country -> @Region`.

Non-filter charts that reference changed parameters remain ordinary refresh dependents. They refresh
after cascading parameter reconciliation, using the final transaction state.

## 5. Atomic State Transition

For one request, the coordinator:

1. Acquires the report-session mutation lock, snapshots evaluator variables, and creates detached
   visual state for every affected refresh.
2. Canonicalizes the complete incoming batch; the batch is unordered author intent.
3. Walks cascading visuals once in stable topological order.
4. For LOCAL nodes, filters the immutable retained source vector. For LIVE nodes, applies the staged
   parent values privately and performs one query refresh.
5. Reconciles the child parameter using its invalid-selection policy. A repair becomes another staged
   parameter change available to later descendants.
6. Refreshes non-filter dependents once using the final parameter dictionary.
7. Commits evaluator variables, baseline parameters, host parameter state, manifest parameters,
   option rows, and transaction metadata together.

Any query, policy, or validation failure restores evaluator variables, discards the detached visual
state, and rejects the request.
`ERROR` therefore never leaves the newly selected parent visible. Concurrent requests serialize at
the session lock; each transaction starts from the last committed state.

## 6. Local, Snapshot, and Live Equivalence

`VisualManifest` carries a cascade contract plus immutable retained option rows for LOCAL nodes.
Snapshots serialize both. The pure local state machine consumes only manifest data and a parameter
dictionary, so offline/snapshot consumers and conformance tests do not need an evaluator.

The live coordinator uses the same selection canonicalization, option-value extraction, policy
reconciliation, topological order, and result contract. The only replaceable operation is how a node's
eligible rows are obtained. Conformance compares final parameters, eligible option values, node order,
and refresh counts across local, serialized-snapshot, and live providers.

## 7. Observable Result

Each successful manifest includes the last cascade transaction's commit time, final changed
parameters, and refreshed visuals. The manifest's cascade graph separately exposes stable parameter
order and edges, while each cascading visual carries its mode and policies.

This metadata contains names and selected report values but no credentials or connection details.

## 8. Tooling and Compatibility

- Existing visuals without `CASCADE` keep current behavior.
- `CASCADE` is preserved by `AstSerializer`, parser round trips, and surgical Report Builder edits.
- Report Builder exposes the accepted fields but does not invent dependencies.
- Analysis emits graph and policy diagnostics before execution.
- LSP completion and hover use the exact accepted tokens and examples above.
- Rich control redesign and keyboard-navigation work remain out of scope.

## 9. Rejected Alternatives

- **Infer local columns by matching parameter names.** Column naming is not a dependable contract.
- **Refresh descendants client-side one request at a time.** This exposes invalid intermediate state
  and multiplies queries.
- **Always clear invalid descendants.** FIRST and transactional ERROR are required operational
  policies and must be explicit.
- **Comma-separated multi-values as the canonical form.** Commas are legitimate data values.
- **A second dependency declaration for LIVE queries.** The query AST is already authoritative and
  duplicate declarations drift.

## References

- [Grammar-of-Graphics Spec/IR](GrammarOfGraphicsSpecIR.md)
- [Report-SQL Guide](../../guides/feature-guides/report-sql.md)
- [Reporting Architecture](../Reporting.md)
- [Language Syntax Standards](../standards/Language_Syntax_Standards.md)
