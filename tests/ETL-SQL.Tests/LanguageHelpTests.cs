using ETL_SQL.Core.Metadata;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Common;
using Xunit;
using System.Linq;
using System;

namespace ETL_SQL.Tests
{
    public class LanguageHelpTests
    {
        [Fact]
        public void Verify_CoreKeywords_HaveHelpDocumentation()
        {
            var registry = new LanguageHelpRegistry();
            
            // Testing a subset of core keywords to verify the resource loading works
            string[] coreKeywords = { "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE", "DECLARE", "SET", "IF", "WHILE", "FOR", "FOREACH" };
            
            foreach (var kw in coreKeywords)
            {
                var help = registry.GetHelp(kw);
                Assert.NotNull(help);
                Assert.True(help.Length > 10, $"Documentation for {kw} is too short.");
            }
        }

        [Fact]
        public void Verify_MajorConnectors_HaveHelpDocumentation()
        {
            var registry = new LanguageHelpRegistry();
            string[] connectors = { "MSSQL", "POSTGRES", "FLATFILE", "API" };
            
            foreach (var conn in connectors)
            {
                var help = registry.GetHelp("CONNECTION", conn);
                Assert.NotNull(help);
            }
        }

        [Fact]
        public void Verify_SystemVariables_HaveHelpDocumentation()
        {
            var registry = new LanguageHelpRegistry();
            string[] sysVars = { "@@ROWCOUNT", "@@ERROR", "@@VERSION" };
            
            foreach (var v in sysVars)
            {
                var help = registry.GetHelp("VARIABLES", v);
                Assert.NotNull(help);
            }
        }

        [Fact]
        public void Verify_ReportComponents_HaveHelpDocumentation()
        {
            var registry = new LanguageHelpRegistry();
            string[] components = { "DATASET", "PAGE", "STYLE", "VISUAL" };
            
            foreach (var comp in components)
            {
                var help = registry.GetHelp("REPORT", comp);
                Assert.NotNull(help);
            }
        }
    }
}
