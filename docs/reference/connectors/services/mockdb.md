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
| `AuditTrail` | `LogID`, `EventID`, `Principal`, `Operation`, `OccurredAt`, `Duration`, `ResultCode`, `TraceID` |
| `departments` | `DeptID`, `DeptName`, `Budget` |
| `Numbers` / `DimNumbers` / `Tally` | `Number`, `IsEven`, `IsOdd` |
| `Dates` / `DimDate` | `DateKey`, `Date`, `FullDateISO`, `Year`, `Quarter`, `YearQuarter`, `Month`, `MonthName`, `MonthShortName`, `YearMonth`, `Day`, `DayOfWeek`, `DayName`, `DayShortName`, `DayOfYear`, `ISOWeek`, `IsWeekend`, `IsWeekday`, `IsMonthStart`, `IsMonthEnd`, `IsQuarterStart`, `IsQuarterEnd`, `IsYearStart`, `IsYearEnd`, `FiscalYear`, `FiscalQuarter`, `RelativeDays` |
| `Times` / `DimTime` | `TimeKey`, `Time`, `FullTime24`, `FullTime12`, `HourMinute24`, `HourMinute12`, `Hour`, `Hour12`, `Minute`, `Second`, `AmPm`, `TimeOfDay`, `MinuteOfDay`, `SecondOfDay`, `HalfHour`, `QuarterHour`, `HourBand`, `HalfHourBand`, `QuarterHourBand`, `IsBusinessHours`, `WorkShift` |
| `Geography` / `DimGeography` | `GeoKey`, `StateCode`, `StateName`, `CountryCode`, `CountryCode3`, `CountryName`, `Continent`, `Region`, `SubRegion`, `TimeZone`, `UtcOffsetHours`, `IsDomestic` |
| `Currencies` / `DimCurrencies` | `CurrencyKey`, `CurrencyCode`, `NumericCode`, `CurrencyName`, `Symbol`, `MinorUnitDigits`, `CountryName`, `IsBaseCurrency`, `StandardFormatPattern` |
| `Flags` / `DimFlags` | `FlagKey`, `FlagValue`, `FlagName`, `YesNo`, `YesNoChar`, `ActiveInactive`, `EnabledDisabled`, `PassFail`, `SuccessFailure`, `IncludeExclude`, `OnOff` |

All tables are pre-seeded with sample rows. Schema exploration returns declared column types, including
integer width, decimal precision, date/time, `DATETIMEOFFSET`, and `UNIQUEIDENTIFIER` columns, so
`eng.columns` and editor explorers are useful without a real database. `INSERT`, `UPDATE`, and `DELETE`
operations are accepted but **do not persist** between sessions.

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
