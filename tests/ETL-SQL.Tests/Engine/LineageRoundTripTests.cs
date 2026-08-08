using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Lineage;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Engine.Lineage;
using Xunit;

namespace ETL_SQL.Tests.Engine
{
    /// <summary>
    /// The point of exporting lineage is being able to pick it back up in a later script: load a
    /// table today, read from it next week, and still be able to trace a column to the CSV it came
    /// from. That requires the export to survive a round trip through OpenLineage and re-attach to
    /// whatever the second script happens to call the connection.
    /// </summary>
    public class LineageRoundTripTests
    {
        private const string ProducerScript = @"
CREATE CONNECTION hospital AS MSSQL('Server=localhost;Database=EDW;');
CREATE CONNECTION pats AS FLATFILE(PATH='C:\tmp\patients.csv');
INSERT INTO hospital.dbo.Patient (name, date_of_birth)
SELECT name, CAST(date_of_birth AS date) AS date_of_birth FROM pats.FILE;
";

        private static LineageTracker Analyze(string script)
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            new LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(script).Tokenize()).Parse());
            return tracker;
        }

        private static string ExportJson()
        {
            var tracker = Analyze(ProducerScript);
            // Namespaces as the exporting script's connections resolve them.
            var namespaces = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["hospital"] = "mssql://localhost/EDW",
                ["pats"] = "file://"
            };
            return OpenLineageExporter.BuildRunEvent(tracker, "sess-1", "load.etlsql", "etl-sql", namespaces);
        }

        [Fact]
        public void ExportedLineageReImportsWithColumnEdgesIntact()
        {
            var entries = OpenLineageImporter.Import(ExportJson());

            Assert.NotEmpty(entries);
            Assert.Contains(entries, e => e.TargetColumn == "date_of_birth");
        }

        /// <summary>
        /// Export strips the connection alias, because an alias is script-local. Import must put the
        /// importing script's own alias back, or the imported rows never chain to anything.
        /// </summary>
        [Fact]
        public void ImportReQualifiesDatasetsWithTheImportingScriptsAlias()
        {
            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mssql://localhost/EDW"] = "warehouse"   // a different name than the producer used
            };

            var entries = OpenLineageImporter.Import(ExportJson(), aliases);

            Assert.Contains(entries, e =>
                e.TargetTable.Equals("warehouse.dbo.Patient", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>A hop that loses its CAST is not worth re-importing.</summary>
        [Fact]
        public void TransformationDetailSurvivesTheRoundTrip()
        {
            var entries = OpenLineageImporter.Import(ExportJson());

            var dob = entries.First(e => e.TargetColumn == "date_of_birth");
            Assert.Equal(TransformationKind.Cast, dob.TransformationKind);
            Assert.Contains("CAST", dob.TransformationExpression ?? "");
        }

        /// <summary>
        /// LoadState previously dropped transformation detail on the floor, so lineage read back out
        /// of the tracker after an import was flatter than what was imported.
        /// </summary>
        [Fact]
        public void LoadStatePreservesTransformationDetail()
        {
            var entries = OpenLineageImporter.Import(ExportJson());

            var tracker = new LineageTracker(NullLogger.Instance);
            tracker.LoadState(entries);

            var dob = tracker.GetFullLineage().First(e => e.TargetColumn == "date_of_birth");
            Assert.Equal(TransformationKind.Cast, dob.TransformationKind);
            Assert.Contains("CAST", dob.TransformationExpression ?? "");
        }

        /// <summary>
        /// The whole workflow: import yesterday's lineage, then write the table out to a new file.
        /// Hovering the exported column should reach all the way back to the original CSV.
        /// </summary>
        [Fact]
        public void ImportedLineageChainsIntoTheConsumingScript()
        {
            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mssql://localhost/EDW"] = "hospital"
            };
            var imported = OpenLineageImporter.Import(ExportJson(), aliases);

            const string consumer = @"
CREATE CONNECTION hospital AS MSSQL('Server=localhost;Database=EDW;');
CREATE CONNECTION outfile AS FLATFILE(PATH='C:\tmp\output.csv');
INSERT INTO outfile.FILE (name, date_of_birth)
SELECT name, date_of_birth FROM hospital.dbo.Patient;
";
            var tracker = new LineageTracker(NullLogger.Instance);
            tracker.LoadState(imported);
            new LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(consumer).Tokenize()).Parse());

            var graph = new LineageGraphRenderer().Render(tracker, "outfile.FILE", "date_of_birth");

            Assert.Contains("output.csv", graph);               // where it landed
            Assert.Contains("EDW.dbo.Patient", graph);          // the warehouse hop, from the import
            Assert.Contains("patients.csv", graph);             // the original source, two scripts back
        }
    }
}
