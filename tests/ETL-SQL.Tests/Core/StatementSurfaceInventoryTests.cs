using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ETL_SQL.Core;
using Xunit;

namespace ETL_SQL.Tests.Core
{
    public class StatementSurfaceInventoryTests
    {
        private static readonly string SolutionRoot;

        static StatementSurfaceInventoryTests()
        {
            // Traverse up from bin to find the repository root
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "ETL-SQL.slnx")))
            {
                dir = Path.GetDirectoryName(dir);
            }
            SolutionRoot = dir ?? throw new InvalidOperationException("Could not locate solution root containing ETL-SQL.slnx");
        }

        [Fact]
        public void VerifyAllStatementsHaveFormatterCoverage()
        {
            var formatterPath = Path.Combine(SolutionRoot, "src", "ETL-SQL.Core", "Formatting", "AstSerializer.cs");
            Assert.True(File.Exists(formatterPath), $"AstSerializer.cs not found at: {formatterPath}");
            var formatterText = File.ReadAllText(formatterPath);

            var statementTypes = typeof(Statement).Assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(Statement)) && !t.IsAbstract)
                .ToList();

            foreach (var type in statementTypes)
            {
                var typeName = type.Name;

                // Exempt auxiliary or internal statements if needed
                if (typeName == "NoOpStatement") continue;

                Assert.True(
                    formatterText.Contains(typeName),
                    $"Statement '{typeName}' is missing formatting coverage in AstSerializer.cs. " +
                    "Every executable statement must have a real ToSql() serialization pattern."
                );
            }
        }

        [Fact]
        public void VerifyLifecycleCreatableStatementsHaveDocumentationCoverage()
        {
            var docsDir = Path.Combine(SolutionRoot, "docs");
            Assert.True(Directory.Exists(docsDir), $"docs/ directory not found at: {docsDir}");

            var docFiles = Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories)
                .Select(f => File.ReadAllText(f))
                .ToList();

            var statementTypes = typeof(Statement).Assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(Statement)) && !t.IsAbstract)
                .Where(t => t.Name.StartsWith("Create", StringComparison.Ordinal))
                .Where(t => t.GetProperty("Mode", BindingFlags.Instance | BindingFlags.Public)?.PropertyType == typeof(ObjectCreationMode))
                .ToList();

            foreach (var type in statementTypes)
            {
                var typeName = type.Name;
                if (typeName == "NoOpStatement") continue;

                var keywords = PascalToKeywords(typeName);
                var keywordsUnderscored = keywords.Replace(" ", "_");
                var cleanKeyword = keywords.Replace(" ", "").Replace("_", "").Replace("-", "");

                // Check if any doc file contains the class name or the normalized keyword statement
                var found = docFiles.Any(content =>
                {
                    if (content.Contains(typeName, StringComparison.OrdinalIgnoreCase) ||
                        content.Contains(keywords, StringComparison.OrdinalIgnoreCase) ||
                        content.Contains(keywordsUnderscored, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    var cleanContent = content.Replace(" ", "").Replace("_", "").Replace("-", "");
                    return cleanContent.Contains(cleanKeyword, StringComparison.OrdinalIgnoreCase);
                });

                Assert.True(
                    found,
                    $"Statement '{typeName}' (keywords: '{keywords}' / '{keywordsUnderscored}') lacks corresponding reference page documentation in the docs/ folder."
                );
            }
        }

        [Fact]
        public void EveryEngineCatalogTableHasColumnsAndAnExactReferencePage()
        {
            var engReferenceDir = Path.Combine(SolutionRoot, "docs", "reference", "eng");
            var readme = File.ReadAllText(Path.Combine(engReferenceDir, "README.md"));

            foreach (var table in EngineCatalog.Tables)
            {
                Assert.True(
                    EngineCatalog.TableColumns.TryGetValue(table, out var columns) && columns.Count > 0,
                    $"eng.{table} is registered without column metadata.");

                var page = Path.Combine(engReferenceDir, table.Replace('_', '-') + ".md");
                Assert.True(File.Exists(page), $"eng.{table} is registered without its exact reference page: {page}");
                Assert.Contains($"eng.{table}", File.ReadAllText(page), StringComparison.OrdinalIgnoreCase);
                Assert.Contains($"eng.{table}", readme, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string PascalToKeywords(string typeName)
        {
            var name = typeName;
            if (name.EndsWith("Statement"))
            {
                name = name.Substring(0, name.Length - "Statement".Length);
            }
            name = name.Replace("Portal", "");

            var result = "";
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                {
                    result += " ";
                }
                result += name[i];
            }
            return result.ToUpperInvariant();
        }
    }
}
