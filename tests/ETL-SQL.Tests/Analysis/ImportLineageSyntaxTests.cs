using System.Linq;
using ETL_SQL.Core;
using Xunit;

namespace ETL_SQL.Tests.Analysis
{
    public class ImportLineageSyntaxTests
    {
        private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize()).Parse();

        [Fact]
        public void ImportLineageParsesTheSymmetricExportSpelling()
        {
            var script = Parse(
                "IMPORT LINEAGE FOR hospital.dbo.Patient AS OPENLINEAGE FROM 'C:\\tmp\\p.openlineage.jsonl';");

            Assert.Empty(script.Diagnostics);
            Assert.IsType<CreateLineageStatement>(script.Statements.Single());
        }

        [Fact]
        public void ImportLineageAcceptsTheTerseForm()
        {
            var script = Parse("IMPORT LINEAGE FOR hospital.dbo.Patient FROM 'lineage.jsonl';");

            Assert.Empty(script.Diagnostics);
            Assert.IsType<CreateLineageStatement>(script.Statements.Single());
        }

        [Fact]
        public void InsertLineageRemainsSupported()
        {
            var script = Parse("INSERT LINEAGE FOR TABLE hospital.dbo.Patient FROM 'lineage.jsonl';");

            Assert.Empty(script.Diagnostics);
            Assert.IsType<CreateLineageStatement>(script.Statements.Single());
        }

        /// <summary>
        /// "import:" is a natural name for a load step and appears in real scripts. Promoting IMPORT
        /// to a keyword must not take that name away.
        /// </summary>
        [Fact]
        public void ImportRemainsUsableAsAStatementLabel()
        {
            var script = Parse(@"
CREATE TABLE #t (id INT);
import:
INSERT INTO #t VALUES (1);
");
            Assert.Empty(script.Diagnostics);
        }

        [Fact]
        public void ImportRemainsUsableAsAnIdentifier()
        {
            var script = Parse("CREATE TABLE #t (import INT); SELECT import FROM #t;");
            Assert.Empty(script.Diagnostics);
        }

        [Fact]
        public void ExportLineageAcceptsMarkdown()
        {
            var script = Parse("EXPORT LINEAGE FOR #orders AS MARKDOWN TO 'lineage.md';");

            Assert.Empty(script.Diagnostics);
            var stmt = Assert.IsType<LineageStatement>(script.Statements.Single());
            Assert.False(stmt.ExportAsOpenLineage);
            Assert.Equal("lineage.md", stmt.ExportPath);
        }

        [Fact]
        public void ExportLineageStillAcceptsOpenLineage()
        {
            var script = Parse("EXPORT LINEAGE FOR #orders AS OPENLINEAGE TO 'lineage.jsonl';");

            Assert.Empty(script.Diagnostics);
            var stmt = Assert.IsType<LineageStatement>(script.Statements.Single());
            Assert.True(stmt.ExportAsOpenLineage);
        }

        [Fact]
        public void ExportLineageRejectsAnUnknownFormatWithABrowsableMessage()
        {
            var script = Parse("EXPORT LINEAGE FOR #orders AS PARQUET TO 'lineage.parquet';");

            var diagnostic = Assert.Single(script.Diagnostics);
            Assert.Contains("OPENLINEAGE", diagnostic.Message);
            Assert.Contains("MARKDOWN", diagnostic.Message);
        }
    }
}
