# Data Lineage Report: #Tagged

## Visual Graph
```mermaid
graph TD
    Tagged_UserName["#Tagged.UserName"]
    Tagged_UserId["#Tagged.UserId"]

```

## Detailed Audit Log
| Timestamp | Operation | Sources | Metadata |
| :--- | :--- | :--- | :--- |
| 2026-05-01 16:09:59 | SELECT INTO | (Direct Values) | **d**: Display name<br/>**owner**: SecurityTeam<br/>**author**: Kitchen Sink Test<br/>**version**: 1.0.0<br/>**description**: Validates diagnostics and metadata features<br/>**engine_version**: 0.6.0 |
| 2026-05-01 16:09:59 | SELECT INTO | (Direct Values) | **d**: Internal user ID<br/>**PII**: true<br/>**author**: Kitchen Sink Test<br/>**version**: 1.0.0<br/>**description**: Validates diagnostics and metadata features<br/>**engine_version**: 0.6.0 |
| 2026-05-01 16:09:59 | SELECT | (Direct Values) | **d**: Display name<br/>**owner**: SecurityTeam<br/>**author**: Kitchen Sink Test<br/>**version**: 1.0.0<br/>**description**: Validates diagnostics and metadata features<br/>**engine_version**: 0.6.0 |
| 2026-05-01 16:09:59 | SELECT | (Direct Values) | **d**: Internal user ID<br/>**PII**: true<br/>**author**: Kitchen Sink Test<br/>**version**: 1.0.0<br/>**description**: Validates diagnostics and metadata features<br/>**engine_version**: 0.6.0 |
