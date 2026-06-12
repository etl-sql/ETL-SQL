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
    }

    public class MockDataSeeder : IMockDataSeeder
    {
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
        }
    }
}
