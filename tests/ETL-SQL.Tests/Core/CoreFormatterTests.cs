using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Formatting;
using ETL_SQL.Data;
using Spectre.Console;
using Xunit;

namespace ETL_SQL.Tests.Core
{
    public class FormatterTests
    {
        [Fact]
        public void FormatterOptions_DefaultsAndJsonSurfaceRemainDocumentedContract()
        {
            var defaults = new FormatterOptions();
            Assert.Equal("upper", defaults.KeywordCasing);
            Assert.Equal(4, defaults.IndentSize);
            Assert.Equal(100, defaults.LineWidth);
            Assert.True(defaults.IndentJoins);
            Assert.False(defaults.OnClauseOnNewLine);
            Assert.True(defaults.CaseWhenThenNewLine);
            Assert.True(defaults.BreakoutWindowFunctions);
            Assert.Equal("leading", defaults.CommaPlacement);
            Assert.False(defaults.RightAlignKeywords);
            Assert.True(defaults.FormatMetadataTags);

            var json = """
                {
                  "keywordCasing": "lower",
                  "indentSize": 2,
                  "lineWidth": 120,
                  "indentJoins": false,
                  "onClauseOnNewLine": true,
                  "caseWhenThenNewLine": false,
                  "breakoutWindowFunctions": false,
                  "commaPlacement": "trailing",
                  "rightAlignKeywords": true,
                  "formatMetadataTags": false
                }
                """;
            var loaded = System.Text.Json.JsonSerializer.Deserialize<FormatterOptions>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.NotNull(loaded);
            Assert.Equal("lower", loaded.KeywordCasing);
            Assert.Equal(2, loaded.IndentSize);
            Assert.Equal(120, loaded.LineWidth);
            Assert.False(loaded.IndentJoins);
            Assert.True(loaded.OnClauseOnNewLine);
            Assert.False(loaded.CaseWhenThenNewLine);
            Assert.False(loaded.BreakoutWindowFunctions);
            Assert.Equal("trailing", loaded.CommaPlacement);
            Assert.True(loaded.RightAlignKeywords);
            Assert.False(loaded.FormatMetadataTags);
        }

        [Fact]
        public void TestBasicFormatting()
        {
            string sql = "SELECT a, b FROM table WHERE c = 1";
            string formatted = SqlFormatter.Format(sql);

            Assert.Contains("SELECT", formatted);
            Assert.Contains("FROM", formatted);
            Assert.Contains("WHERE", formatted);
        }

        [Fact]
        public void TestLeadingCommaPlacement()
        {
            string sql = "SELECT col1, col2, col3 FROM t";
            string formatted = SqlFormatter.Format(sql).Replace("\r\n", "\n");

            Assert.Contains("\n    ,col2", formatted);
            Assert.Contains("\n    ,col3", formatted);
        }

        [Fact]
        public void TestIndentedClauses()
        {
            string sql = "SELECT * FROM t WHERE a=1 AND b=2 OR c=3";
            string formatted = SqlFormatter.Format(sql).Replace("\r\n", "\n");

            Assert.Contains("\nWHERE", formatted);
            Assert.Contains("\n    AND", formatted);
            Assert.Contains("\n    OR", formatted);
        }

        [Fact]
        public void TestComplexJoinFormatting()
        {
            string sql = "SELECT * FROM t1 JOIN t2 ON t1.id = t2.id WHERE t1.val > 10";
            string formatted = SqlFormatter.Format(sql).Replace("\r\n", "\n");

            Assert.Contains("\n    JOIN", formatted);
            Assert.Contains("\nWHERE", formatted);
        }

        [Fact]
        public void TestSelectFirstColumnIndentation()
        {
            string sql = "SELECT col1, col2 FROM t";
            string formatted = SqlFormatter.Format(sql).Replace("\r\n", "\n");

            var lines = formatted.Split('\n');
            Assert.True(lines.Length >= 3);
            string col1Line = lines.FirstOrDefault(l => l.Contains("col1", StringComparison.OrdinalIgnoreCase)) ?? "";
            Assert.StartsWith("     ", col1Line); // 5 spaces
        }

