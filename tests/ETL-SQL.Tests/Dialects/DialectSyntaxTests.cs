using System;
using System.Linq;
using ETL_SQL.Connectors.MockDb;
using ETL_SQL.Connectors.Oracle;
using Xunit;

namespace ETL_SQL.Tests.Dialects.Dialects
{
    /// <summary>
    /// CQ-T4: Unit tests for MockDbSyntax and OracleSyntax dialect definitions.
    /// Verifies that expected keywords, functions, and exclusions are correctly declared.
    /// </summary>
    public class DialectSyntaxTests
    {
        // ── MockDbSyntax ───────────────────────────────────────────────────────

        [Fact]
        public void MockDb_Functions_ContainsExpectedEntries()
        {
            Assert.Contains("GETDATE", MockDbSyntax.Functions);
            Assert.Contains("LEN", MockDbSyntax.Functions);
            Assert.Contains("ISNULL", MockDbSyntax.Functions);
            Assert.Contains("CONVERT", MockDbSyntax.Functions);
            Assert.Contains("CHARINDEX", MockDbSyntax.Functions);
            Assert.Contains("DATEADD", MockDbSyntax.Functions);
        }

        [Fact]
        public void MockDb_Additions_ContainsTSqlKeywords()
        {
            Assert.Contains("TOP", MockDbSyntax.Additions);
            Assert.Contains("NOLOCK", MockDbSyntax.Additions);
            Assert.Contains("OUTPUT", MockDbSyntax.Additions);
            Assert.Contains("INSERTED", MockDbSyntax.Additions);
            Assert.Contains("DELETED", MockDbSyntax.Additions);
        }

        [Fact]
        public void MockDb_Exclusions_ContainsLimit()
        {
            // MockDB uses TOP instead of LIMIT
            Assert.Contains("LIMIT", MockDbSyntax.Exclusions);
        }

        [Fact]
        public void MockDb_GetSupportedKeywords_ReturnsSameAsAdditions()
        {
            var keywords = MockDbSyntax.GetSupportedKeywords();
            Assert.Equal(MockDbSyntax.Additions, keywords);
        }

        [Fact]
        public void MockDb_GetSupportedFunctions_ReturnsSameAsFunctions()
        {
            var functions = MockDbSyntax.GetSupportedFunctions();
            Assert.Equal(MockDbSyntax.Functions, functions);
        }

        [Fact]
        public void MockDb_KeywordsAndFunctions_AreCaseInsensitive()
        {
            // HashSets are declared with OrdinalIgnoreCase
            Assert.Contains("top", MockDbSyntax.Additions);
            Assert.Contains("getdate", MockDbSyntax.Functions);
            Assert.Contains("limit", MockDbSyntax.Exclusions);
        }

        [Fact]
        public void MockDb_Additions_DoesNotContainLimit()
        {
            // Exclusions should NOT appear in Additions
            Assert.DoesNotContain("LIMIT", MockDbSyntax.Additions);
        }

        // ── OracleSyntax ───────────────────────────────────────────────────────

        [Fact]
        public void Oracle_Functions_ContainsExpectedEntries()
        {
            Assert.Contains("SYSDATE", OracleSyntax.Functions);
            Assert.Contains("TO_CHAR", OracleSyntax.Functions);
            Assert.Contains("NVL", OracleSyntax.Functions);
            Assert.Contains("NVL2", OracleSyntax.Functions);
            Assert.Contains("DECODE", OracleSyntax.Functions);
            Assert.Contains("INSTR", OracleSyntax.Functions);
            Assert.Contains("SUBSTR", OracleSyntax.Functions);
            Assert.Contains("MONTHS_BETWEEN", OracleSyntax.Functions);
            Assert.Contains("SYS_GUID", OracleSyntax.Functions);
        }

        [Fact]
        public void Oracle_Additions_ContainsOracleKeywords()
        {
            Assert.Contains("ROWNUM", OracleSyntax.Additions);
            Assert.Contains("ROWID", OracleSyntax.Additions);
            Assert.Contains("CONNECT_BY", OracleSyntax.Additions);
            Assert.Contains("PRIOR", OracleSyntax.Additions);
        }

        [Fact]
        public void Oracle_Exclusions_ContainsTSqlKeywords()
        {
            // Oracle doesn't support TOP or LIMIT pushdown
            Assert.Contains("TOP", OracleSyntax.Exclusions);
            Assert.Contains("LIMIT", OracleSyntax.Exclusions);
            // Oracle uses NVL, not ISNULL
            Assert.Contains("ISNULL", OracleSyntax.Exclusions);
        }

        [Fact]
        public void Oracle_GetSupportedKeywords_ReturnsSameAsAdditions()
        {
            Assert.Equal(OracleSyntax.Additions, OracleSyntax.GetSupportedKeywords());
        }

        [Fact]
        public void Oracle_GetSupportedFunctions_ReturnsSameAsFunctions()
        {
            Assert.Equal(OracleSyntax.Functions, OracleSyntax.GetSupportedFunctions());
        }

        [Fact]
        public void Oracle_KeywordsAndFunctions_AreCaseInsensitive()
        {
            Assert.Contains("sysdate", OracleSyntax.Functions);
            Assert.Contains("rownum", OracleSyntax.Additions);
            Assert.Contains("top", OracleSyntax.Exclusions);
        }

        [Fact]
        public void Oracle_Exclusions_NotInAdditions()
        {
            // TOP and LIMIT should not be Oracle additions
            Assert.DoesNotContain("TOP", OracleSyntax.Additions);
            Assert.DoesNotContain("LIMIT", OracleSyntax.Additions);
        }

        [Fact]
        public void Oracle_Functions_NotEmpty()
        {
            Assert.NotEmpty(OracleSyntax.Functions);
        }

        [Fact]
        public void MockDb_Functions_NotEmpty()
        {
            Assert.NotEmpty(MockDbSyntax.Functions);
        }
    }
}
