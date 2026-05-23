using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using Xunit;

namespace ETL_SQL.Tests.Core.Parsing
{
    public class ViewParserTests
    {
        private static Script Parse(string sql)
        {
            var tokens = new Lexer(sql).Tokenize();
            return new Parser(tokens, sql).Parse();
        }

        [Fact]
        public void CreateView_ParsesSelectQuery()
        {
            var script = Parse("CREATE VIEW ActiveCustomers AS SELECT id, name FROM #customers WHERE active = 1;");
            var stmt = Assert.IsType<CreateViewStatement>(Assert.Single(script.Statements));

            Assert.Equal("ActiveCustomers", stmt.ViewName);
            Assert.Equal(ObjectCreationMode.Create, stmt.Mode);
            Assert.IsType<SelectStatement>(stmt.Query);
        }

        [Fact]
        public void AlterAndCreateOrAlterView_ParseModes()
        {
            var script = Parse(@"
ALTER VIEW ActiveCustomers AS SELECT id FROM #customers;
CREATE OR ALTER VIEW ActiveCustomers AS SELECT id, name FROM #customers;
");

            var alter = Assert.IsType<CreateViewStatement>(script.Statements[0]);
            var createOrAlter = Assert.IsType<CreateViewStatement>(script.Statements[1]);

            Assert.Equal(ObjectCreationMode.Alter, alter.Mode);
            Assert.Equal(ObjectCreationMode.CreateOrAlter, createOrAlter.Mode);
        }

        [Fact]
        public void DropViewAndShowViews_Parse()
        {
            var script = Parse("DROP VIEW IF EXISTS ActiveCustomers; SHOW VIEWS INTO #views;");

            var drop = Assert.IsType<DropViewStatement>(script.Statements[0]);
            var show = Assert.IsType<ShowViewsStatement>(script.Statements[1]);

            Assert.True(drop.IfExists);
            Assert.Equal("ActiveCustomers", drop.ViewName);
            Assert.Equal("#views", show.IntoTable);
        }
    }
}
