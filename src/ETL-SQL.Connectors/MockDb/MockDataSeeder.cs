using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;

namespace ETL_SQL.Connectors.MockDb
{
    /// <summary>
    /// Service for generating high-fidelity mock data for test scenarios.
    /// </summary>
    public interface IMockDataSeeder
    {
        Task SeedDataAsync(Dictionary<string, DataTable> tables, Random rng);

        /// <summary>
        /// Declared column metadata per table, keyed exactly as the seeded table dictionary is.
        ///
        /// Declared rather than inferred from the seeded rows on purpose: every numeric is a
        /// <see cref="decimal"/> at runtime, so inference would report <c>DECIMAL</c> for
        /// <c>Quantity</c> (int) and <c>WeightGrams</c> (bigint) alike and quietly misdescribe the
        /// schema the explorers show.
        ///
        /// Defaults to empty so alternate seeders need no change; an empty map simply leaves the
        /// existing <c>ANY</c> behaviour in place.
        /// </summary>
        IReadOnlyDictionary<string, IReadOnlyList<CatalogColumn>> GetDeclaredSchema() =>
            new Dictionary<string, IReadOnlyList<CatalogColumn>>();
    }

    public class MockDataSeeder : IMockDataSeeder
    {
        private static CatalogColumn Col(string name, string type, bool nullable = false, bool primaryKey = false) =>
            new(name, type, nullable, primaryKey, null, new Dictionary<string, string>());

