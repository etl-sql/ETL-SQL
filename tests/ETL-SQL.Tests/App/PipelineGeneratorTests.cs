using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core.Parser;
using Xunit;

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
        public async Task Generate_InvalidSchemaContract_ReturnsError()
        {
            var schemaPath = Path.Combine(_tempDir, "invalid_schema.json");
            var outputPath = Path.Combine(_tempDir, "invalid_output.etlsql");

            var jsonContent = @"
{
  ""pipeline_name"": ""bad_feed"",
  ""metadata"": {
    ""description"": ""Missing destination and schema"",
    ""classification"": ""confidential"",
    ""owner"": ""Data Team""
  }
}";
            await File.WriteAllTextAsync(schemaPath, jsonContent);

            var result = await PipelineGenerator.Generate(schemaPath, outputPath, NullLogger.Instance);

            Assert.Equal(1, result);
            Assert.False(File.Exists(outputPath));
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
  ""confidence"": 0.91,
  ""source_evidence"": [
    {
      ""document"": ""customer_spec.pdf"",
      ""page"": 2,
      ""section"": ""Customer Feed Overview"",
      ""text"": ""Daily customer feed""
    }
  ],
  ""source"": {
    ""confidence"": 0.86,
    ""source_evidence"": [
      {
        ""page"": 4,
        ""section"": ""Inbound File Layout"",
        ""text"": ""CSV with header row""
      }
    ],
    ""connector_type"": ""FLATFILE"",
    ""format"": ""CSV"",
    ""path"": ""C:/Inbound/customers.csv"",
    ""delimiter"": ""comma"",
    ""text_qualifier"": ""doublequote"",
    ""encoding"": ""UTF8"",
    ""has_header"": true,
    ""header_rows"": 1,
    ""skip_rows"": 0,
    ""null_tokens"": ["""", ""NULL"", ""N/A""],
    ""primary_keys"": [""CustomerId""],
    ""duplicate_policy"": ""reject"",
    ""reject_policy"": ""quarantine""
  },
  ""destination"": {
    ""connector_type"": ""FLATFILE"",
    ""confidence"": 0.83,
    ""source_evidence"": [
      {
        ""page"": 5,
        ""section"": ""Outbound Delivery"",
        ""text"": ""CustomerFeeds folder""
      }
    ],
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
      ""confidence"": 0.98,
      ""source_evidence"": [
        {
          ""page"": 6,
          ""section"": ""Data Dictionary"",
          ""original_field_name"": ""customer_id"",
          ""text"": ""Unique customer ID""
        }
      ],
      ""source_name"": ""customer_id"",
      ""type_family"": ""INT"",
      ""nullable"": false,
      ""description"": ""Unique customer ID"",
      ""is_key"": true,
      ""tags"": [""PII""]
    },
    {
      ""column_name"": ""Email"",
      ""source_name"": ""email_address"",
      ""type_family"": ""VARCHAR"",
      ""max_length"": 150,
      ""nullable"": true,
      ""description"": ""Email address"",
      ""validation_regex"": ""^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\\.[a-zA-Z]{2,}$"",
      ""null_tokens"": ["""", ""UNKNOWN""],
      ""allowed_values"": [""valid_email_format""],
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

            // Assert AI Review Evidence
            Assert.Contains("--   pipeline confidence: 0.91", code);
            Assert.Contains("--   pipeline evidence: doc=customer_spec.pdf; page=2; section=Customer Feed Overview; text=Daily customer feed", code);
            Assert.Contains("--   source confidence: 0.86", code);
            Assert.Contains("--   destination confidence: 0.83", code);
            Assert.Contains("--   column CustomerId confidence: 0.98", code);
            Assert.Contains("--   column CustomerId evidence: page=6; section=Data Dictionary; field=customer_id; text=Unique customer ID", code);

            // Assert Source Layout Contract
            Assert.Contains("--   source connector: FLATFILE", code);
            Assert.Contains("--   header rows: 1", code);
            Assert.Contains("--   null tokens: , NULL, N/A", code);
            Assert.Contains("--   primary keys: CustomerId", code);
            Assert.Contains("--   duplicate policy: reject", code);
            Assert.Contains("CREATE CONNECTION src_file AS FLATFILE(", code);
            Assert.Contains("PATH = 'C:/Inbound/customers.csv'", code);
            Assert.Contains("customer_id                  AS CustomerId", code);
            Assert.Contains("email_address                AS Email", code);
            Assert.Contains("column Email: source=email_address; null_tokens=|UNKNOWN; allowed_values=valid_email_format", code);

            // Assert Expect Schema Checks
            Assert.Contains("EXPECT SCHEMA #staging (", code);
            Assert.Contains("CustomerId                INT", code);
            Assert.Contains("Email                     VARCHAR(150)", code);

            // Assert Tagging
            Assert.Contains("CustomerId                /*@d: Unique customer ID; @pii*/", code);
            Assert.Contains("Email                     /*@d: Email address; @pii*/", code);

            // Assert Validation Review and Quarantine Gates
            Assert.Contains("REGEXP_LIKE(Email, '^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\\.[a-zA-Z]{2,}$') = 0", code);
            Assert.Contains("CREATE TABLE #spec_validation_issues", code);
            Assert.Contains("'REGEX_FORMAT'", code);
            Assert.Contains("'ALLOWED_VALUES'", code);
            Assert.Contains("SELECT * INTO #rejected_data FROM #cleaned_data", code);
            Assert.Contains("SELECT * INTO #valid_data FROM #cleaned_data", code);
            Assert.Contains("SELECT * INTO outbound_dest FROM #valid_data;", code);

            // Assert Final Tag statement
            Assert.Contains("TAG #cleaned_data WITH (", code);
            Assert.Contains("pipeline_source = 'single_schema.json'", code);

            var script = new Parser(new Lexer(code).Tokenize(), code).Parse();
            Assert.Empty(script.Diagnostics);
        }

        [Fact]
        public async Task Generate_FlatFileConnectorWithExcelFormat_ResolvesToExcelConnector()
        {
            var schemaPath = Path.Combine(_tempDir, "excel_source_schema.json");
            var outputPath = Path.Combine(_tempDir, "excel_load.etlsql");

            var jsonContent = @"
{
  ""pipeline_name"": ""excel_load"",
  ""metadata"": {
    ""description"": ""Load excel source"",
    ""owner"": ""BI Team"",
    ""classification"": ""internal""
  },
  ""source"": {
    ""connector_type"": ""FLATFILE"",
    ""format"": ""EXCEL"",
    ""path"": ""C:/Inbound/data.xlsx"",
    ""sheet_name"": ""Locations_"",
    ""has_header"": true
  },
  ""destination"": {
    ""connector_type"": ""FLATFILE"",
    ""format"": ""EXCEL"",
    ""path"": ""target_folder"",
    ""sheet_name"": ""Results""
  },
  ""schema"": [
    {
      ""column_name"": ""Id"",
      ""type_family"": ""INT"",
      ""nullable"": false
    }
  ]
}";
            await File.WriteAllTextAsync(schemaPath, jsonContent);

            var result = await PipelineGenerator.Generate(schemaPath, outputPath, NullLogger.Instance);

            Assert.Equal(0, result);
            Assert.True(File.Exists(outputPath));

            var code = await File.ReadAllTextAsync(outputPath);

            // Assert source resolved to EXCEL connector
            Assert.Contains("CREATE CONNECTION src_file AS EXCEL(", code);
            Assert.Contains("PATH = 'C:/Inbound/data.xlsx'", code);
            Assert.Contains("SHEET = 'Locations_'", code);
            Assert.Contains("HEADER = ON", code);

            // Assert destination resolved to EXCEL connector
            Assert.Contains("CREATE CONNECTION target_dir AS DIRECTORY('target_folder');", code);
            Assert.Contains("CREATE CONNECTION outbound_dest AS EXCEL(", code);
            Assert.Contains("SHEET = 'Results'", code);
            Assert.Contains("HEADER = ON", code);

            var script = new Parser(new Lexer(code).Tokenize(), code).Parse();
            Assert.Empty(script.Diagnostics);
        }

        [Fact]
        public async Task Generate_InvalidEvidenceMetadata_ReturnsError()
        {
            var schemaPath = Path.Combine(_tempDir, "invalid_evidence_schema.json");
            var outputPath = Path.Combine(_tempDir, "invalid_evidence_output.etlsql");

            var jsonContent = @"
{
  ""pipeline_name"": ""bad_evidence"",
  ""metadata"": {
    ""description"": ""Invalid evidence metadata"",
    ""classification"": ""internal"",
    ""owner"": ""Data Team""
  },
  ""confidence"": 1.5,
  ""source_evidence"": [
    {
      ""page"": 0
    }
  ],
  ""destination"": {
    ""connector_type"": ""FLATFILE"",
    ""format"": ""CSV"",
    ""path"": ""outbound""
  },
  ""schema"": [
    {
      ""column_name"": ""CustomerId"",
      ""type_family"": ""INT"",
      ""nullable"": false
    }
  ]
}";
            await File.WriteAllTextAsync(schemaPath, jsonContent);

            var result = await PipelineGenerator.Generate(schemaPath, outputPath, NullLogger.Instance);

            Assert.Equal(1, result);
            Assert.False(File.Exists(outputPath));
        }

        [Fact]
        public async Task Generate_InvalidSourceLayout_ReturnsError()
        {
            var schemaPath = Path.Combine(_tempDir, "invalid_layout_schema.json");
            var outputPath = Path.Combine(_tempDir, "invalid_layout_output.etlsql");

            var jsonContent = @"
{
  ""pipeline_name"": ""bad_layout"",
  ""metadata"": {
    ""description"": ""Invalid fixed-width metadata"",
    ""classification"": ""internal"",
    ""owner"": ""Data Team""
  },
  ""source"": {
    ""connector_type"": ""FLATFILE"",
    ""format"": ""CSV"",
    ""duplicate_policy"": ""reject""
  },
  ""destination"": {
    ""connector_type"": ""FLATFILE"",
    ""format"": ""CSV"",
    ""path"": ""outbound""
  },
  ""schema"": [
    {
      ""column_name"": ""CustomerId"",
      ""source_name"": ""customer_id"",
      ""start_position"": 0,
      ""type_family"": ""INT"",
      ""nullable"": false
    }
  ]
}";
            await File.WriteAllTextAsync(schemaPath, jsonContent);

            var result = await PipelineGenerator.Generate(schemaPath, outputPath, NullLogger.Instance);

            Assert.Equal(1, result);
            Assert.False(File.Exists(outputPath));
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

            Assert.Empty(new Parser(new Lexer(masterCode).Tokenize(), masterCode).Parse().Diagnostics);
            Assert.Empty(new Parser(new Lexer(salesCode).Tokenize(), salesCode).Parse().Diagnostics);
            Assert.Empty(new Parser(new Lexer(invCode).Tokenize(), invCode).Parse().Diagnostics);
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

        // ── Dataset-name injection / traversal hardening ──────────────────────

        private static string BuildMultiDatasetSpecJson(params string[] datasetNames)
        {
            var datasets = datasetNames.Select(n => new Dictionary<string, object?>
            {
                ["name"] = n,
                ["destination"] = new Dictionary<string, object?>
                {
                    ["connector_type"] = "FLATFILE",
                    ["format"] = "CSV",
                    ["path"] = "out_folder",
                    ["naming_pattern"] = "out.csv"
                },
                ["schema"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["column_name"] = "Id",
                        ["type_family"] = "INT",
                        ["nullable"] = false
                    }
                }
            }).ToArray();

            var spec = new Dictionary<string, object?>
            {
                ["pipeline_name"] = "multi",
                ["metadata"] = new Dictionary<string, object?>
                {
                    ["description"] = "Injection hardening fixture",
                    ["owner"] = "Data Team",
                    ["classification"] = "internal"
                },
                ["datasets"] = datasets
            };

            return JsonSerializer.Serialize(spec);
        }

        private async Task<int> GenerateWithDatasetNamesAsync(string fileTag, params string[] datasetNames)
        {
            var schemaPath = Path.Combine(_tempDir, $"inj_{fileTag}.json");
            var outputPath = Path.Combine(_tempDir, $"inj_{fileTag}.etlsql");
            await File.WriteAllTextAsync(schemaPath, BuildMultiDatasetSpecJson(datasetNames));
            return await PipelineGenerator.Generate(schemaPath, outputPath, NullLogger.Instance);
        }

        [Theory]
        [InlineData("traversal", "../evil")]
        [InlineData("traversal_deep", "../../../etc/cron.d/evil")]
        [InlineData("fwd_slash", "sub/dir")]
        [InlineData("back_slash", "sub\\dir")]
        [InlineData("single_quote", "a'; PRINT 'pwned")]
        [InlineData("newline", "a\nPRINT 'pwned'")]
        [InlineData("reserved_con", "CON")]
        [InlineData("reserved_lpt1", "LPT1")]
        [InlineData("leading_dot", ".hidden")]
        [InlineData("space", "bad name")]
        public async Task Generate_UnsafeDatasetName_IsRejected(string tag, string datasetName)
        {
            var before = Directory.GetFiles(_tempDir, "*", SearchOption.AllDirectories).Length;

            var result = await GenerateWithDatasetNamesAsync(tag, datasetName);

            Assert.Equal(1, result);
            // No master script and no module files should be written when validation rejects the spec.
            Assert.False(File.Exists(Path.Combine(_tempDir, $"inj_{tag}.etlsql")));
            var etlsqlFiles = Directory.GetFiles(_tempDir, "*.etlsql", SearchOption.AllDirectories);
            Assert.Empty(etlsqlFiles);
            // The only new file is the schema JSON we wrote.
            var after = Directory.GetFiles(_tempDir, "*", SearchOption.AllDirectories).Length;
            Assert.Equal(before + 1, after);
        }

        [Fact]
        public async Task Generate_DuplicateNormalizedDatasetNames_IsRejected()
        {
            // "Sales" and "sales" collide to the same module filename on case-insensitive filesystems.
            var result = await GenerateWithDatasetNamesAsync("dupe", "Sales", "sales");

            Assert.Equal(1, result);
            Assert.Empty(Directory.GetFiles(_tempDir, "*.etlsql", SearchOption.AllDirectories));
        }

        [Fact]
        public async Task Generate_SafeDatasetNames_ProduceContainedModules()
        {
            var schemaPath = Path.Combine(_tempDir, "inj_ok.json");
            var outputPath = Path.Combine(_tempDir, "inj_ok.etlsql");
            await File.WriteAllTextAsync(schemaPath, BuildMultiDatasetSpecJson("sales_feed", "inv-2"));

            var result = await PipelineGenerator.Generate(schemaPath, outputPath, NullLogger.Instance);

            Assert.Equal(0, result);
            var masterCode = await File.ReadAllTextAsync(outputPath);
            Assert.Contains("RUN SCRIPT './inj_ok_modules/sales_feed.etlsql';", masterCode);
            Assert.Contains("RUN SCRIPT './inj_ok_modules/inv-2.etlsql';", masterCode);

            var modulesDir = Path.Combine(_tempDir, "inj_ok_modules");
            Assert.True(File.Exists(Path.Combine(modulesDir, "sales_feed.etlsql")));
            Assert.True(File.Exists(Path.Combine(modulesDir, "inv-2.etlsql")));

            // Every generated module path stays under the modules directory.
            foreach (var file in Directory.GetFiles(modulesDir))
            {
                Assert.StartsWith(Path.GetFullPath(modulesDir), Path.GetFullPath(file));
            }
        }
    }
}
