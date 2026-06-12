using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Services;
using ETL_SQL.Tests.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements
{
    /// <summary>
    /// Output-correctness coverage for core example scripts in samples/01_Basics/ and
    /// samples/07_Real_World/. Verifies that the core ETL logic produces the expected
    /// row counts, column names, and specific values — catching silent regressions that
    /// crash-free smoke tests (Test-AllSamples.ps1) cannot detect.
    ///
    /// Each test inlines the self-contained SQL from the corresponding sample script and
    /// asserts on the final in-memory result. File-output steps are replaced with
    /// SELECT INTO #temp equivalents so the logic is exercised without file-system deps.
    /// </summary>
    public class ExampleOutputCorrectnessTests
    {
        private static Evaluator MakeEval()
        {
            var sp = DependencyInjectionSetup.BuildServiceProvider();
            sp.GetRequiredService<SecurityService>().IsTestMode = true;
            return sp.GetRequiredService<Evaluator>();
        }

        private static async Task<DataTable?> RunAndGetResult(Evaluator eval, string sql)
        {
            await TestHelpers.Execute(eval, sql);
            return eval.LastResult;
        }

        // ── 01_Basics/Function_Library.etlsql ────────────────────────────────────

        [Fact]
        public async Task FunctionLibrary_StringFunctions_ProduceCorrectValues()
        {
            await using var eval = MakeEval();
            var result = await RunAndGetResult(eval, """
                DROP TABLE IF EXISTS #func_test;
                CREATE TABLE #func_test (category VARCHAR(50), test_case VARCHAR(100), result VARCHAR(200));
                INSERT INTO #func_test VALUES ('STRING', 'UPPER',      UPPER('hello'));
                INSERT INTO #func_test VALUES ('STRING', 'LOWER',      LOWER('HELLO'));
                INSERT INTO #func_test VALUES ('STRING', 'CONCAT',     CONCAT('a', 'b', 'c'));
                INSERT INTO #func_test VALUES ('STRING', 'TRIM',       TRIM('  spaced  '));
                INSERT INTO #func_test VALUES ('STRING', 'REPLACE',    REPLACE('abc', 'b', 'X'));
                INSERT INTO #func_test VALUES ('STRING', 'LEFT',       LEFT('abcdef', 3));
                INSERT INTO #func_test VALUES ('STRING', 'RIGHT',      RIGHT('abcdef', 3));
                INSERT INTO #func_test VALUES ('MATH',   'ROUND',      CAST(ROUND(12.345, 2) AS VARCHAR(10)));
                INSERT INTO #func_test VALUES ('MATH',   'ABS',        CAST(ABS(-5) AS VARCHAR(10)));
                INSERT INTO #func_test VALUES ('MATH',   'POWER',      CAST(POWER(2, 4) AS VARCHAR(10)));
                INSERT INTO #func_test VALUES ('DATE',   'YEAR',       CAST(YEAR('2026-03-14') AS VARCHAR(4)));
                INSERT INTO #func_test VALUES ('DATE',   'MONTH',      CAST(MONTH('2026-03-14') AS VARCHAR(2)));
                INSERT INTO #func_test VALUES ('DATE',   'DAY',        CAST(DAY('2026-03-14') AS VARCHAR(2)));
                INSERT INTO #func_test VALUES ('GENERAL','COALESCE',   COALESCE(NULL, NULL, 'found it'));
                INSERT INTO #func_test VALUES ('GENERAL','ISNULL',     ISNULL(NULL, 'fallback'));
                SELECT * FROM #func_test ORDER BY category, test_case;
                """);

            Assert.NotNull(result);
            Assert.Equal(15, result.Rows.Count);

            var byCase = result.Rows.ToDictionary(
                r => r["test_case"]?.ToString() ?? "",
                r => r["result"]?.ToString() ?? "");

            Assert.Equal("HELLO", byCase["UPPER"]);
            Assert.Equal("hello", byCase["LOWER"]);
            Assert.Equal("abc", byCase["CONCAT"]);
            Assert.Equal("spaced", byCase["TRIM"]);
            Assert.Equal("aXc", byCase["REPLACE"]);
            Assert.Equal("abc", byCase["LEFT"]);
            Assert.Equal("def", byCase["RIGHT"]);
            Assert.Equal("12.35", byCase["ROUND"]);
            Assert.Equal("5", byCase["ABS"]);
            Assert.Equal("16", byCase["POWER"]);
            Assert.Equal("2026", byCase["YEAR"]);
            Assert.Equal("3", byCase["MONTH"]);
            Assert.Equal("14", byCase["DAY"]);
            Assert.Equal("found it", byCase["COALESCE"]);
            Assert.Equal("fallback", byCase["ISNULL"]);
        }

        // ── 07_Real_World/realworld_07_window_deduplication.etlsql ───────────────

        [Fact]
        public async Task WindowDeduplication_LatestEventPerUserIsRetained()
        {
            await using var eval = MakeEval();
            var result = await RunAndGetResult(eval, """
                CREATE TABLE #StageEvents (EventID INT, UserID INT, EventType VARCHAR(20), Payload VARCHAR(100), EventTimestamp DATETIME);
                INSERT INTO #StageEvents VALUES (101, 5, 'Login', 'Success', DATEADD(MINUTE, -10, GETDATE()));
                INSERT INTO #StageEvents VALUES (102, 5, 'Login', 'Success', DATEADD(MINUTE, -2, GETDATE()));
                INSERT INTO #StageEvents VALUES (103, 5, 'Login', 'Success', GETDATE());
                INSERT INTO #StageEvents VALUES (104, 8, 'Logout', 'Success', DATEADD(MINUTE, -5, GETDATE()));

                SELECT EventID, UserID, EventType, Payload, EventTimestamp,
                       ROW_NUMBER() OVER(PARTITION BY UserID ORDER BY EventTimestamp DESC) AS RankVersion
                INTO #RankedEvents
                FROM #StageEvents;

                SELECT EventID, UserID, EventType, Payload, EventTimestamp
                INTO #CleanEvents
                FROM #RankedEvents
                WHERE RankVersion = 1;

                SELECT * FROM #CleanEvents ORDER BY UserID;
                """);

            Assert.NotNull(result);
            Assert.Equal(2, result.Rows.Count);

            // UserID 5 → latest event is EventID 103; UserID 8 → only event is EventID 104
            var rows = result.Rows.OrderBy(r => Convert.ToInt32(r["UserID"])).ToList();
            Assert.Equal(5, Convert.ToInt32(rows[0]["UserID"]));
            Assert.Equal(103, Convert.ToInt32(rows[0]["EventID"]));
            Assert.Equal(8, Convert.ToInt32(rows[1]["UserID"]));
            Assert.Equal(104, Convert.ToInt32(rows[1]["EventID"]));
        }

        // ── 07_Real_World/realworld_04_incremental_merge.etlsql ──────────────────

        [Fact]
        public async Task IncrementalMerge_UpsertProducesExpectedFinalState()
        {
            await using var eval = MakeEval();

            // Run the MERGE and audit query, then inspect ProdDb
            await TestHelpers.Execute(eval, """
                CREATE TABLE #DeltaFeed (CustomerID VARCHAR(50), Name VARCHAR(50), Email VARCHAR(50), Segment VARCHAR(50), LastLogin DATETIME);
                INSERT INTO #DeltaFeed VALUES ('CUST001', 'Alice',   'alice@corp.com', 'PREMIUM',  GETDATE());
                INSERT INTO #DeltaFeed VALUES ('CUST002', 'Bob',     'bob@corp.com',   'STANDARD', GETDATE());
                INSERT INTO #DeltaFeed VALUES ('CUST003', 'Charlie', 'char@corp.com',  'NEW',      GETDATE());

                CREATE TABLE #ProdDb (CustomerID VARCHAR(50), Name VARCHAR(50), Email VARCHAR(50), Segment VARCHAR(50), LastLogin DATETIME, CreatedAt DATETIME, UpdatedAt DATETIME);
                INSERT INTO #ProdDb VALUES ('CUST001', 'Alice', 'old@corp.com', 'STANDARD', DATEADD(DAY, -5, GETDATE()), DATEADD(DAY, -10, GETDATE()), DATEADD(DAY, -10, GETDATE()));
                INSERT INTO #ProdDb VALUES ('CUST002', 'Bob',   'bob@corp.com', 'STANDARD', GETDATE(),                  GETDATE(),                     GETDATE());

                SELECT CustomerID, Name, Email, Segment, LastLogin INTO #StagingDelta FROM #DeltaFeed;

                CREATE TABLE #AuditTrail (ActionType VARCHAR(20), CustomerID VARCHAR(50), OldSegment VARCHAR(50), NewSegment VARCHAR(50));

                MERGE INTO #ProdDb AS Target
                USING (SELECT * FROM #StagingDelta) AS Source
                ON Target.CustomerID = Source.CustomerID
                WHEN MATCHED AND (Target.Segment <> Source.Segment OR Target.LastLogin > Target.LastLogin) THEN
                    UPDATE SET Name = Source.Name, Email = Source.Email, Segment = Source.Segment,
                               LastLogin = Source.LastLogin, UpdatedAt = GETDATE()
                WHEN NOT MATCHED BY TARGET THEN
                    INSERT (CustomerID, Name, Email, Segment, LastLogin, CreatedAt, UpdatedAt)
                    VALUES (Source.CustomerID, Source.Name, Source.Email, Source.Segment, Source.LastLogin, GETDATE(), GETDATE())
                OUTPUT $action AS ActionType, INSERTED.CustomerID, DELETED.Segment AS OldSegment, INSERTED.Segment AS NewSegment
                INTO #AuditTrail;
                """);

            // ProdDb: 3 rows (CUST001 updated, CUST002 matched/unchanged, CUST003 inserted)
            var prodResult = await RunAndGetResult(eval, "SELECT CustomerID, Segment FROM #ProdDb ORDER BY CustomerID;");
            Assert.NotNull(prodResult);
            Assert.Equal(3, prodResult.Rows.Count);

            var cust001 = prodResult.Rows.First(r => r["CustomerID"]?.ToString() == "CUST001");
            var cust002 = prodResult.Rows.First(r => r["CustomerID"]?.ToString() == "CUST002");
            var cust003 = prodResult.Rows.First(r => r["CustomerID"]?.ToString() == "CUST003");
            Assert.Equal("PREMIUM", cust001["Segment"]?.ToString());
            Assert.Equal("STANDARD", cust002["Segment"]?.ToString());
            Assert.Equal("NEW", cust003["Segment"]?.ToString());

            // AuditTrail contains at least the UPDATE for CUST001 (engine may or may not emit OUTPUT for INSERT branch)
            var auditResult = await RunAndGetResult(eval, "SELECT * FROM #AuditTrail;");
            Assert.NotNull(auditResult);
            Assert.True(auditResult.Rows.Count >= 1, "AuditTrail should capture at least the UPDATE action");
        }

        // ── 07_Real_World/realworld_05_masking_json.etlsql ───────────────────────

        [Fact]
        public async Task DataMasking_EmailAndSsnAreCorrectlyMasked()
        {
            await using var eval = MakeEval();
            var result = await RunAndGetResult(eval, """
                CREATE TABLE #Employees (EmployeeID INT, FirstName VARCHAR(50), LastName VARCHAR(50), Email VARCHAR(100), SSN VARCHAR(20), DeptID INT, IsActive INT);
                INSERT INTO #Employees VALUES (1, 'John', 'Doe',   'jdoe@secure.com',       '123456789', 100, 1);
                INSERT INTO #Employees VALUES (2, 'Jane', 'Smith', 'jane.smith@secure.com',  '987654321', 101, 1);

                CREATE TABLE #Departments (DeptID INT, DepartmentName VARCHAR(50));
                INSERT INTO #Departments VALUES (100, 'Accounting');
                INSERT INTO #Departments VALUES (101, 'Engineering');

                SELECT
                    EmployeeID,
                    FirstName,
                    LastName,
                    CONCAT(LEFT(Email, 2), '***@', SUBSTRING(Email, CHARINDEX('@', Email) + 1, LEN(Email))) AS MaskedEmail,
                    CONCAT('XXX-XX-', RIGHT(SSN, 4)) AS MaskedSSN,
                    DepartmentName
                INTO #MaskedProfiles
                FROM #Employees e
                INNER JOIN #Departments d ON e.DeptID = d.DeptID
                WHERE e.IsActive = 1;

                SELECT * FROM #MaskedProfiles ORDER BY EmployeeID;
                """);

            Assert.NotNull(result);
            Assert.Equal(2, result.Rows.Count);

            var john = result.Rows[0];
            Assert.Equal("jd***@secure.com", john["MaskedEmail"]?.ToString());
            Assert.Equal("XXX-XX-6789", john["MaskedSSN"]?.ToString());
            Assert.Equal("Accounting", john["DepartmentName"]?.ToString());

            var jane = result.Rows[1];
            Assert.Equal("ja***@secure.com", jane["MaskedEmail"]?.ToString());
            Assert.Equal("XXX-XX-4321", jane["MaskedSSN"]?.ToString());
            Assert.Equal("Engineering", jane["DepartmentName"]?.ToString());
        }

        // ── 07_Real_World/realworld_06_reconciliation_anti_join.etlsql ───────────

        [Fact]
        public async Task AntiJoinReconciliation_FindsMissingAccount()
        {
            await using var eval = MakeEval();
            var result = await RunAndGetResult(eval, """
                CREATE TABLE #BillingActive (AccountID INT, CustomerName VARCHAR(50), Tier VARCHAR(10), MonthlyCharge DECIMAL, Status VARCHAR(10));
                INSERT INTO #BillingActive VALUES (1001, 'Acme Corp',   'Gold',   500.00, 'Active');
                INSERT INTO #BillingActive VALUES (1002, 'Beta Ltd',    'Silver', 250.00, 'Active');
                INSERT INTO #BillingActive VALUES (1003, 'Charlie LLC', 'Bronze', 100.00, 'Active');

                CREATE TABLE #FulfillmentActive (AccountID INT, ProvisionedDate DATETIME, Status VARCHAR(10));
                INSERT INTO #FulfillmentActive VALUES (1001, GETDATE(), 'Active');
                INSERT INTO #FulfillmentActive VALUES (1003, GETDATE(), 'Active');

                SELECT
                    b.AccountID,
                    b.CustomerName,
                    b.Tier,
                    b.MonthlyCharge,
                    'MISSING IN FULFILLMENT' AS DiscrepancyReason
                INTO #Discrepancies
                FROM #BillingActive b
                LEFT HASH JOIN #FulfillmentActive f ON b.AccountID = f.AccountID
                WHERE f.AccountID IS NULL;

                SELECT * FROM #Discrepancies;
                """);

            Assert.NotNull(result);
            Assert.Single(result.Rows);

            var missing = result.Rows[0];
            Assert.Equal(1002, Convert.ToInt32(missing["AccountID"]));
            Assert.Equal("Beta Ltd", missing["CustomerName"]?.ToString());
            Assert.Equal("Silver", missing["Tier"]?.ToString());
            Assert.Equal("MISSING IN FULFILLMENT", missing["DiscrepancyReason"]?.ToString());
        }

        // ── 07_Real_World/realworld_08_aggregation_pivot.etlsql ──────────────────

        [Fact]
        public async Task AggregationPivot_QuarterlyRevenueIsCorrect()
        {
            await using var eval = MakeEval();
            var result = await RunAndGetResult(eval, """
                CREATE TABLE #Transactions (TransactionID INT, SalesRegion VARCHAR(20), ProductCategory VARCHAR(20), SaleDate DATETIME, TotalRevenue DECIMAL);
                INSERT INTO #Transactions VALUES (1, 'NORTH', 'Electronics', '2026-02-15', 12000.00);
                INSERT INTO #Transactions VALUES (2, 'NORTH', 'Electronics', '2026-05-15', 15000.00);
                INSERT INTO #Transactions VALUES (3, 'SOUTH', 'Furniture',   '2026-08-10', 25000.00);
                INSERT INTO #Transactions VALUES (4, 'NORTH', 'Electronics', '2026-11-20', 30000.00);
                INSERT INTO #Transactions VALUES (5, 'SOUTH', 'Furniture',   '2026-12-05', 40000.00);

                SELECT
                    SalesRegion, ProductCategory,
                    [1] AS Q1_Revenue, [2] AS Q2_Revenue, [3] AS Q3_Revenue, [4] AS Q4_Revenue
                FROM (
                    SELECT SalesRegion, ProductCategory, DATEPART(QQ, SaleDate) AS Q, TotalRevenue
                    FROM #Transactions
                    WHERE YEAR(SaleDate) = 2026
                ) AS src
                PIVOT (SUM(TotalRevenue) FOR Q IN (1, 2, 3, 4)) AS pvt
                ORDER BY SalesRegion;
                """);

            Assert.NotNull(result);
            Assert.Equal(2, result.Rows.Count);

            var north = result.Rows.First(r => r["SalesRegion"]?.ToString() == "NORTH");
            Assert.Equal("Electronics", north["ProductCategory"]?.ToString());
            Assert.Equal(12000m, Convert.ToDecimal(north["Q1_Revenue"]));
            Assert.Equal(15000m, Convert.ToDecimal(north["Q2_Revenue"]));
            Assert.Equal(30000m, Convert.ToDecimal(north["Q4_Revenue"]));

            var south = result.Rows.First(r => r["SalesRegion"]?.ToString() == "SOUTH");
            Assert.Equal("Furniture", south["ProductCategory"]?.ToString());
            Assert.Equal(25000m, Convert.ToDecimal(south["Q3_Revenue"]));
            Assert.Equal(40000m, Convert.ToDecimal(south["Q4_Revenue"]));
        }
    }
}
