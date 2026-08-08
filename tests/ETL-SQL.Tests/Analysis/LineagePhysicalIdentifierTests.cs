using System;
using System.Linq;
using ETL_SQL.Analysis.Lineage;
using ETL_SQL.Common;
using ETL_SQL.Core;
using Xunit;

namespace ETL_SQL.Tests.Analysis
{
    /// <summary>
    /// Lineage must stay meaningful once the reader leaves the script that produced it. A hover that
    /// says "pats.FILE" only helps someone reading that script; "FLATFILE C:\tmp\patients.csv" helps
    /// anyone. These cover the IDE hover path, which analyses text statically and never opens a
    /// connection — so the descriptors have to come from the script's own CREATE CONNECTION.
    /// </summary>
    public class LineagePhysicalIdentifierTests
    {
        private const string HospitalScript = @"
CREATE CONNECTION hospital AS MSSQL('Server=localhost;Database=EDW;Trusted_Connection=True;');
CREATE CONNECTION pats AS FLATFILE(PATH=""C:\tmp\patients.csv"", DELIMITER=',', HEADER=TRUE);

INSERT INTO hospital.dbo.Patient (name, date_of_birth, date_of_death, gender)
SELECT
    name
    ,CAST(date_of_birth AS date) AS date_of_birth
    ,CAST(date_of_death AS date)
    ,gender
FROM pats.FILE;

SELECT patient_id, name, date_of_birth, date_of_death, gender
FROM hospital.dbo.Patient;
";

        private static LineageTracker Analyze(string script)
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            new LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(script).Tokenize()).Parse());
            return tracker;
        }

        [Fact]
        public void InsertTargetRendersServerQualifiedPhysicalName()
        {
            var graph = new LineageGraphRenderer()
                .Render(Analyze(HospitalScript), "hospital.dbo.Patient", "date_of_birth");

            Assert.Contains("localhost:EDW.dbo.Patient", graph);
            Assert.DoesNotContain("[Table: hospital.dbo.Patient]", graph);
        }

        [Fact]
        public void FlatFileSourceRendersConnectorAndPath()
        {
            var graph = new LineageGraphRenderer()
                .Render(Analyze(HospitalScript), "hospital.dbo.Patient", "date_of_birth");

            Assert.Contains(@"FLATFILE C:\tmp\patients.csv", graph);
            Assert.DoesNotContain("pats.FILE", graph);
        }

        [Fact]
        public void NoSaveConnectionOmitsTheServer()
        {
            var graph = new LineageGraphRenderer()
                .Render(Analyze("SET NO_SAVE_CONNECTION = ON;\n" + HospitalScript),
                        "hospital.dbo.Patient", "date_of_birth");

            Assert.Contains("EDW.dbo.Patient", graph);
            Assert.DoesNotContain("localhost", graph);
        }

        /// <summary>
        /// The bug the user hit: hovering a column in a SELECT that reads the table an earlier
        /// INSERT populated showed only that one hop, losing the CSV origin and the CAST.
        /// </summary>
        [Fact]
        public void DownstreamSelectKeepsTheFullChainBackToTheOriginalSource()
        {
            var graph = new LineageGraphRenderer()
                .Render(Analyze(HospitalScript), "RESULTSET", "date_of_birth");

            Assert.Contains("localhost:EDW.dbo.Patient", graph);
            Assert.Contains("Cast", graph);
            Assert.Contains(@"FLATFILE C:\tmp\patients.csv", graph);
        }

        /// <summary>Column tags applied at the INSERT must still be reachable downstream.</summary>
        [Fact]
        public void ColumnTagsPropagateThroughTheInsertToTheDownstreamSelect()
        {
            const string tagged = @"
CREATE CONNECTION hospital AS MSSQL('Server=localhost;Database=EDW;');
CREATE CONNECTION pats AS FLATFILE(PATH='C:\tmp\patients.csv');
INSERT INTO hospital.dbo.Patient (name, date_of_birth)
SELECT
     name /* @d: patient name formatted as last name, first name; @pii: true; */
    ,CAST(date_of_birth AS date) AS date_of_birth
FROM pats.FILE;
SELECT name, date_of_birth FROM hospital.dbo.Patient;
";
            var entry = Analyze(tagged)
                .GetColumnLineage("RESULTSET", "name")
                .First();

            Assert.Equal("true", entry.Metadata["pii"]);
            Assert.Contains("last name, first name", entry.Metadata["d"]);
        }

        /// <summary>
        /// An encrypted connection string must not be guessed at or partially disclosed — lineage
        /// falls back to the logical alias rather than inventing a location.
        /// </summary>
        [Fact]
        public void EncryptedConnectionStringIsLeftUnresolved()
        {
            const string encrypted = @"
CREATE CONNECTION hospital AS MSSQL('ENC:ArFsBrabRZQUUiaVAw6a1XNHcXrNolQfWGGxr3kACamZ5c8Q');
CREATE CONNECTION pats AS FLATFILE(PATH='C:\tmp\patients.csv');
INSERT INTO hospital.dbo.Patient (name) SELECT name FROM pats.FILE;
";
            var graph = new LineageGraphRenderer()
                .Render(Analyze(encrypted), "hospital.dbo.Patient", "name");

            Assert.Contains("hospital.dbo.Patient", graph);
            Assert.DoesNotContain("ENC:", graph);
        }

        /// <summary>
        /// Physical descriptors are for display only. The tracker stays keyed on the logical name so
        /// lineage still resolves after export/import, when no connection map exists.
        /// </summary>
        [Fact]
        public void LookupKeysRemainLogicalNotPhysical()
        {
            var tracker = Analyze(HospitalScript);

            Assert.NotEmpty(tracker.GetColumnLineage("hospital.dbo.Patient", "date_of_birth"));
            Assert.Empty(tracker.GetColumnLineage("localhost:EDW.dbo.Patient", "date_of_birth"));

            var entry = tracker.GetColumnLineage("hospital.dbo.Patient", "date_of_birth").First();
            Assert.Equal("hospital.dbo.Patient", entry.TargetTable);
            Assert.Equal("localhost:EDW.dbo.Patient", entry.TargetTablePhysical);
        }

        /// <summary>A file path already contains dots, so a trailing ".column" reads as a path segment.</summary>
        [Fact]
        public void FileSourceColumnIsBracketedNotDotted()
        {
            var graph = new LineageGraphRenderer()
                .Render(Analyze(HospitalScript), "hospital.dbo.Patient", "date_of_birth");

            Assert.Contains(@"FLATFILE C:\tmp\patients.csv [date_of_birth]", graph);
            Assert.DoesNotContain(@"patients.csv.date_of_birth", graph);
        }
    }
}
