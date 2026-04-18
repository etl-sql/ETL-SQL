using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Core.Formatting;
using Spectre.Console;

namespace ETL_SQL.Tests.Core
{
    public class FormatterTests
    {
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
        public void TestLeadingCommas()
        {
            string sql = "SELECT col1, col2, col3 FROM t";
            string formatted = SqlFormatter.Format(sql);
            
            Assert.Contains("\n    ,col2", formatted);
            Assert.Contains("\n    ,col3", formatted);
        }

        [Fact]
        public void TestIndentedClauses()
        {
            string sql = "SELECT * FROM t WHERE a=1 AND b=2 OR c=3";
            string formatted = SqlFormatter.Format(sql);
            
            Assert.Contains("\nWHERE", formatted);
            Assert.Contains("\n    AND", formatted);
            Assert.Contains("\n    OR", formatted);
        }

        [Fact]
        public void TestComplexJoinFormatting()
        {
            string sql = "SELECT * FROM t1 JOIN t2 ON t1.id = t2.id WHERE t1.val > 10";
            string formatted = SqlFormatter.Format(sql);
            
            Assert.Contains("\nJOIN", formatted);
            Assert.Contains("\nWHERE", formatted);
        }

        [Fact]
        public void TestSelectFirstColumnIndentation()
        {
            string sql = "SELECT col1, col2 FROM t";
            string formatted = SqlFormatter.Format(sql);
            
            var lines = formatted.Split('\n');
            Assert.True(lines.Length >= 3);
            string col1Line = lines.FirstOrDefault(l => l.Contains("col1", StringComparison.OrdinalIgnoreCase)) ?? "";
            Assert.StartsWith("     ", col1Line); // 5 spaces
        }
    }
}