        // Types match what SeedDataAsync actually writes — note SaleID/LogID/WeightGrams are seeded
        // as long (BIGINT) while Quantity/StockLevel are int, a distinction lost at runtime.
        private static readonly IReadOnlyDictionary<string, IReadOnlyList<CatalogColumn>> Schema =
            new Dictionary<string, IReadOnlyList<CatalogColumn>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Users"] =
                [
                    Col("UserID", "INT", primaryKey: true),
                    Col("UserName", "VARCHAR"),
                    Col("Email", "VARCHAR"),
                    Col("ExternalID", "UNIQUEIDENTIFIER"),
                    Col("RegistrationDate", "DATE"),
                    Col("PreciseTime", "DATETIME2"),
                    Col("LastLoginOffset", "DATETIMEOFFSET")
                ],
                ["FILE"] =
                [
                    Col("patient_id", "INT", primaryKey: true),
                    Col("name", "VARCHAR")
                ],
                ["Products"] =
                [
                    Col("ProductID", "INT", primaryKey: true),
                    Col("ProductName", "VARCHAR"),
                    Col("Category", "VARCHAR"),
                    Col("Cost", "DECIMAL(18,2)"),
                    Col("Price", "DECIMAL(18,2)"),
                    Col("StockLevel", "INT"),
                    // Seeded as an int 1/0 flag, so declared INT rather than BIT — the declaration
                    // describes what is stored, not what the name suggests.
                    Col("Discontinued", "INT"),
                    Col("WeightGrams", "BIGINT"),
                    Col("SkidGuid", "UNIQUEIDENTIFIER")
                ],
                ["Sales"] =
                [
                    Col("SaleID", "BIGINT", primaryKey: true),
                    Col("OrderDate", "DATETIME2"),
                    Col("CustomerID", "INT"),
                    Col("ProductID", "INT"),
                    Col("Quantity", "INT"),
                    Col("UnitPrice", "DECIMAL(18,2)"),
                    Col("Total", "DECIMAL(18,2)"),
                    Col("Region", "VARCHAR"),
                    Col("ShipTimeOffset", "DATETIMEOFFSET"),
                    Col("ProcessDuration", "TIME")
                ],
                ["Employee"] =
                [
                    Col("EmpID", "INT", primaryKey: true),
                    Col("FirstName", "VARCHAR"),
                    Col("LastName", "VARCHAR"),
                    Col("Name", "VARCHAR"),
                    Col("DeptID", "INT"),
                    Col("Salary", "DECIMAL(18,2)"),
                    Col("HireDate", "DATE"),
                    // The first employee has no manager, so this column really is nullable.
                    Col("ManagerID", "INT", nullable: true),
                    Col("Status", "INT"),
                    Col("Active", "INT"),
                    Col("GlobalID", "UNIQUEIDENTIFIER")
                ],
                ["AuditTrail"] =
                [
                    Col("LogID", "BIGINT", primaryKey: true),
                    Col("EventID", "UNIQUEIDENTIFIER"),
                    Col("Principal", "VARCHAR"),
                    Col("Operation", "VARCHAR"),
                    Col("OccurredAt", "DATETIMEOFFSET"),
                    Col("Duration", "TIME"),
                    Col("ResultCode", "INT"),
                    Col("TraceID", "UNIQUEIDENTIFIER")
                ],
                ["departments"] =
                [
                    Col("DeptID", "INT", primaryKey: true),
                    Col("DeptName", "VARCHAR"),
                    Col("Budget", "DECIMAL(18,2)")
                ],
                ["Numbers"] =
                [
                    Col("Number", "INT", primaryKey: true),
                    Col("IsEven", "INT"),
                    Col("IsOdd", "INT")
                ],
                ["Dates"] =
                [
                    Col("DateKey", "INT", primaryKey: true),
                    Col("Date", "DATE"),
                    Col("Year", "INT"),
                    Col("Quarter", "INT"),
                    Col("Month", "INT"),
                    Col("MonthName", "VARCHAR"),
                    Col("Day", "INT"),
                    Col("DayOfWeek", "INT"),
                    Col("DayName", "VARCHAR"),
                    Col("IsWeekend", "INT"),
                    Col("FiscalYear", "INT")
                ],
                ["Times"] =
                [
                    Col("TimeKey", "INT", primaryKey: true),
                    Col("Time", "TIME"),
                    Col("Hour", "INT"),
                    Col("Hour12", "INT"),
                    Col("Minute", "INT"),
                    Col("Second", "INT"),
                    Col("AmPm", "VARCHAR"),
                    Col("TimeOfDay", "VARCHAR")
                ],
                ["Geography"] =
                [
                    Col("StateCode", "VARCHAR", primaryKey: true),
                    Col("StateName", "VARCHAR"),
                    Col("CountryCode", "VARCHAR"),
                    Col("CountryName", "VARCHAR"),
                    Col("Region", "VARCHAR"),
                    Col("TimeZone", "VARCHAR")
                ],
                ["Currencies"] =
                [
                    Col("CurrencyCode", "VARCHAR", primaryKey: true),
                    Col("CurrencyName", "VARCHAR"),
                    Col("Symbol", "VARCHAR")
                ],
                ["Flags"] =
                [
                    Col("FlagKey", "INT", primaryKey: true),
                    Col("FlagValue", "INT"),
                    Col("FlagName", "VARCHAR"),
                    Col("YesNo", "VARCHAR"),
                    Col("ActiveInactive", "VARCHAR")
                ]
            };

