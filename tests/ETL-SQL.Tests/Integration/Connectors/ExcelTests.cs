using Xunit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Connectors.FlatFile;
using Spectre.Console;
using ETL_SQL.Common;
using ETL_SQL.Core.Common;

namespace ETL_SQL.Tests.Integration
{
    public class ExcelTests
    {
        [Fact]
        public async Task TestExcelBasicRead()
        {
            string excelPlaceholder = "ExcelTest_Basic.csv";
            await File.WriteAllTextAsync(excelPlaceholder, "id,col1\n1,val1");

            try
            {
                // In this implementation, ExcelConnector creates a FlatFileDataSource as a placeholder
                var ds = new FlatFileDataSource(SystemExecutionContext.Instance, excelPlaceholder);
                var batches = await ds.ReadBatches().ToListAsync();

                Assert.Single(batches);
                Assert.Single(batches[0].Rows);
            }
            finally 
            { 
                if (File.Exists(excelPlaceholder)) File.Delete(excelPlaceholder); 
            }
        }

        [Fact]
        public async Task TestExcelOptions()
        {
            string excelPlaceholder = "ExcelTest_Options.csv";
            await File.WriteAllTextAsync(excelPlaceholder, "HeaderRow\nid,col1\n1,val1");

            try
            {
                var options = new Dictionary<string, string> 
                { 
                    { "SHEET", "Sheet1" },
                    { "HEADER", "ON" },
                    { "START_AT", "1" } 
                };
                
                // FlatFileDataSource handles START_AT, which simulates finding a sheet/header offset
                var ds = new FlatFileDataSource(SystemExecutionContext.Instance, excelPlaceholder, options);
                var batches = await ds.ReadBatches().ToListAsync();

                Assert.Equal("val1", batches[0].Rows[0]["col1"]?.ToString());
            }
            finally 
            { 
                if (File.Exists(excelPlaceholder)) File.Delete(excelPlaceholder); 
            }
        }
    }
}
