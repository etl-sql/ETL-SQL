# MOCKDB

Built-in, zero-configuration in-memory database for script development and testing. No credentials, no
server, no configuration required. Accepts all DDL and DML operations but discards its data when the
session ends.

```sql
CREATE CONNECTION <name> AS MOCKDB();
```

## Pre-populated tables

| Table | Columns |
| :--- | :--- |
| `Users` | `UserID`, `UserName`, `Email`, `ExternalID`, `RegistrationDate`, `PreciseTime`, `LastLoginOffset` |
| `Products` | `ProductID`, `ProductName`, `Category`, `Cost`, `Price`, `StockLevel`, `Discontinued`, `WeightGrams`, `SkidGuid` |
| `Orders` / `Sales` | `SaleID`, `OrderDate`, `CustomerID`, `ProductID`, `Quantity`, `UnitPrice`, `Total`, `Region`, `ShipTimeOffset`, `ProcessDuration` |
| `Employee` | `EmpID`, `FirstName`, `LastName`, `Name`, `DeptID`, `Salary`, `HireDate`, `ManagerID`, `Status`, `Active`, `GlobalID` |
| `departments` | `DeptID`, `DeptName`, `Budget` |

All tables are pre-seeded with sample rows. `INSERT`, `UPDATE`, and `DELETE` operations are accepted but
**do not persist** between sessions.

## Example

```sql
CREATE CONNECTION m AS MOCKDB();

SELECT u.UserName, o.Total
INTO #UserOrders
FROM m.Users AS u
JOIN m.Orders AS o ON u.UserID = o.CustomerID;

-- Test an EXECUTE block
EXECUTE m INTO #emp
BEGIN
    SELECT EmpID, Name FROM Employee WHERE Active = 1;
END
```

> [!WARNING]
> `MOCKDB` is strictly for development and testing. Do not use it in production scripts.

## References

- [Service Connectors](README.md)
- [Connectors](../README.md)
