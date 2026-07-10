# Plan Fallback Ranking

| CandidatePath | ReasonCode | Count | ObservedElapsedMs | ObservedSpillBytes | ObservedRowsAffected | ObservedPeakWorkingSetMB | SourceCount |
| :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: |
| ColumnarProjection | UnsupportedExpression | 3 | 84.5 | 0 | 120000 | 142.2 | 1 |
| ColumnarAggregate | UnsupportedType | 2 | 119.75 | 0 | 85000 | 155.8 | 1 |
| SqlPushdown | ConnectorCapabilityMissing | 2 | 44.25 | 0 | 45000 | 118.4 | 1 |
| ExternalJoin | MemoryAdmissionRejected | 1 | 410 | 268435456 | 500000 | 252 | 1 |
| RowPipeline | SemanticGuard | 1 | 230.5 | 125829120 | 300000 | 248.9 | 1 |
