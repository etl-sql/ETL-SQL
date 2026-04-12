using System;
using System.IO;
using System.Linq;
using Xunit;
using ETL_SQL;

namespace ETL_SQL.Tests.Engine
{
    /// <summary>
    /// CQ-T5: Smoke tests for ETL_SQL.DataGenerator.
    /// Verifies that Generate() produces files with the expected schema.
    /// </summary>
    public class DataGeneratorTests : IDisposable
    {
        private readonly string _testDataDir;

        public DataGeneratorTests()
        {
            // Use a temp directory so tests don't pollute the real TestData folder
            _testDataDir = Path.Combine(Path.GetTempPath(), "ETL-SQL-DataGen-Tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDataDir);

            // Redirect the generator to write here by patching working directory
            _originalDir = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(_testDataDir);
            Directory.CreateDirectory(Path.Combine(_testDataDir, "TestData"));
        }

        private readonly string _originalDir;

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalDir);
            if (Directory.Exists(_testDataDir))
                Directory.Delete(_testDataDir, recursive: true);
        }

        [Fact]
        public void Generate_CreatesExpectedFiles()
        {
            DataGenerator.Generate(100); // Small count for fast tests

            Assert.True(File.Exists(Path.Combine(_testDataDir, "TestData", "test_stress_BigTable.csv")),
                "BigTable.csv should be created");
            Assert.True(File.Exists(Path.Combine(_testDataDir, "TestData", "test_stress_SmallTable.csv")),
                "SmallTable.csv should be created");
        }

        [Fact]
        public void Generate_BigTable_HasExpectedSchema()
        {
            DataGenerator.Generate(50);

            var path = Path.Combine(_testDataDir, "TestData", "test_stress_BigTable.csv");
            var lines = File.ReadAllLines(path);

            // Header + 50 data rows
            Assert.True(lines.Length >= 51, $"Expected at least 51 lines, got {lines.Length}");
            Assert.Equal("ID,Value,Data", lines[0]);
        }

        [Fact]
        public void Generate_BigTable_HasCorrectRowCount()
        {
            DataGenerator.Generate(10);

            var path = Path.Combine(_testDataDir, "TestData", "test_stress_BigTable.csv");
            var dataLines = File.ReadAllLines(path).Skip(1).ToList(); // skip header

            Assert.Equal(10, dataLines.Count);
        }

        [Fact]
        public void Generate_BigTable_RowsHaveThreeColumns()
        {
            DataGenerator.Generate(20);

            var path = Path.Combine(_testDataDir, "TestData", "test_stress_BigTable.csv");
            var lines = File.ReadAllLines(path).Skip(1); // skip header

            foreach (var line in lines)
            {
                var parts = line.Split(',');
                Assert.Equal(3, parts.Length);
                // ID column should be a number
                Assert.True(int.TryParse(parts[0], out _), $"ID column '{parts[0]}' is not an integer");
                // Value column should start with "Val_"
                Assert.StartsWith("Val_", parts[1]);
                // Data column should start with "RandomData_"
                Assert.StartsWith("RandomData_", parts[2]);
            }
        }

        [Fact]
        public void Generate_SmallTable_HasExpectedSchema()
        {
            DataGenerator.Generate(5);

            var path = Path.Combine(_testDataDir, "TestData", "test_stress_SmallTable.csv");
            var lines = File.ReadAllLines(path);

            Assert.True(lines.Length >= 2, "SmallTable should have header + data rows");
            Assert.Equal("ID,Name", lines[0]);
        }

        [Fact]
        public void Generate_SmallTable_HasExactly1000Rows()
        {
            DataGenerator.Generate(5);

            var path = Path.Combine(_testDataDir, "TestData", "test_stress_SmallTable.csv");
            var dataLines = File.ReadAllLines(path).Skip(1).ToList();

            Assert.Equal(1000, dataLines.Count);
        }

        [Fact]
        public void Generate_SmallTable_IdsAreSequential()
        {
            DataGenerator.Generate(5);

            var path = Path.Combine(_testDataDir, "TestData", "test_stress_SmallTable.csv");
            var lines = File.ReadAllLines(path).Skip(1).ToList();

            for (int i = 0; i < lines.Count; i++)
            {
                var parts = lines[i].Split(',');
                Assert.Equal(i + 1, int.Parse(parts[0]));
                Assert.Equal($"User_{i + 1}", parts[1]);
            }
        }
    }
}
