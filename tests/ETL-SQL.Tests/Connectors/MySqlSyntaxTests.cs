using ETL_SQL.Connectors.MySql;
using Xunit;

namespace ETL_SQL.Tests.Connectors
{
    // Verifies the MySQL/MariaDB dialect vocabulary (additions/exclusions relative to the ETL-SQL
    // baseline). Pure static data + accessors — no live database required.
    public class MySqlSyntaxTests
    {
        [Fact]
        public void SupportedKeywords_IncludeMySqlAdditions_CaseInsensitive()
        {
            var keywords = MySqlSyntax.GetSupportedKeywords();
            Assert.Contains("LIMIT", keywords);
            Assert.Contains("OFFSET", keywords);
            Assert.Contains("RLIKE", keywords);
            Assert.Contains("limit", keywords); // OrdinalIgnoreCase
        }

        [Fact]
        public void SupportedFunctions_IncludeMySqlFunctions_CaseInsensitive()
        {
            var funcs = MySqlSyntax.GetSupportedFunctions();
            Assert.Contains("IFNULL", funcs);
            Assert.Contains("GROUP_CONCAT", funcs);
            Assert.Contains("json_extract", funcs); // OrdinalIgnoreCase
        }

        [Fact]
        public void Exclusions_CoverTSqlAndOracleSpecificKeywords()
        {
            Assert.Contains("TOP", MySqlSyntax.Exclusions);       // MySQL uses LIMIT
            Assert.Contains("ROWNUM", MySqlSyntax.Exclusions);    // Oracle-specific
            Assert.Contains("GETDATE", MySqlSyntax.Exclusions);   // T-SQL-specific
            Assert.Contains("ISNULL", MySqlSyntax.Exclusions);    // 2-arg T-SQL ISNULL unsupported
            Assert.DoesNotContain("LIMIT", MySqlSyntax.Exclusions);
        }

        [Fact]
        public void Additions_And_Exclusions_DoNotOverlap()
        {
            foreach (var addition in MySqlSyntax.Additions)
                Assert.DoesNotContain(addition, MySqlSyntax.Exclusions);
        }
    }
}
