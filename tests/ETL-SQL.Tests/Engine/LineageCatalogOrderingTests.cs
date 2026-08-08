using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Lineage;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine.Storage;
using Xunit;

namespace ETL_SQL.Tests.Engine
{
    /// <summary>
    /// eng.lineage should read as a walkable chain — origin first, destination last — with physical
    /// identifiers on every hop. Recording order does not do this, because static analysis and
    /// execution observe the same flow at different moments.
    /// </summary>
    public class LineageCatalogOrderingTests
    {
        private const string Script = @"
CREATE CONNECTION hospital AS MSSQL('Server=localhost;Database=EDW;');
CREATE CONNECTION pats AS FLATFILE(PATH='C:\tmp\patients.csv');

INSERT INTO hospital.dbo.Patient (name, date_of_birth)
SELECT name, CAST(date_of_birth AS date) AS date_of_birth FROM pats.FILE;

SELECT patient_id, name, date_of_birth FROM hospital.dbo.Patient;
";

        private static async Task<List<Row>> ReadCatalogAsync()
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            new LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(Script).Tokenize()).Parse());

            var rows = new List<Row>();
            await foreach (var batch in new LineageDataSource(tracker).ReadBatches())
                rows.AddRange(batch.Rows);
            return rows;
        }

        [Fact]
        public async Task StepsAreOrderedOriginFirst()
        {
            var rows = await ReadCatalogAsync();
            var steps = rows.Select(r => System.Convert.ToInt32(r["step"])).ToList();

            Assert.Equal(steps.OrderBy(s => s), steps);
        }

        [Fact]
        public async Task LoadIntoTheTableIsAnEarlierStepThanTheReadBackFromIt()
        {
            var rows = await ReadCatalogAsync();

            var load = rows.Single(r =>
                r["target_table"]?.ToString() == "hospital.dbo.Patient" &&
                r["target_column"]?.ToString() == "date_of_birth");
            var readBack = rows.Single(r =>
                r["target_table"]?.ToString() == "RESULTSET" &&
                r["target_column"]?.ToString() == "date_of_birth");

            Assert.True(System.Convert.ToInt32(load["step"]) < System.Convert.ToInt32(readBack["step"]),
                "the write into Patient must come before the read out of it");
        }

        [Fact]
        public async Task PhysicalIdentifiersAreProjectedForBothEnds()
        {
            var rows = await ReadCatalogAsync();

            var load = rows.Single(r =>
                r["target_table"]?.ToString() == "hospital.dbo.Patient" &&
                r["target_column"]?.ToString() == "date_of_birth");

            Assert.Equal("localhost:EDW.dbo.Patient", load["target_physical"]?.ToString());
            Assert.Equal(@"FLATFILE C:\tmp\patients.csv", load["source_physical"]?.ToString());
        }

        /// <summary>
        /// An INSERT ... SELECT is one movement. It previously produced both a SELECT row and an
        /// INSERT row for the same column, which read as two steps that never happened.
        /// </summary>
        [Fact]
        public async Task InsertSelectProducesOneRowPerColumnNotTwo()
        {
            var rows = await ReadCatalogAsync();

            var forColumn = rows.Where(r =>
                r["target_table"]?.ToString() == "hospital.dbo.Patient" &&
                r["target_column"]?.ToString() == "date_of_birth").ToList();

            var single = Assert.Single(forColumn);
            Assert.Equal("INSERT", single["operation"]?.ToString());
            Assert.Equal("Cast", single["transformation_kind"]?.ToString());
        }

        /// <summary>The transformation rides on the write that performs it, so the CAST stays visible.</summary>
        [Fact]
        public async Task TransformationIsCarriedOnTheWriteThatAppliedIt()
        {
            var rows = await ReadCatalogAsync();

            var load = rows.Single(r =>
                r["target_table"]?.ToString() == "hospital.dbo.Patient" &&
                r["target_column"]?.ToString() == "date_of_birth");

            Assert.Contains("CAST", load["transformation_expression"]?.ToString() ?? "");
        }

        [Fact]
        public async Task CatalogColumnsAreAllSnakeCase()
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            new LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(Script).Tokenize()).Parse());

            var columns = await new LineageDataSource(tracker).GetColumnsAsync();
            Assert.All(columns, c => Assert.Equal(c.ToLowerInvariant(), c));
        }

        /// <summary>
        /// EngineCatalog drives editor autocomplete, and nothing forces it to agree with the data
        /// source that actually produces the rows — so it drifts silently and the IDE starts
        /// offering columns that do not exist.
        /// </summary>
        [Fact]
        public async Task DeclaredCatalogSchemaMatchesTheRowsActuallyProduced()
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            new LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(Script).Tokenize()).Parse());

            var actual = (await new LineageDataSource(tracker).GetColumnsAsync()).ToList();
            var declared = EngineCatalog.TableColumns["lineage"].Select(c => c.Name).ToList();

            Assert.Equal(actual, declared);
        }

        /// <summary>Every eng.* table the catalog declares uses snake_case, per the project decision.</summary>
        [Fact]
        public void EveryDeclaredEngineCatalogColumnIsSnakeCase()
        {
            foreach (var (table, columns) in EngineCatalog.TableColumns)
            {
                foreach (var column in columns)
                {
                    Assert.True(column.Name == column.Name.ToLowerInvariant(),
                        $"eng.{table}.{column.Name} is not snake_case.");
                }
            }
        }
    }
}
