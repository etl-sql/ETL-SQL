# Data Lineage Report: #FinalAudit

## Visual Graph
```mermaid
graph TD
    FinalAudit_AuditDate["#FinalAudit.AuditDate"]
    SalesAudit["#SalesAudit"]
    SalesAudit --> FinalAudit_AuditDate
    SalesAudit_OrderDate["#SalesAudit.OrderDate"]
    Orders_OrderDate["Orders.OrderDate"]
    Orders_OrderDate --> SalesAudit_OrderDate
    Orders["Orders"]
    SalesAudit_UserId["#SalesAudit.UserId"]
    Orders_UserId["Orders.UserId"]
    Orders_UserId --> SalesAudit_UserId
    Orders_OrderDate --> SalesAudit_OrderDate
    Orders_UserId --> SalesAudit_UserId
    FinalAudit_OrderDate["#FinalAudit.OrderDate"]
    SalesAudit_OrderDate --> FinalAudit_OrderDate
    Orders_OrderDate --> SalesAudit_OrderDate
    Orders_OrderDate --> SalesAudit_OrderDate
    FinalAudit_UserId["#FinalAudit.UserId"]
    SalesAudit_UserId --> FinalAudit_UserId
    Orders_UserId --> SalesAudit_UserId
    Orders_UserId --> SalesAudit_UserId
    SalesAudit --> FinalAudit_AuditDate
    SalesAudit_OrderDate --> FinalAudit_OrderDate
    SalesAudit_UserId --> FinalAudit_UserId

```

## Detailed Audit Log
| Timestamp | Operation | Sources | Metadata |
| :--- | :--- | :--- | :--- |
| 2026-05-01 02:03:06 | SELECT INTO | #SalesAudit | **author**: chuck<br/>**engine_version**: 0.6.0 |
| 2026-05-01 02:03:06 | SELECT INTO | #SalesAudit (OrderDate) | **owner**: SalesDept<br/>**d**: Timestamp of sale<br/>**author**: chuck<br/>**engine_version**: 0.6.0<br/>*Derived From*: OrderDate: Timestamp of sale |
| 2026-05-01 02:03:06 | SELECT INTO | #SalesAudit (UserId) | **owner**: SalesDept<br/>**sensitive**: true<br/>**d**: Customer UID<br/>**author**: chuck<br/>**engine_version**: 0.6.0<br/>*Derived From*: UserId: Customer UID |
| 2026-05-01 02:03:06 | SELECT | #SalesAudit | **author**: chuck<br/>**engine_version**: 0.6.0 |
| 2026-05-01 02:03:06 | SELECT | #SalesAudit (OrderDate) | **owner**: SalesDept<br/>**d**: Timestamp of sale<br/>**author**: chuck<br/>**engine_version**: 0.6.0<br/>*Derived From*: OrderDate: Timestamp of sale |
| 2026-05-01 02:03:06 | SELECT | #SalesAudit (UserId) | **owner**: SalesDept<br/>**sensitive**: true<br/>**d**: Customer UID<br/>**author**: chuck<br/>**engine_version**: 0.6.0<br/>*Derived From*: UserId: Customer UID |
