using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.App;
using ETL_SQL.Core.Parser;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Tests.Integration
{
    /// <summary>
    /// Integration tests against the standard test data files in TestData/.
    /// Validates that JSON, XML, XLSX, and large CSV files can be read correctly.
    /// </summary>
    public class TestDataFilesTests
    {
        private static readonly string TestDataPath =
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "TestData"));

        public TestDataFilesTests()
        {
            TestDataGenerator.EnsureTestDataFiles();
        }

        private string DataFile(string name) => Path.Combine(TestDataPath, name).Replace("\\", "/");

        [Fact]
        public async Task CanReadJsonEmployees()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var path = DataFile("test_employees.json");
            Assert.True(File.Exists(path), $"Missing test data: {path}");

            await ev.Evaluate(TestHelpers.Parse($@"
                CREATE CONNECTION emp_json ON JSON('{path}');
                SELECT id, name, department, salary FROM emp_json;
            "));

            var result = ev.LastResult;
            Assert.NotNull(result);
            Assert.Equal(10, result.Rows.Count);
            Assert.Equal("Alice", result.Rows[0]["name"]?.ToString());
        }

        [Fact]
        public async Task CanReadXmlProducts()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var path = DataFile("test_products.xml");
            Assert.True(File.Exists(path), $"Missing test data: {path}");

            await ev.Evaluate(TestHelpers.Parse($@"
                CREATE CONNECTION prod_xml ON XML('{path}') WITH (ROOT = 'product');
                SELECT id, name, category, price FROM prod_xml;
            "));

            var result = ev.LastResult;
            Assert.NotNull(result);
            Assert.True(result.Rows.Count >= 1, "Expected at least one product row");
        }

        [Fact]
        public async Task CanReadExcelEmployees()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var path = DataFile("test_employees.xlsx");
            Assert.True(File.Exists(path), $"Missing test data: {path}");

            await ev.Evaluate(TestHelpers.Parse($@"
                CREATE CONNECTION emp_xlsx ON EXCEL('{path}');
                SELECT ID, Name, Department, Salary FROM emp_xlsx;
            "));

            var result = ev.LastResult;
            Assert.NotNull(result);
            Assert.Equal(5, result.Rows.Count);
            Assert.Equal("Alice", result.Rows[0]["Name"]?.ToString());
        }

        [Fact]
        public async Task CanReadLargeEmployeesCsv()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var path = DataFile("test_large_employees.csv");
            Assert.True(File.Exists(path), $"Missing test data: {path}");

            await ev.Evaluate(TestHelpers.Parse($@"
                CREATE CONNECTION large_emp ON FLATFILE('{path}') WITH (HEADER = ON);
                SELECT COUNT(*) AS Total FROM large_emp;
            "));

            var result = ev.LastResult;
            Assert.NotNull(result);
            Assert.Equal(10000m, Convert.ToDecimal(result.Rows[0]["Total"]));
        }

        [Fact]
        public async Task JsonEmployees_FilterByDepartment()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var path = DataFile("test_employees.json");

            await ev.Evaluate(TestHelpers.Parse($@"
                CREATE CONNECTION emp_json2 ON JSON('{path}');
                SELECT name, salary FROM emp_json2 WHERE department = 'Engineering';
            "));

            var result = ev.LastResult;
            Assert.NotNull(result);
            Assert.Equal(3, result.Rows.Count); // Alice, Charlie, Grace
        }
    }
}
