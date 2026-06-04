using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.App;
using ETL_SQL.Common;

namespace ETL_SQL.Tests.App
{
    public class PipelineGeneratorTests : IDisposable
    {
        private readonly string _tempDir;

        public PipelineGeneratorTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "etlsql_pipeline_gen_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }

        [Fact]
        public async Task Generate_MissingSchemaFile_ReturnsError()
        {
            var schemaPath = Path.Combine(_tempDir, "non_existent.json");
            var outputPath = Path.Combine(_tempDir, "output.etlsql");

            var result = await PipelineGenerator.Generate(schemaPath, outputPath, NullLogger.Instance);

            Assert.Equal(1, result);
        }

        [Fact]
        public async Task Generate_SingleDataset_CompilesCorrectly()
        {
            var schemaPath = Path.Combine(_tempDir, "single_schema.json");
            var outputPath = Path.Combine(_tempDir, "customers_load.etlsql");

            var jsonContent = @"
{
  ""pipeline_name"": ""customer_load"",
  ""metadata"": {
    ""description"": ""Load customer data"",
    ""owner"": ""Sales Team"",
    ""classification"": ""confidential""
  },
  ""destination"": {
    ""connector_type"": ""FLATFILE"",
    ""format"": ""CSV"",
    ""path"": ""target_folder"",
    ""naming_pattern"": ""customers_{yyyyMMdd}.csv"",
    ""delimiter"": ""comma"",
    ""has_header"": true,
    ""encoding"": ""UTF8""
  },
  ""schema"": [
    {
      ""column_name"": ""CustomerId"",
      ""type_family"": ""INT"",
      ""nullable"": false,
      ""description"": ""Unique customer ID"",
      ""tags"": [""PII""]
    },
    {
      ""column_name"": ""Email"",
      ""type_family"": ""VARCHAR"",
      ""max_length"": 150,
      ""nullable"": true,
      ""description"": ""Email address"",
      ""validation_regex"": ""^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\\.[a-zA-Z]{2,}$"",
      ""tags"": [""PII""]
    }
  ]
}";
            await File.WriteAllTextAsync(schemaPath, jsonContent);

            var result = await PipelineGenerator.Generate(schemaPath, outputPath, NullLogger.Instance);

            Assert.Equal(0, result);
            Assert.True(File.Exists(outputPath));

            var code = await File.ReadAllTextAsync(outputPath);

            // Assert Header Info
            Assert.Contains("Pipeline: customer_load", code);
            Assert.Contains("Description: Load customer data", code);
            Assert.Contains("Owner: Sales Team", code);
            Assert.Contains("Security Classification: confidential", code);

            // Assert Date formatting and filename generation
            Assert.Contains("DECLARE @DateStr VARCHAR(8);", code);
            Assert.Contains("SET @DateStr = FORMAT(GETDATE(), 'yyyyMMdd');", code);
            Assert.Contains("target_dir + '/' + @FileName", code);

            // Assert Connection Directory Isolation
            Assert.Contains("CREATE CONNECTION target_dir AS DIRECTORY('target_folder');", code);
            Assert.Contains("CREATE CONNECTION outbound_dest AS FLATFILE", code);

            // Assert Expect Schema Checks
            Assert.Contains("EXPECT SCHEMA #staging (", code);
            Assert.Contains("CustomerId                INT", code);
            Assert.Contains("Email                     VARCHAR(150)", code);

            // Assert Tagging
            Assert.Contains("CustomerId                /*@d: Unique customer ID; @pii*/", code);
            Assert.Contains("Email                     /*@d: Email address; @pii*/", code);

            // Assert Regular Expression Validation Gate
            Assert.Contains("REGEXP_LIKE(Email, '^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\\.[a-zA-Z]{2,}$') = 0", code);
            Assert.Contains("THROW 50002", code);

            // Assert Final Tag statement
            Assert.Contains("TAG #cleaned_data WITH (", code);
            Assert.Contains("pipeline_source = 'single_schema.json'", code);
        }