        // The seeder publishes several tables under more than one name; the declared schema follows
        // the same aliases so a qualified lookup does not fall back to ANY.
        private static readonly IReadOnlyDictionary<string, string> Aliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Orders"] = "Sales",
                ["Employee_Log"] = "Employee",
                ["DemoDb.dbo.Employee"] = "Employee",
                ["hr.departments"] = "departments",
                ["DimDate"] = "Dates",
                ["Dim_Date"] = "Dates",
                ["DimTime"] = "Times",
                ["Dim_Time"] = "Times",
                ["DimNumbers"] = "Numbers",
                ["Dim_Numbers"] = "Numbers",
                ["Tally"] = "Numbers",
                ["DimGeography"] = "Geography",
                ["DimCurrencies"] = "Currencies"
            };

        public IReadOnlyDictionary<string, IReadOnlyList<CatalogColumn>> GetDeclaredSchema()
        {
            var map = new Dictionary<string, IReadOnlyList<CatalogColumn>>(Schema, StringComparer.OrdinalIgnoreCase);
            foreach (var (alias, target) in Aliases)
            {
                if (Schema.TryGetValue(target, out var columns)) map[alias] = columns;
            }
            return map;
        }

        public async Task SeedDataAsync(Dictionary<string, DataTable> tables, Random rng)
        {
            // 1. Users
            var users = new DataTable();
            users.SetColumns(new[] { "UserID", "UserName", "Email", "ExternalID", "RegistrationDate", "PreciseTime", "LastLoginOffset" });
            for (int i = 1; i <= 150; i++)
            {
                var regDate = DateTime.Today.AddDays(-rng.Next(365));
                await users.AddRowAsync(new Row
                {
                    ["UserID"] = i,
                    ["UserName"] = $"User_{i}",
                    ["Email"] = $"user{i}@example.com",
                    ["ExternalID"] = Guid.NewGuid(),
                    ["RegistrationDate"] = regDate,
                    ["PreciseTime"] = regDate.AddTicks(rng.Next(10000000)), // High precision ticks
                    ["LastLoginOffset"] = DateTimeOffset.Now.AddHours(-rng.Next(168))
                });
            }
            tables["Users"] = users;

            // 2. Products
            var products = new DataTable();
            products.SetColumns(new[] { "ProductID", "ProductName", "Category", "Cost", "Price", "StockLevel", "Discontinued", "WeightGrams", "SkidGuid" });
            string[] categories = { "Electronics", "Home", "Garden", "Toys", "Automotive" };
            for (int i = 1; i <= 120; i++)
            {
                var cost = (decimal)(rng.NextDouble() * 100 + 1);
                await products.AddRowAsync(new Row
                {
                    ["ProductID"] = 100 + i,
                    ["ProductName"] = $"Product_{100 + i}",
                    ["Category"] = categories[rng.Next(categories.Length)],
                    ["Cost"] = Math.Round(cost, 2),
                    ["Price"] = Math.Round(cost * 1.5m, 2),
                    ["StockLevel"] = rng.Next(1000),
                    ["Discontinued"] = rng.Next(100) < 5 ? 1 : 0,
                    ["WeightGrams"] = (long)rng.Next(50, 5000), // BigInt
                    ["SkidGuid"] = Guid.NewGuid()
                });
            }
            tables["Products"] = products;

            // 3. Sales
            var sales = new DataTable();
            sales.SetColumns(new[] { "SaleID", "OrderDate", "CustomerID", "ProductID", "Quantity", "UnitPrice", "Total", "Region", "ShipTimeOffset", "ProcessDuration" });
            string[] regions = { "North America", "EMEA", "APAC", "LATAM" };
            for (int i = 1; i <= 250; i++)
            {
                var price = (decimal)(rng.NextDouble() * 200 + 10);
                var qty = rng.Next(1, 10);
                await sales.AddRowAsync(new Row
                {
                    ["SaleID"] = (long)10000 + i,
                    ["OrderDate"] = DateTime.UtcNow.AddHours(-rng.Next(1000)),
                    ["CustomerID"] = rng.Next(1, 151),
                    ["ProductID"] = rng.Next(101, 221),
                    ["Quantity"] = qty,
                    ["UnitPrice"] = Math.Round(price, 2),
                    ["Total"] = Math.Round(price * qty, 2),
                    ["Region"] = regions[rng.Next(regions.Length)],
                    ["ShipTimeOffset"] = DateTimeOffset.UtcNow.AddDays(rng.Next(1, 5)),
                    ["ProcessDuration"] = TimeSpan.FromMilliseconds(rng.Next(100, 5000)) // TimeSpan
                });
            }
            tables["Sales"] = sales;
            tables["Orders"] = sales;

            // 4. Employees
            var employees = new DataTable();
            employees.SetColumns(new[] { "EmpID", "FirstName", "LastName", "Name", "DeptID", "Salary", "HireDate", "ManagerID", "Status", "Active", "GlobalID" });
            for (int i = 1; i <= 100; i++)
            {
                var firstName = $"First_{i}";
                var lastName = $"Last_{i}";
                await employees.AddRowAsync(new Row
                {
                    ["EmpID"] = i,
                    ["FirstName"] = firstName,
                    ["LastName"] = lastName,
                    ["Name"] = $"{firstName} {lastName}",
                    ["DeptID"] = rng.Next(1, 6),
                    ["Salary"] = (decimal)(rng.Next(4000000, 15000000)) / 100m, // Precise Decimal
                    ["HireDate"] = DateTime.Today.AddYears(-rng.Next(1, 10)).AddDays(rng.Next(365)),
                    ["ManagerID"] = i == 1 ? null : rng.Next(1, i),
                    ["Status"] = rng.Next(3),
                    ["Active"] = 1,
                    ["GlobalID"] = Guid.NewGuid()
                });
            }
            tables["Employee"] = employees;
            tables["Employee_Log"] = employees.Clone();
            tables["DemoDb.dbo.Employee"] = employees;

            // 5. AuditTrail
            var audit = new DataTable();
            audit.SetColumns(new[] { "LogID", "EventID", "Principal", "Operation", "OccurredAt", "Duration", "ResultCode", "TraceID" });
            for (int i = 1; i <= 300; i++)
            {
                await audit.AddRowAsync(new Row
                {
                    ["LogID"] = (long)i,
                    ["EventID"] = Guid.NewGuid(),
                    ["Principal"] = $"user_{rng.Next(1, 50)}@corp.local",
                    ["Operation"] = rng.Next(100) < 20 ? "DELETE" : "UPDATE",
                    ["OccurredAt"] = DateTimeOffset.UtcNow.AddMinutes(-rng.Next(10000)),
                    ["Duration"] = TimeSpan.FromTicks(rng.Next(1000, 10000000)), // High precision TimeSpan
                    ["ResultCode"] = 200,
                    ["TraceID"] = Guid.NewGuid()
                });
            }
            tables["AuditTrail"] = audit;

            // 6. Departments
            var deptTable = new DataTable();
            deptTable.SetColumns(new[] { "DeptID", "DeptName", "Budget" });
            string[] deptNames = { "Engineering", "Sales", "HR", "Finance", "Legal" };
            for (int i = 0; i < deptNames.Length; i++)
            {
                await deptTable.AddRowAsync(new Row
                {
                    ["DeptID"] = i + 1,
                    ["DeptName"] = deptNames[i],
                    ["Budget"] = (decimal)(rng.Next(50000000, 200000000)) / 100m
                });
            }
            tables["departments"] = deptTable;
            tables["hr.departments"] = deptTable;

            // 7. Numbers (Tally dimension - 1,000,000 numbers)
            var numbersSchema = new TableSchema(new[] { "Number", "IsEven", "IsOdd" });
            var numbers = new DataTable { Schema = numbersSchema };
            for (int i = 1; i <= 1000000; i++)
            {
                numbers.Rows.Add(new Row(numbersSchema, new object?[] { i, i % 2 == 0 ? 1 : 0, i % 2 != 0 ? 1 : 0 }));
            }
            tables["Numbers"] = numbers;
            tables["DimNumbers"] = numbers;
            tables["Dim_Numbers"] = numbers;
            tables["Tally"] = numbers;

            // 8. Dates (Ultimate Date dimension - 200 year range 1900-01-01 to 2100-01-01 + Sentinels)
            var datesSchema = new TableSchema(new[] {
                "DateKey", "Date", "FullDateISO", "Year", "Quarter", "YearQuarter",
                "Month", "MonthName", "MonthShortName", "YearMonth", "Day", "DayOfWeek",
                "DayName", "DayShortName", "DayOfYear", "ISOWeek", "IsWeekend", "IsWeekday",
                "IsMonthStart", "IsMonthEnd", "IsQuarterStart", "IsQuarterEnd", "IsYearStart",
                "IsYearEnd", "FiscalYear", "FiscalQuarter", "RelativeDays"
            });
            var dates = new DataTable { Schema = datesSchema };

            // Sentinel rows (-1 Unknown, -2 N/A)
            dates.Rows.Add(new Row(datesSchema, new object?[] {
                -1, new DateTime(1900, 1, 1), "1900-01-01", 1900, 1, "1900-Q1", 1, "Unknown", "Unk", 190001, 1, 0, "Unknown", "Unk", 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 1900, "FQ1", -999999
            }));
            dates.Rows.Add(new Row(datesSchema, new object?[] {
                -2, new DateTime(9999, 12, 31), "9999-12-31", 9999, 4, "9999-Q4", 12, "Not Applicable", "N/A", 999912, 31, 0, "N/A", "N/A", 365, 52, 0, 0, 0, 0, 0, 0, 0, 0, 9999, "FQ4", 999999
            }));

            var startCalendar = new DateTime(1900, 1, 1);
            var endCalendar = new DateTime(2100, 1, 1);
            var today = DateTime.Today;

            for (var d = startCalendar; d <= endCalendar; d = d.AddDays(1))
            {
                int dateKey = d.Year * 10000 + d.Month * 100 + d.Day;
                int quarter = ((d.Month - 1) / 3) + 1;
                int isWeekend = (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) ? 1 : 0;
                int isWeekday = isWeekend == 1 ? 0 : 1;
                int isMonthStart = d.Day == 1 ? 1 : 0;
                int isMonthEnd = d.Day == DateTime.DaysInMonth(d.Year, d.Month) ? 1 : 0;
                int isQuarterStart = (d.Day == 1 && (d.Month == 1 || d.Month == 4 || d.Month == 7 || d.Month == 10)) ? 1 : 0;
                int isQuarterEnd = (isMonthEnd == 1 && (d.Month == 3 || d.Month == 6 || d.Month == 9 || d.Month == 12)) ? 1 : 0;
                int isYearStart = (d.Day == 1 && d.Month == 1) ? 1 : 0;
                int isYearEnd = (d.Day == 31 && d.Month == 12) ? 1 : 0;

                dates.Rows.Add(new Row(datesSchema, new object?[] {
                    dateKey,
                    d,
                    d.ToString("yyyy-MM-dd"),
                    d.Year,
                    quarter,
                    $"{d.Year}-Q{quarter}",
                    d.Month,
                    d.ToString("MMMM"),
                    d.ToString("MMM"),
                    d.Year * 100 + d.Month,
                    d.Day,
                    (int)d.DayOfWeek,
                    d.ToString("dddd"),
                    d.ToString("ddd"),
                    d.DayOfYear,
                    System.Globalization.ISOWeek.GetWeekOfYear(d),
                    isWeekend,
                    isWeekday,
                    isMonthStart,
                    isMonthEnd,
                    isQuarterStart,
                    isQuarterEnd,
                    isYearStart,
                    isYearEnd,
                    d.Year,
                    $"FQ{quarter}",
                    (d.Date - today).Days
                }));
            }
            tables["Dates"] = dates;
            tables["DimDate"] = dates;
            tables["Dim_Date"] = dates;

            // 9. Times (Ultimate Time dimension - 1440 minutes + Sentinels)
            var times = new DataTable();
            times.SetColumns(new[] {
                "TimeKey", "Time", "FullTime24", "FullTime12", "HourMinute24", "HourMinute12",
                "Hour", "Hour12", "Minute", "Second", "AmPm", "TimeOfDay",
                "MinuteOfDay", "SecondOfDay", "HalfHour", "QuarterHour",
                "HourBand", "HalfHourBand", "QuarterHourBand", "IsBusinessHours", "WorkShift"
            });

            // Sentinel row (-1 Unknown)
            await times.AddRowAsync(new Row
            {
                ["TimeKey"] = -1,
                ["Time"] = TimeSpan.Zero,
                ["FullTime24"] = "00:00:00",
                ["FullTime12"] = "12:00:00 AM",
                ["HourMinute24"] = "00:00",
                ["HourMinute12"] = "12:00 AM",
                ["Hour"] = 0,
                ["Hour12"] = 12,
                ["Minute"] = 0,
                ["Second"] = 0,
                ["AmPm"] = "AM",
                ["TimeOfDay"] = "Unknown",
                ["MinuteOfDay"] = 0,
                ["SecondOfDay"] = 0,
                ["HalfHour"] = 0,
                ["QuarterHour"] = 0,
                ["HourBand"] = "Unknown",
                ["HalfHourBand"] = "Unknown",
                ["QuarterHourBand"] = "Unknown",
                ["IsBusinessHours"] = 0,
                ["WorkShift"] = "Unknown"
            });

            for (int h = 0; h < 24; h++)
            {
                for (int m = 0; m < 60; m++)
                {
                    int h12 = h % 12 == 0 ? 12 : h % 12;
                    string ampm = h < 12 ? "AM" : "PM";
                    string tod = h switch
                    {
                        >= 5 and < 12 => "Morning",
                        >= 12 and < 17 => "Afternoon",
                        >= 17 and < 21 => "Evening",
                        _ => "Night"
                    };
                    var ts = new TimeSpan(h, m, 0);

                    int minuteOfDay = h * 60 + m;
                    int secondOfDay = minuteOfDay * 60;
                    int halfHour = minuteOfDay / 30;
                    int quarterHour = minuteOfDay / 15;

                    int nextHour = (h + 1) % 24;
                    string hourBand = $"{h:D2}:00 - {nextHour:D2}:00";

                    int halfMStart = (m / 30) * 30;
                    int halfMEnd = halfMStart + 30;
                    int halfHEnd = halfMEnd == 60 ? nextHour : h;
                    string halfMEndStr = halfMEnd == 60 ? "00" : halfMEnd.ToString("D2");
                    string halfHourBand = $"{h:D2}:{halfMStart:D2} - {halfHEnd:D2}:{halfMEndStr}";

                    int qMStart = (m / 15) * 15;
                    int qMEnd = qMStart + 15;
                    int qHEnd = qMEnd == 60 ? nextHour : h;
                    string qMEndStr = qMEnd == 60 ? "00" : qMEnd.ToString("D2");
                    string quarterHourBand = $"{h:D2}:{qMStart:D2} - {qHEnd:D2}:{qMEndStr}";

                    int isBusinessHours = (h >= 8 && h < 17) ? 1 : 0;
                    string shift = h switch
                    {
                        >= 7 and < 15 => "Shift 1 (Day)",
                        >= 15 and < 23 => "Shift 2 (Evening)",
                        _ => "Shift 3 (Night)"
                    };

                    await times.AddRowAsync(new Row
                    {
                        ["TimeKey"] = h * 10000 + m * 100,
                        ["Time"] = ts,
                        ["FullTime24"] = $"{h:D2}:{m:D2}:00",
                        ["FullTime12"] = $"{h12:D2}:{m:D2}:00 {ampm}",
                        ["HourMinute24"] = $"{h:D2}:{m:D2}",
                        ["HourMinute12"] = $"{h12:D2}:{m:D2} {ampm}",
                        ["Hour"] = h,
                        ["Hour12"] = h12,
                        ["Minute"] = m,
                        ["Second"] = 0,
                        ["AmPm"] = ampm,
                        ["TimeOfDay"] = tod,
                        ["MinuteOfDay"] = minuteOfDay,
                        ["SecondOfDay"] = secondOfDay,
                        ["HalfHour"] = halfHour,
                        ["QuarterHour"] = quarterHour,
                        ["HourBand"] = hourBand,
                        ["HalfHourBand"] = halfHourBand,
                        ["QuarterHourBand"] = quarterHourBand,
                        ["IsBusinessHours"] = isBusinessHours,
                        ["WorkShift"] = shift
                    });
                }
            }
            tables["Times"] = times;
            tables["DimTime"] = times;
            tables["Dim_Time"] = times;

            // 10. Geography (Ultimate Geography dimension - ISO / US States & International Regions)
            var geo = new DataTable();
            geo.SetColumns(new[] { "GeoKey", "StateCode", "StateName", "CountryCode", "CountryCode3", "CountryName", "Continent", "Region", "SubRegion", "TimeZone", "UtcOffsetHours", "IsDomestic" });

            await geo.AddRowAsync(new Row
            {
                ["GeoKey"] = -1,
                ["StateCode"] = "XX",
                ["StateName"] = "Unknown",
                ["CountryCode"] = "XX",
                ["CountryCode3"] = "UNK",
                ["CountryName"] = "Unknown",
                ["Continent"] = "Unknown",
                ["Region"] = "Unknown",
                ["SubRegion"] = "Unknown",
                ["TimeZone"] = "UTC",
                ["UtcOffsetHours"] = 0,
                ["IsDomestic"] = 0
            });

            var sampleStates = new (int Key, string Code, string Name, string C2, string C3, string CName, string Cont, string Reg, string SubReg, string TZ, int Offset, int Domestic)[]
            {
                (1, "CA", "California", "US", "USA", "United States", "North America", "West", "Pacific", "PST", -8, 1),
                (2, "NY", "New York", "US", "USA", "United States", "North America", "East", "Middle Atlantic", "EST", -5, 1),
                (3, "TX", "Texas", "US", "USA", "United States", "North America", "South", "West South Central", "CST", -6, 1),
                (4, "FL", "Florida", "US", "USA", "United States", "North America", "South", "South Atlantic", "EST", -5, 1),
                (5, "IL", "Illinois", "US", "USA", "United States", "North America", "Midwest", "East North Central", "CST", -6, 1),
                (6, "WA", "Washington", "US", "USA", "United States", "North America", "West", "Pacific", "PST", -8, 1),
                (7, "ON", "Ontario", "CA", "CAN", "Canada", "North America", "Americas", "Central Canada", "EST", -5, 0),
                (8, "LND", "London", "GB", "GBR", "United Kingdom", "Europe", "EMEA", "Northern Europe", "GMT", 0, 0),
                (9, "BY", "Bavaria", "DE", "DEU", "Germany", "Europe", "EMEA", "Western Europe", "CET", 1, 0)
            };

            foreach (var st in sampleStates)
            {
                await geo.AddRowAsync(new Row
                {
                    ["GeoKey"] = st.Key,
                    ["StateCode"] = st.Code,
                    ["StateName"] = st.Name,
                    ["CountryCode"] = st.C2,
                    ["CountryCode3"] = st.C3,
                    ["CountryName"] = st.CName,
                    ["Continent"] = st.Cont,
                    ["Region"] = st.Reg,
                    ["SubRegion"] = st.SubReg,
                    ["TimeZone"] = st.TZ,
                    ["UtcOffsetHours"] = st.Offset,
                    ["IsDomestic"] = st.Domestic
                });
            }
            tables["Geography"] = geo;
            tables["DimGeography"] = geo;
            tables["Dim_Geography"] = geo;

            // 11. Currencies (Ultimate Currency dimension - ISO 4217)
            var currencies = new DataTable();
            currencies.SetColumns(new[] { "CurrencyKey", "CurrencyCode", "NumericCode", "CurrencyName", "Symbol", "MinorUnitDigits", "CountryName", "IsBaseCurrency", "StandardFormatPattern" });

            await currencies.AddRowAsync(new Row
            {
                ["CurrencyKey"] = -1,
                ["CurrencyCode"] = "XXX",
                ["NumericCode"] = 999,
                ["CurrencyName"] = "Unknown Currency",
                ["Symbol"] = "?",
                ["MinorUnitDigits"] = 2,
                ["CountryName"] = "Unknown",
                ["IsBaseCurrency"] = 0,
                ["StandardFormatPattern"] = "#,##0.00"
            });

            var sampleCurrencies = new (int Key, string Code, int NumCode, string Name, string Sym, int Digits, string Country, int BaseCurr, string Format)[]
            {
                (840, "USD", 840, "US Dollar", "$", 2, "United States", 1, "$#,##0.00"),
                (978, "EUR", 978, "Euro", "€", 2, "Eurozone", 0, "€#,##0.00"),
                (826, "GBP", 826, "British Pound", "£", 2, "United Kingdom", 0, "£#,##0.00"),
                (124, "CAD", 124, "Canadian Dollar", "C$", 2, "Canada", 0, "C$#,##0.00"),
                (36, "AUD", 36, "Australian Dollar", "A$", 2, "Australia", 0, "A$#,##0.00"),
                (392, "JPY", 392, "Japanese Yen", "¥", 0, "Japan", 0, "¥#,##0"),
                (756, "CHF", 756, "Swiss Franc", "CHF", 2, "Switzerland", 0, "CHF #,##0.00"),
                (156, "CNY", 156, "Chinese Yuan", "¥", 2, "China", 0, "¥#,##0.00")
            };

            foreach (var c in sampleCurrencies)
            {
                await currencies.AddRowAsync(new Row
                {
                    ["CurrencyKey"] = c.Key,
                    ["CurrencyCode"] = c.Code,
                    ["NumericCode"] = c.NumCode,
                    ["CurrencyName"] = c.Name,
                    ["Symbol"] = c.Sym,
                    ["MinorUnitDigits"] = c.Digits,
                    ["CountryName"] = c.Country,
                    ["IsBaseCurrency"] = c.BaseCurr,
                    ["StandardFormatPattern"] = c.Format
                });
            }
            tables["Currencies"] = currencies;
            tables["DimCurrencies"] = currencies;
            tables["Dim_Currencies"] = currencies;

            // 12. Flags (Ultimate Flag / Boolean dimension)
            var flags = new DataTable();
            flags.SetColumns(new[] { "FlagKey", "FlagValue", "FlagName", "YesNo", "YesNoChar", "ActiveInactive", "EnabledDisabled", "PassFail", "SuccessFailure", "IncludeExclude", "OnOff" });

            await flags.AddRowAsync(new Row
            {
                ["FlagKey"] = -1,
                ["FlagValue"] = -1,
                ["FlagName"] = "Unknown",
                ["YesNo"] = "Unknown",
                ["YesNoChar"] = "U",
                ["ActiveInactive"] = "Unknown",
                ["EnabledDisabled"] = "Unknown",
                ["PassFail"] = "Unknown",
                ["SuccessFailure"] = "Unknown",
                ["IncludeExclude"] = "Unknown",
                ["OnOff"] = "Unknown"
            });

            await flags.AddRowAsync(new Row
            {
                ["FlagKey"] = 0,
                ["FlagValue"] = 0,
                ["FlagName"] = "False",
                ["YesNo"] = "No",
                ["YesNoChar"] = "N",
                ["ActiveInactive"] = "Inactive",
                ["EnabledDisabled"] = "Disabled",
                ["PassFail"] = "Fail",
                ["SuccessFailure"] = "Failure",
                ["IncludeExclude"] = "Exclude",
                ["OnOff"] = "Off"
            });

            await flags.AddRowAsync(new Row
            {
                ["FlagKey"] = 1,
                ["FlagValue"] = 1,
                ["FlagName"] = "True",
                ["YesNo"] = "Yes",
                ["YesNoChar"] = "Y",
                ["ActiveInactive"] = "Active",
                ["EnabledDisabled"] = "Enabled",
                ["PassFail"] = "Pass",
                ["SuccessFailure"] = "Success",
                ["IncludeExclude"] = "Include",
                ["OnOff"] = "On"
            });

            tables["Flags"] = flags;
            tables["DimFlags"] = flags;
            tables["Dim_Flags"] = flags;
        }
    }
}
