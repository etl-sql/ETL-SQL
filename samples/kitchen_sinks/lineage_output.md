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
| 2026-04-30 13:23:48 | SELECT INTO | (Direct Values) | **d**: Display name<br/>**owner**: SecurityTeam<br/>**author**: Kitchen Sink Test<br/>**version**: 1.0.0<br/>**description**: Validates diagnostics and metadata features<br/>**engine_version**: 0.6.0 |
| 2026-04-30 13:23:48 | SELECT INTO | (Direct Values) | **d**: Internal user ID<br/>**PII**: true<br/>**author**: Kitchen Sink Test<br/>**version**: 1.0.0<br/>**description**: Validates diagnostics and metadata features<br/>**engine_version**: 0.6.0 |
| 2026-04-30 13:23:48 | SELECT | (Direct Values) | **d**: Display name<br/>**owner**: SecurityTeam<br/>**author**: Kitchen Sink Test<br/>**version**: 1.0.0<br/>**description**: Validates diagnostics and metadata features<br/>**engine_version**: 0.6.0 |
| 2026-04-30 13:23:48 | SELECT | (Direct Values) | **d**: Internal user ID<br/>**PII**: true<br/>**author**: Kitchen Sink Test<br/>**version**: 1.0.0<br/>**description**: Validates diagnostics and metadata features<br/>**engine_version**: 0.6.0 |