        [Fact]
        public void TestSubqueryNestingInVisual()
        {
            string sql = "CREATE VISUAL SalesBar AS BAR ( SOURCE = (SELECT product, revenue FROM orders WHERE id = 1), MAPPINGS (X = product) );";
            var options = new FormatterOptions { CommaPlacement = "trailing" };
            string formatted = SqlFormatter.Format(sql, options).Replace("\r\n", "\n");

            // Verify that the SELECT statement is indented inside parenthesized block
            Assert.Contains("    SELECT", formatted);
            // Verify that outer MAPPINGS isn't squashed to the left
            Assert.Contains("\n    MAPPINGS", formatted);
        }

        [Fact]
        public void TestJoinIndentationOptions()
        {
            string sql = "SELECT * FROM t1 JOIN t2 ON t1.id = t2.id";

            // Indent Joins = true, ON Clause on new line = true
            var opt1 = new FormatterOptions { IndentJoins = true, OnClauseOnNewLine = true, IndentSize = 4 };
            string res1 = SqlFormatter.Format(sql, opt1).Replace("\r\n", "\n");
            Assert.Contains("\n    JOIN t2", res1);
            Assert.Contains("\n        ON t1.id = t2.id", res1);

            // Indent Joins = false, ON Clause on new line = false
            var opt2 = new FormatterOptions { IndentJoins = false, OnClauseOnNewLine = false };
            string res2 = SqlFormatter.Format(sql, opt2).Replace("\r\n", "\n");
            Assert.Contains("\nJOIN t2 ON t1.id = t2.id", res2);
        }

        [Fact]
        public void TestCaseStatementFormatting()
        {
            string sql = "SELECT CASE WHEN val = 1 THEN 'one' ELSE 'other' END FROM t";

            // Case When Then on New Line = true
            var opt1 = new FormatterOptions { CaseWhenThenNewLine = true };
            string res1 = SqlFormatter.Format(sql, opt1).Replace("\r\n", "\n");
            Assert.Contains("        WHEN val = 1", res1);
            Assert.Contains("            THEN 'one'", res1);
            Assert.Contains("        ELSE 'other'", res1);
            Assert.Contains("    END", res1);

            // Case When Then on New Line = false
            var opt2 = new FormatterOptions { CaseWhenThenNewLine = false };
            string res2 = SqlFormatter.Format(sql, opt2).Replace("\r\n", "\n");
            Assert.Contains("        WHEN val = 1 THEN 'one'", res2);
        }

        [Fact]
        public void TestWindowFunctionFormatting()
        {
            string sql = "SELECT ROW_NUMBER() OVER (PARTITION BY cat ORDER BY val DESC) FROM t";

            var opt = new FormatterOptions { BreakoutWindowFunctions = true };
            string res = SqlFormatter.Format(sql, opt).Replace("\r\n", "\n");
            Assert.Contains("ROW_NUMBER() OVER (\n        PARTITION BY cat\n        ORDER BY val DESC\n    )", res);
        }

        [Fact]
        public void TestUniversalCommaFormatting()
        {
            // Trailing Commas option
            var opt = new FormatterOptions { CommaPlacement = "trailing" };
            string sql = "SELECT col1, col2 FROM t";
            string res = SqlFormatter.Format(sql, opt).Replace("\r\n", "\n");
            Assert.Contains("col1,\n", res);
            Assert.Contains("col2\n", res);
        }

        [Fact]
        public void TestCteFormatting()
        {
            string sql = "WITH MyCte AS (SELECT col1, col2 FROM Table1) SELECT * FROM MyCte;";
            var options = new FormatterOptions { CommaPlacement = "trailing" };
            string formatted = SqlFormatter.Format(sql, options).Replace("\r\n", "\n");

            Assert.Contains("WITH MyCte AS (\n    SELECT\n        col1,\n        col2\n    FROM Table1\n)", formatted);
            Assert.Contains("SELECT\n    *\nFROM MyCte", formatted);
        }

        [Fact]
        public void TestSubqueryInFromFormatting()
        {
            string sql = "SELECT * FROM (SELECT id, name FROM customers) c JOIN orders o ON c.id = o.customer_id;";
            var options = new FormatterOptions { CommaPlacement = "trailing" };
            string formatted = SqlFormatter.Format(sql, options).Replace("\r\n", "\n");

            Assert.Contains("FROM (\n    SELECT\n        id,\n        name\n    FROM customers\n) c", formatted);
            Assert.Contains("    JOIN orders o ON c.id = o.customer_id", formatted);
        }

