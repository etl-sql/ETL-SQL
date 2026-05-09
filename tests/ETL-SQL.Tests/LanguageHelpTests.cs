using ETL_SQL.Analysis.Documentation;
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
            var verifier = new HelpDocumentationVerifier(registry);
            
            // Testing a subset of core keywords to verify the resource loading works
            string[] coreKeywords = { "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE", "DECLARE", "SET", "IF", "WHILE", "FOR", "FOREACH" };
            
            foreach (var check in verifier.VerifyRequiredTopics(coreKeywords))
            {
                Assert.True(check.Found, check.Message);
            }
        }

        [Fact]
        public void Verify_MajorConnectors_HaveHelpDocumentation()
        {
            var registry = new LanguageHelpRegistry();
            var verifier = new HelpDocumentationVerifier(registry);
            string[] connectors = { "MSSQL", "POSTGRES", "FLATFILE", "API" };
            
            foreach (var check in verifier.VerifyRequiredSubTopics("CONNECTION", connectors))
            {
                Assert.True(check.Found, check.Message);
            }
        }

        [Fact]
        public void Verify_SystemVariables_HaveHelpDocumentation()
        {
            var registry = new LanguageHelpRegistry();
            var verifier = new HelpDocumentationVerifier(registry);
            string[] sysVars = { "@@ROWCOUNT", "@@ERROR", "@@VERSION" };
            
            foreach (var check in verifier.VerifyRequiredSubTopics("VARIABLES", sysVars))
            {
                Assert.True(check.Found, check.Message);
            }
        }

        [Fact]
        public void Verify_ReportComponents_HaveHelpDocumentation()
        {
            var registry = new LanguageHelpRegistry();
            var verifier = new HelpDocumentationVerifier(registry);
            string[] components = { "DATASET", "PAGE", "STYLE", "VISUAL" };
            
            foreach (var check in verifier.VerifyRequiredSubTopics("REPORT", components))
            {
                Assert.True(check.Found, check.Message);
            }
        }
    }
}
