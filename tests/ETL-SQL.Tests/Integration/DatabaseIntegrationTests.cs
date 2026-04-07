using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Testcontainers.Oracle;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;
using Spectre.Console;

namespace ETL_SQL.Tests
{
    [Collection("Database collection")]
    public class DatabaseIntegrationTests
    {
        private readonly DatabaseFixture _fixture;

        public DatabaseIntegrationTests(DatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task TestDbToDbTransfer()
        {
            AnsiConsole.MarkupLine("  - Scenario: Postgres -> SQL Server...");
            
            try
            {
                var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                await eval.Evaluate(new Parser(new Lexer($"CREATE CONNECTION pg ON POSTGRES('{_fixture.PostgresConnectionString}');").Tokenize()).Parse());
                await eval.Evaluate(new Parser(new Lexer($"CREATE CONNECTION ms ON MSSQL('{_fixture.SqlConnectionString}');").Tokenize()).Parse());

                // Create source data in Postgres
                await eval.Evaluate(new Parser(new Lexer("CREATE TABLE pg.SourceData (ID INT, Name VARCHAR(100), Value DECIMAL(18,2));").Tokenize()).Parse());
                await eval.Evaluate(new Parser(new Lexer("INSERT INTO pg.SourceData (ID, Name, Value) VALUES (1, 'Test', 100.50), (2, 'Other', 200.75);").Tokenize()).Parse());

                // Create target table in SqlServer
                await eval.Evaluate(new Parser(new Lexer("CREATE TABLE ms.TargetData (ID INT, Name VARCHAR(100), Value DECIMAL(18,2));").Tokenize()).Parse());

                // Perform transfer
                AnsiConsole.MarkupLine("    Performing transfer...");
                await eval.Evaluate(new Parser(new Lexer("INSERT INTO ms.TargetData SELECT * FROM pg.SourceData;").Tokenize()).Parse());
                AnsiConsole.MarkupLine("    Transfer command executed.");

                // Verify
                await eval.Evaluate(new Parser(new Lexer("SELECT COUNT(*) as Total FROM ms.TargetData;").Tokenize()).Parse());
                Assert.Equal(2, Convert.ToInt32(eval.LastResult?.Rows[0]["TOTAL"]));
            }
            finally
            {
                // No teardown needed for fixture
            }
        }

        [Fact]
        public async Task TestFileToDb()
        {
            AnsiConsole.MarkupLine("  - Scenario: CSV -> Oracle...");
            
            try
            {
                var connStr = _fixture.OracleConnectionString;
                var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                await eval.Evaluate(new Parser(new Lexer($"CREATE CONNECTION db ON ORACLE('{connStr}');").Tokenize()).Parse());

                // Create CSV file
                string csvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_data.csv");
                await File.WriteAllTextAsync(csvPath, "ID,Name\n10,John\n20,Jane");

                await eval.Evaluate(new Parser(new Lexer($"CREATE CONNECTION f ON FLATFILE('{csvPath}') WITH (HEADER = TRUE);").Tokenize()).Parse());

                // Create oracle table
                await eval.Evaluate(new Parser(new Lexer("CREATE TABLE db.People (ID NUMBER, Name VARCHAR2(50));").Tokenize()).Parse());

                // Load from file to db
                await eval.Evaluate(new Parser(new Lexer("INSERT INTO db.People SELECT * FROM f;").Tokenize()).Parse());

                // Verify
                await eval.Evaluate(new Parser(new Lexer("SELECT * FROM db.People ORDER BY ID;").Tokenize()).Parse());
                Assert.NotNull(eval.LastResult);
                Assert.Equal(2, eval.LastResult.Rows.Count);
                Assert.Equal("John", eval.LastResult.Rows[0]["NAME"]?.ToString());

                if (File.Exists(csvPath)) File.Delete(csvPath);
            }
            finally
            {
                // No teardown needed for fixture
            }
        }

        [Fact]
        public async Task TestMultiDbJoin()
        {
            AnsiConsole.MarkupLine("  - Scenario: MSSQL + Postgres JOIN -> Postgres...");
            
            try
            {
                var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                await eval.Evaluate(new Parser(new Lexer($"CREATE CONNECTION ms ON MSSQL('{_fixture.SqlConnectionString}');").Tokenize()).Parse());
                await eval.Evaluate(new Parser(new Lexer($"CREATE CONNECTION pg ON POSTGRES('{_fixture.PostgresConnectionString}');").Tokenize()).Parse());

                // Setup tables
                await eval.Evaluate(new Parser(new Lexer("CREATE TABLE ms.Customers (ID INT, Name VARCHAR(50));").Tokenize()).Parse());
                await eval.Evaluate(new Parser(new Lexer("INSERT INTO ms.Customers VALUES (1, 'Alice'), (2, 'Bob');").Tokenize()).Parse());

                await eval.Evaluate(new Parser(new Lexer("CREATE TABLE pg.Orders (CustID INT, Amount DECIMAL);").Tokenize()).Parse());
                await eval.Evaluate(new Parser(new Lexer("INSERT INTO pg.Orders VALUES (1, 100.50), (1, 50.25), (2, 200.00);").Tokenize()).Parse());

                // Target table
                await eval.Evaluate(new Parser(new Lexer("CREATE TABLE pg.Summary (Name VARCHAR(50), Total DECIMAL);").Tokenize()).Parse());

                // Complex Join and Aggregate across DBs
                string etl = @"
                    INSERT INTO pg.Summary
                    SELECT C.Name, SUM(O.Amount) as Total
                    FROM ms.Customers C
                    JOIN pg.Orders O ON C.ID = O.CustID
                    GROUP BY C.Name;";
                var tokens = new Lexer(etl).Tokenize();
                await eval.Evaluate(new Parser(tokens, etl).Parse());

                // Verify
                await eval.Evaluate(new Parser(new Lexer("SELECT * FROM pg.Summary WHERE Name = 'Alice';").Tokenize()).Parse());
                Assert.Equal(150.75m, Convert.ToDecimal(eval.LastResult?.Rows[0]["TOTAL"]));
            }
            finally
            {
                // No teardown needed for fixture
            }
        }

        [Fact]
        public async Task TestComplexJoinAndTransfer()
        {
            AnsiConsole.MarkupLine("  - Scenario: Large File -> Docker MS SQL (with Join)...");
            string csvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "salaries_large.csv");
            int empCount = 100;
            int weekdayCount = 0;
            
            try
            {
                // Generate CSV data: 3 years, M-F
                DateTime start = new DateTime(2023, 1, 1);
                DateTime end = new DateTime(2026, 1, 1);
                var rand = new Random(42);
                
                using (var writer = new StreamWriter(csvPath))
                {
                    writer.WriteLine("\"date\"\t\"employee_id\"\t\"amount\"");
                    for (DateTime d = start; d < end; d = d.AddDays(1))
                    {
                        if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday)
                        {
                            weekdayCount++;
                            for (int i = 1; i <= empCount; i++)
                            {
                                writer.WriteLine($"\"{d:yyyy-MM-dd}\"\t\"{i}\"\t\"{rand.Next(3000, 8000)}\"");
                            }
                        }
                    }
                }

                long expectedRows = (long)empCount * (long)weekdayCount;
                AnsiConsole.MarkupLine($"    Generated {expectedRows} rows of salary data.");

                var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                
                // Construct Employee Inserts
                var employeeValues = string.Join(", ", Enumerable.Range(1, empCount).Select(i => $"({i}, 'Employee {i}')"));
                
                string etl = $@"
                    USE DOCKER('mcr.microsoft.com/mssql/server:2022-latest');
                    DECLARE @conn varchar(500) = DOCKER.CONNECTION_STRING;
                    CREATE CONNECTION ds ON MSSQL(@conn);
                    
                    EXECUTE ds BEGIN IF OBJECT_ID('EmployeePay', 'U') IS NOT NULL DROP TABLE EmployeePay; END;
                    EXECUTE ds BEGIN IF OBJECT_ID('Employee', 'U') IS NOT NULL DROP TABLE Employee; END;
                    EXECUTE ds BEGIN CREATE TABLE [Employee] ([id] int, [employee_name] varchar(500)); END;
                    EXECUTE ds BEGIN CREATE TABLE [EmployeePay] ([emp_id] int, [pay_date] datetime, [amount] decimal(10,2)); END;
                    EXECUTE ds BEGIN INSERT INTO [Employee] ([id], [employee_name]) VALUES {employeeValues}; END;

                    CREATE CONNECTION csv ON FLATFILE('{csvPath.Replace("\\", "/")}') WITH(DELIMITER=TAB, TEXT_QUALIFIER=DOUBLEQUOTE);

                    DROP TABLE IF EXISTS #Sal;
                    SELECT * INTO #Sal FROM csv;

                    DROP TABLE IF EXISTS #Emp;
                    SELECT * INTO #Emp FROM ds.Employee;

                    DROP TABLE IF EXISTS #EmpPay;
                    SELECT
                        e.id AS emp_id
                       ,s.""date"" AS pay_date
                       ,s.amount
                    INTO #EmpPay
                    FROM #Emp AS e
                        JOIN #Sal AS s ON e.id = s.employee_id;

                    INSERT INTO ds.EmployeePay (emp_id, pay_date, amount)
                    SELECT emp_id, pay_date, amount FROM #EmpPay;

                    SELECT COUNT(*) AS Total FROM ds.EmployeePay;
                    
                    CLOSE_DOCKER;";

                AnsiConsole.MarkupLine("    Executing ETL script...");
                var tokens = new Lexer(etl).Tokenize();
                await eval.Evaluate(new Parser(tokens, etl).Parse());

                long actualRows = Convert.ToInt64(eval.LastResult?.Rows[0]["TOTAL"] ?? 0);
                Assert.Equal(expectedRows, actualRows);

                AnsiConsole.MarkupLine($"    Verified {actualRows} rows in Docker MS SQL.");
            }
            finally
            {
                if (File.Exists(csvPath)) File.Delete(csvPath);
            }
        }
        [Fact]
        public async Task TestMegaIntegration()
        {
            AnsiConsole.MarkupLine("  - Scenario: Mega Integration (MSSQL + CSV + Postgres + Aliases + Indexing)...");
            string csvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mega_salaries.csv");
            int empCount = 100;
            var rand = new Random(42);
            
            try
            {
                // 1. Generate Salaries CSV (3 years, M-F)
                DateTime start = new DateTime(2023, 1, 1);
                DateTime end = new DateTime(2026, 1, 1);
                var salaryRows = new List<(DateTime Date, int EmpId, int Amount)>();
                
                using (var writer = new StreamWriter(csvPath))
                {
                    writer.WriteLine("\"date\"\t\"employee_id\"\t\"amount\"");
                    for (DateTime d = start; d < end; d = d.AddDays(1))
                    {
                        if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday)
                        {
                            for (int i = 1; i <= empCount; i++)
                            {
                                int amount = rand.Next(3000, 8000);
                                writer.WriteLine($"\"{d:yyyy-MM-dd HH:mm:ss}\"\t\"{i}\"\t\"{amount}\"");
                                salaryRows.Add((d, i, amount));
                            }
                        }
                    }
                }

                // 2. Generate Deposits (Same rows, minus 10 random)
                var depositRows = salaryRows.OrderBy(x => rand.Next()).Skip(10).ToList();
                var depositSql = string.Join(", ", depositRows.Take(500).Select(d => $"({d.EmpId}, '{d.Date:yyyy-MM-dd HH:mm:ss}', 'DEP-{rand.Next(1000, 9999)}')"));
                
                var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                
                // Construct Employee Inserts (100)
                var employeeValues = string.Join(", ", Enumerable.Range(1, empCount).Select(i => $"({i}, 'Employee {i}')"));
                
                string etl = $@"
                    USE DOCKER('mcr.microsoft.com/mssql/server:2022-latest') AS dms;
                    DECLARE @conn varchar(500) = dms.CONNECTION_STRING;
                    CREATE CONNECTION ds ON MSSQL(@conn);
                    
                    EXECUTE ds
                    BEGIN
                      CREATE TABLE Employee (id int, employee_name varchar(500));
                      CREATE TABLE EmployeePay (emp_id int, date datetime, amount decimal(10,2));
                      INSERT INTO Employee(id, employee_name) VALUES {employeeValues}; 
                    END;

                    CREATE CONNECTION csv ON FLATFILE('{csvPath.Replace("\\", "/")}') WITH(DELIMITER=TAB, TEXT_QUALIFIER=DOUBLEQUOTE);

                    DROP TABLE IF EXISTS #Sal;
                    SELECT * INTO #Sal FROM csv;

                    DROP TABLE IF EXISTS #Emp;
                    SELECT * INTO #Emp FROM ds.Employee;

                    DROP TABLE IF EXISTS #EmpPay;
                    SELECT
                        e.id AS emp_id
                       ,s.date
                       ,s.amount
                    INTO #EmpPay
                    FROM #Emp AS e
                        JOIN #Sal AS s ON e.id = s.employee_id;

                    CREATE INDEX ix_emp_date ON #EmpPay(emp_id, date);

                    DECLARE @cnt int = 0;
                    SET @cnt = (SELECT COUNT(*) FROM #EmpPay);

                    -- Initial load into MSSQL
                    INSERT INTO ds.EmployeePay (emp_id, date, amount)
                    SELECT emp_id, date, amount FROM #EmpPay;

                    USE DOCKER('postgres:15-alpine') AS dpost;
                    DECLARE @post_conn varchar(500) = dpost.CONNECTION_STRING;
                    CREATE CONNECTION post ON POSTGRES(@post_conn);

                    EXECUTE post
                    BEGIN
                        CREATE TABLE EmployeeDeposits(id int, date timestamp, deposit_number varchar(500));
                        -- We will insert in chunks from C# to keep string size manageable
                    END;
                ";

                AnsiConsole.MarkupLine("    Starting containers and initial load...");
                await evaluator.Evaluate(new Parser(new Lexer(etl).Tokenize(), etl).Parse());

                // Insert deposits in chunks
                AnsiConsole.MarkupLine($"    Inserting {depositRows.Count} deposits into Postgres...");
                int chunkSize = 1000;
                for (int i = 0; i < depositRows.Count; i += chunkSize)
                {
                    var chunk = depositRows.GetRange(i, Math.Min(chunkSize, depositRows.Count - i));
                    // Escape single quotes for the EXECUTE string literal
                    var values = string.Join(", ", chunk.Select(d => $"({d.EmpId}, ''{d.Date:yyyy-MM-dd HH:mm:ss}'', ''DEP-{Guid.NewGuid().ToString().Substring(0,8)}'')"));
                    await evaluator.Evaluate(new Parser(new Lexer($"EXECUTE ('INSERT INTO EmployeeDeposits(id, date, deposit_number) VALUES {values}') AT post;").Tokenize()).Parse());
                }

                string etl2 = @"
                    DROP TABLE IF EXISTS #deposit;
                    SELECT * INTO #deposit FROM post.EmployeeDeposits;

                    -- We need to add the column first
                    EXECUTE ('ALTER TABLE EmployeePay ADD deposit_number varchar(500); TRUNCATE TABLE EmployeePay;') AT ds;

                    -- This join should use the index
                    DROP TABLE IF EXISTS #EmpPayFinal;
                    SELECT
                        e.emp_id
                       ,e.date
                       ,e.amount
                       ,d.deposit_number
                    INTO #EmpPayFinal
                    FROM #EmpPay e
                        JOIN #deposit d ON e.emp_id = d.id AND e.date = d.date;

                    -- Should be 10 less so incorrect should show
                    DECLARE @finalCnt int = (SELECT COUNT(*) FROM #EmpPayFinal);
                    IF @finalCnt = @cnt
                       PRINT('GOOD');
                    ELSE
                       PRINT('INCORRECT');

                    INSERT INTO ds.EmployeePay (emp_id, date, amount, deposit_number)
                    SELECT emp_id, date, amount, deposit_number FROM #EmpPayFinal;

                    CREATE_DIRECTORY('/Output');
                    CREATE CONNECTION outFile FLATFILE('/Output/descrepency.csv') WITH(HEADER=ON, DELIMITER=COMMA);

                    -- Explain the discrepancy analysis
                    EXPLAIN
                    SELECT
                        e.emp_id
                       ,e.date
                       ,e.amount
                    FROM #EmpPay e
                        LEFT JOIN #deposit d ON e.emp_id = d.id AND e.date = d.date
                    WHERE d.id IS NULL;

                    -- Find discrepancy (10 rows)
                    INSERT INTO outFile
                    SELECT
                        e.emp_id
                       ,e.date
                       ,e.amount
                    FROM #EmpPay e
                        LEFT JOIN #deposit d ON e.emp_id = d.id AND e.date = d.date
                    WHERE d.id IS NULL;

                    DECLARE @output int = (SELECT COUNT(*) FROM outFile);
                    IF @output > 0
                       PRINT(CONCAT(@output, ' Missing'));

                    -- Verify discrepancy count in C# side
                    -- (We'll do that by checking the last result or variable if needed)

                    DELETE_FILE(outFile);
                    DELETE_DIRECTORY('/Output');

                    CLOSE_DOCKER dms;
                    CLOSE_DOCKER dpost;
                ";

                AnsiConsole.MarkupLine("    Running discrepancy analysis and cleanup...");
                await evaluator.Evaluate(new Parser(new Lexer(etl2).Tokenize(), etl2).Parse());

                // Final Verification
                var outputVar = evaluator.GetVariable("@output");
                Assert.Equal(10, Convert.ToInt32(outputVar));

                AnsiConsole.MarkupLine("    [green]Success![/] Correctly identified 10 missing deposits.");
            }
            finally
            {
                if (File.Exists(csvPath)) File.Delete(csvPath);
            }
        }
    }
}