        [Fact]
        public async Task Generate_MultiDataset_GeneratesMasterAndModules()
        {
            var schemaPath = Path.Combine(_tempDir, "multi_schema.json");
            var outputPath = Path.Combine(_tempDir, "master_pipeline.etlsql");

            var jsonContent = @"
{
  ""pipeline_name"": ""multi_load"",
  ""metadata"": {
    ""description"": ""Load multiple feeds"",
    ""owner"": ""Data Platform Team"",
    ""classification"": ""internal""
  },
  ""datasets"": [
    {
      ""name"": ""sales_feed"",
      ""destination"": {
        ""connector_type"": ""FLATFILE"",
        ""format"": ""CSV"",
        ""path"": ""sales_folder"",
        ""naming_pattern"": ""sales.csv""
      },
      ""schema"": [
        {
          ""column_name"": ""SaleId"",
          ""type_family"": ""INT"",
          ""nullable"": false
        }
      ]
    },
    {
      ""name"": ""inventory_feed"",
      ""destination"": {
        ""connector_type"": ""FLATFILE"",
        ""format"": ""CSV"",
        ""path"": ""inv_folder"",
        ""naming_pattern"": ""inventory.csv""
      },
      ""schema"": [
        {
          ""column_name"": ""ItemId"",
          ""type_family"": ""INT"",
          ""nullable"": false
        }
      ]
    }
  ]
}";
            await File.WriteAllTextAsync(schemaPath, jsonContent);

            var result = await PipelineGenerator.Generate(schemaPath, outputPath, NullLogger.Instance);

            Assert.Equal(0, result);
            Assert.True(File.Exists(outputPath));

            // Verify master runner file
            var masterCode = await File.ReadAllTextAsync(outputPath);
            Assert.Contains("Master Pipeline: multi_load", masterCode);
            Assert.Contains("RUN SCRIPT './master_pipeline_modules/sales_feed.etlsql';", masterCode);
            Assert.Contains("RUN SCRIPT './master_pipeline_modules/inventory_feed.etlsql';", masterCode);

            // Verify sub-modules generated in subdirectory
            var modulesDir = Path.Combine(_tempDir, "master_pipeline_modules");
            Assert.True(Directory.Exists(modulesDir));
            
            var salesModuleFile = Path.Combine(modulesDir, "sales_feed.etlsql");
            var inventoryModuleFile = Path.Combine(modulesDir, "inventory_feed.etlsql");
            
            Assert.True(File.Exists(salesModuleFile));
            Assert.True(File.Exists(inventoryModuleFile));

            var salesCode = await File.ReadAllTextAsync(salesModuleFile);
            Assert.Contains("Pipeline: sales_feed", salesCode);
            Assert.Contains("CREATE CONNECTION target_dir AS DIRECTORY('sales_folder');", salesCode);
            Assert.Contains("SaleId                    INT", salesCode);

            var invCode = await File.ReadAllTextAsync(inventoryModuleFile);
            Assert.Contains("Pipeline: inventory_feed", invCode);
            Assert.Contains("CREATE CONNECTION target_dir AS DIRECTORY('inv_folder');", invCode);
            Assert.Contains("ItemId                    INT", invCode);
        }

        [Fact]
        public async Task Generate_AdvancedMappings_GeneratesLookupConstantAndAggregations()
        {
            var schemaPath = Path.Combine(_tempDir, "mapping_schema.json");
            var outputPath = Path.Combine(_tempDir, "mapping_load.etlsql");

            var jsonContent = @"
{
  ""pipeline_name"": ""mapping_pipeline"",
  ""metadata"": {
    ""description"": ""Test mapping rules"",
    ""owner"": ""BI Team"",
    ""classification"": ""internal""
  },
  ""destination"": {
    ""connector_type"": ""FLATFILE"",
    ""format"": ""CSV"",
    ""path"": ""output_dir"",
    ""naming_pattern"": ""output.csv""
  },
  ""schema"": [
    {
      ""column_name"": ""ItemId"",
      ""type_family"": ""INT"",
      ""nullable"": false
    },
    {
      ""column_name"": ""ItemName"",
      ""type_family"": ""VARCHAR"",
      ""mapping_type"": ""lookup"",
      ""mapping_rule"": ""Lookup by ItemId in DimItems""
    },
    {
      ""column_name"": ""SystemSource"",
      ""type_family"": ""VARCHAR"",
      ""mapping_type"": ""constant"",
      ""mapping_rule"": ""SAP""
    },
    {
      ""column_name"": ""TotalQuantity"",
      ""type_family"": ""INT"",
      ""mapping_type"": ""aggregation"",
      ""mapping_rule"": ""Sum of Quantity""
    }
  ]
}";
            await File.WriteAllTextAsync(schemaPath, jsonContent);

            var result = await PipelineGenerator.Generate(schemaPath, outputPath, NullLogger.Instance);

            Assert.Equal(0, result);
            Assert.True(File.Exists(outputPath));

            var code = await File.ReadAllTextAsync(outputPath);

            // Verify advanced mapping generated expressions
            Assert.Contains("L.ItemName /* [LOOKUP]: Lookup by ItemId in DimItems */", code);
            Assert.Contains("'SAP'                                              AS SystemSource", code);
            Assert.Contains("SUM(TotalQuantity) /* [AGGREGATION]: Sum of Quantity */", code);

            // Verify USER TODO comment hints are generated for lookups and aggregations
            Assert.Contains("Uncomment and complete reference lookup joins (L alias)", code);
            Assert.Contains("LEFT JOIN target_db.dbo.LookupTable AS L ON #staging.SourceKey = L.SourceKey", code);
            Assert.Contains("Group by non-aggregated columns for calculations", code);
            Assert.Contains("GROUP BY ItemId, ItemName, SystemSource", code);
        }
    }
}
