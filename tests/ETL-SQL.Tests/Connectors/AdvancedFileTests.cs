using System;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Xunit;

namespace ETL_SQL.Tests.Connectors
{
    [Trait("Connector", "FILE")]
    [Trait("CertificationClass", "LocalRealIntegration")]
    public class AdvancedFileTests
    {


        [Fact]
        public async Task TestDirectoryConnection()
        {
            string testDir = "TestDir_Conn";
            if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
            Directory.CreateDirectory(testDir);
            await File.WriteAllTextAsync(Path.Combine(testDir, "file1.txt"), "hello");

            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await eval.Evaluate(new Lexer($"CREATE CONNECTION MyDir AS DIRECTORY('{testDir}');").TokenizeToScript());
            await eval.Evaluate(new Lexer("SELECT * FROM MyDir;").TokenizeToScript());

            Assert.NotNull(eval.LastResult);
            Assert.NotEmpty(eval.LastResult.Rows);
            Assert.Equal("file1.txt", eval.LastResult.Rows[0]["FileName"]?.ToString());

            Directory.Delete(testDir, true);
        }



        [Fact]
        public async Task TestConnectionPathResolution()
        {
            string dir = "ConnPath_Test";
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "test.csv");
            await File.WriteAllTextAsync(file, "id\n1");

            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await eval.Evaluate(new Lexer($"CREATE CONNECTION MyDir AS DIRECTORY('{dir}');").TokenizeToScript());
            await eval.Evaluate(new Lexer($"CREATE CONNECTION MyFile AS FLATFILE('{file}');").TokenizeToScript());

            await eval.Evaluate(new Lexer("CREATE DIRECTORY MyDir + '/subdir';").TokenizeToScript());
            Assert.True(Directory.Exists(Path.Combine(dir, "subdir")), "Path resolution for DIRECTORY connection failed");

            await eval.Evaluate(new Lexer("COPY FILE MyFile TO MyDir + '/test_copy.csv';").TokenizeToScript());
            Assert.True(File.Exists(Path.Combine(dir, "test_copy.csv")), "Path resolution for FILE connection failed");

            Directory.Delete(dir, true);
        }

        [Fact]
        public async Task TestListInteraction()
        {
            string dir = "ListInteraction_Test";
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);

            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string script = $@"
                DECLARE @files LIST = ['{dir.Replace("\\", "/")}/f1', '{dir.Replace("\\", "/")}/f2'];
                FOREACH @f IN @files
                BEGIN
                    CREATE DIRECTORY @f;
                END
            ";
            await eval.Evaluate(new Lexer(script).TokenizeToScript());

            Assert.True(Directory.Exists(Path.Combine(dir, "f1")), "f1 not created via LIST");
            Assert.True(Directory.Exists(Path.Combine(dir, "f2")), "f2 not created via LIST");

            Directory.Delete(dir, true);
        }

        [Fact]
        public async Task TestCompressionAndEncryption()
        {
            string src = "compress_test.txt";
            string zip = "compress_test.zip";
            string enc = "encrypt_test.enc";
            string dec = "decrypt_test.txt";

            if (File.Exists(src)) File.Delete(src);
            if (File.Exists(zip)) File.Delete(zip);
            if (File.Exists(enc)) File.Delete(enc);
            if (File.Exists(dec)) File.Delete(dec);

            await File.WriteAllTextAsync(src, "secret info");

            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

            await eval.Evaluate(new Lexer($"COMPRESS FILE '{src}' TO '{zip}';").TokenizeToScript());
            Assert.True(File.Exists(zip), "COMPRESS FILE failed to create zip");

            await eval.Evaluate(new Lexer($"ENCRYPT FILE '{src}' TO '{enc}' PASSWORD 'TestPass1' WITH(OVERWRITE=ON);").TokenizeToScript());
            Assert.True(File.Exists(enc), "ENCRYPT FILE failed to create enc");

            await eval.Evaluate(new Lexer($"DECRYPT FILE '{enc}' TO '{dec}' PASSWORD 'TestPass1' WITH(OVERWRITE=ON);").TokenizeToScript());
            Assert.True(File.Exists(dec), "DECRYPT FILE failed to create dec");

            string content = await File.ReadAllTextAsync(dec);
            Assert.Equal("secret info", content);

            File.Delete(src);
            File.Delete(zip);
            File.Delete(enc);
            File.Delete(dec);
        }

        [Fact]
        public async Task TestSecureConnections()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string suffix = Guid.NewGuid().ToString("N");
            string csv = $"secure_test_{suffix}.csv";
            string encCsv = $"secure_test_{suffix}_enc.csv";
            string zipCsv = $"secure_test_{suffix}.zip";

            try
            {
                await File.WriteAllTextAsync(csv, "id,val\n1,secret");
                await eval.Evaluate(new Lexer($"CREATE CONNECTION RawCsv AS FLATFILE('{csv}');").TokenizeToScript());
                await eval.Evaluate(new Lexer($"CREATE CONNECTION EncryptedCsv AS FLATFILE('{encCsv}', ENCRYPT='ON', PASSWORD='MyPass123');").TokenizeToScript());

                await eval.Evaluate(new Lexer("INSERT INTO EncryptedCsv SELECT * FROM RawCsv;").TokenizeToScript());
                string resolvedEncCsv = eval.ResolvePath(encCsv) + ".pgp";
                Assert.True(File.Exists(resolvedEncCsv), "Encrypted file not created");

                string rawContent = await File.ReadAllTextAsync(resolvedEncCsv);
                Assert.DoesNotContain("secret", rawContent);

                await eval.Evaluate(new Lexer("SELECT * FROM EncryptedCsv;").TokenizeToScript());
                Assert.Equal("secret", eval.LastResult?.Rows[0]["val"]?.ToString());

                await eval.Evaluate(new Lexer($"CREATE CONNECTION CompressedCsv AS FLATFILE('{zipCsv}', COMPRESS='ON');").TokenizeToScript());
                await eval.Evaluate(new Lexer("INSERT INTO CompressedCsv SELECT * FROM RawCsv;").TokenizeToScript());
                Assert.True(File.Exists(zipCsv), "Compressed file not created");

                await eval.Evaluate(new Lexer("SELECT * FROM CompressedCsv;").TokenizeToScript());
                Assert.Equal("secret", eval.LastResult?.Rows[0]["val"]?.ToString());
            }
            finally
            {
                File.Delete(csv);
                File.Delete(encCsv);
                File.Delete(eval.ResolvePath(encCsv) + ".pgp");
                File.Delete(zipCsv);
            }
        }
    }
}