        [Fact]
        public void TestVisualFormattingFromProposal()
        {
            string sql = "CREATE VISUAL SalesBar AS BAR ( SOURCE = (SELECT product, SUM(revenue) AS revenue FROM &orders WHERE region = @region GROUP BY product), MAPPINGS (X = product, Y = revenue) );";
            var options = new FormatterOptions { CommaPlacement = "trailing" };
            string formatted = SqlFormatter.Format(sql, options).Replace("\r\n", "\n");

            // Verify outer visual alignment and indentation
            Assert.Contains("CREATE VISUAL SalesBar AS BAR (", formatted);
            Assert.Contains("    SOURCE = (", formatted);
            Assert.Contains("        SELECT", formatted);
            Assert.Contains("            product,", formatted);
            Assert.Contains("            SUM(revenue) AS revenue", formatted);
            Assert.Contains("        FROM &orders", formatted);
            Assert.Contains("        WHERE", formatted);
            Assert.Contains("            region = @region", formatted);
            Assert.Contains("        GROUP BY", formatted);
            Assert.Contains("            product", formatted);
            Assert.Contains("    ),", formatted);
            Assert.Contains("    MAPPINGS (", formatted);
            Assert.Contains("        X = product,", formatted);
            Assert.Contains("        Y = revenue", formatted);
            Assert.Contains("    )", formatted);
        }

        [Fact]
        public void TestSpecialShorthandFormatting()
        {
            string sql = "SELECT * FROM t WHERE 1=1 AND (1=0 OR col1 = a OR col2 = b);";
            var options = new FormatterOptions { CommaPlacement = "trailing" };
            string formatted = SqlFormatter.Format(sql, options).Replace("\r\n", "\n");

            string expected = "SELECT\n" +
                              "    *\n" +
                              "FROM t\n" +
                              "WHERE 1=1\n" +
                              "    AND (1=0\n" +
                              "        OR col1 = a\n" +
                              "        OR col2 = b\n" +
                              "    );";
            Assert.Equal(expected, formatted);
        }

        [Fact]
        public void TestMetadataTagFormatting()
        {
            string sql = "SELECT id /* @d: The identity column; @pii; */, name FROM customers;";
            var options = new FormatterOptions { FormatMetadataTags = true, CommaPlacement = "trailing" };
            string formatted = SqlFormatter.Format(sql, options).Replace("\r\n", "\n");

            string expected = "SELECT\n" +
                              "    id\n" +
                              "        /*\n" +
                              "            @d: The identity column;\n" +
                              "            @pii;\n" +
                              "        */,\n" +
                              "    name\n" +
                              "FROM customers;";
            Assert.Equal(expected, formatted);
        }

        [Fact]
        public void TestMetadataTagFormattingDisabled()
        {
            string sql = "SELECT id /* @d: The identity column; @pii; */, name FROM customers;";
            var options = new FormatterOptions { FormatMetadataTags = false, CommaPlacement = "trailing" };
            string formatted = SqlFormatter.Format(sql, options).Replace("\r\n", "\n");

            Assert.Contains("id /* @d: The identity column; @pii; */,", formatted);
        }

        [Fact]
        public void TestMetadataTagFormattingWithQuotedSemicolonsAndInLists()
        {
            string sql = "SELECT gender /* @d: patient gender; @example: \"IN (; 'MALE' ,'FEMALE' ,'OTHER' )\"; @owner: 'Bob'; */ FROM pats;";
            var options = new FormatterOptions { FormatMetadataTags = true, CommaPlacement = "trailing" };
            string formatted = SqlFormatter.Format(sql, options).Replace("\r\n", "\n");

            string expected = "SELECT\n" +
                              "    gender\n" +
                              "        /*\n" +
                              "            @d: patient gender;\n" +
                              "            @example: \"IN ('MALE', 'FEMALE', 'OTHER')\";\n" +
                              "            @owner: 'Bob';\n" +
                              "        */\n" +
                              "FROM pats;";
            Assert.Equal(expected, formatted);
        }

        [Fact]
        public void TestFunctionCallParameterListFormatting()
        {
            string sql = "CREATE TABLE Patient (patient_id bigint IDENTITY(1,1) PRIMARY KEY);";
            var options = new FormatterOptions();
            string formatted = SqlFormatter.Format(sql, options).Replace("\r\n", "\n");

            Assert.Contains("IDENTITY(1, 1)", formatted);
        }
    }
}
