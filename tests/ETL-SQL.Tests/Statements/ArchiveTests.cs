using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Data;
using ETL_SQL.Engine.Services;
using ETL_SQL.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements;

public sealed class ArchiveTests : IDisposable
{
    private readonly string _root;

    public ArchiveTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"etlsql_archive_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string P(string relative) => Path.Combine(_root, relative);
    private string Sql(string relative) => P(relative).Replace("'", "''");

    [Fact]
    public void TarLimitsApplyToListingAndPatternExcludedEntries()
    {
        var archivePath = P("large.tar.gz");
        using (var file = File.Create(archivePath))
        using (var gzip = new GZipStream(file, CompressionMode.Compress))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Pax))
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "excluded.csv")
            {
                DataStream = new MemoryStream(new byte[32 * 1024])
            });
        }

        var limits = new ArchiveExtractionLimits(MaxEntryBytes: 1024, MaxTotalBytes: 2048);
        Assert.Throws<InvalidDataException>(() => SafeArchiveService.ListEntries(
            archivePath, ArchiveFormat.TarGz, default, limits));

        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.SecurityService.IsTestMode = false;
        evaluator.SecurityService.ApprovedSafeZones.Add(_root);
        var authorizer = new FileSystemPolicyAuthorizer(evaluator.SecurityService);
        Assert.Throws<InvalidDataException>(() => SafeArchiveService.ExtractArchive(
            archivePath, P("selected"), ArchiveFormat.TarGz, false, evaluator, authorizer,
            pattern: "*.json", limits: limits));
        Assert.False(File.Exists(P("selected/excluded.csv")));
    }

    private static async Task<Evaluator> RunAsync(string script)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        var parsed = new Parser(new Lexer(script).Tokenize(), script).Parse();
        Assert.Empty(parsed.Diagnostics);
        await evaluator.Evaluate(parsed);
        return evaluator;
    }

    [Theory]
    [InlineData("ZIP", "data.zip")]
    [InlineData("TAR", "data.tar")]
    [InlineData("TAR_GZ", "data.tar.gz")]
    public async Task CompressAndDecompressFile_MultiEntryFormats_RoundTrip(string format, string archiveName)
    {
        var src = P("orders.csv");
        await File.WriteAllTextAsync(src, "id,amount\n1,100\n2,200\n");

        await RunAsync($@"
COMPRESS FILE '{Sql("orders.csv")}' TO '{Sql(archiveName)}' WITH (FORMAT = '{format}');
DECOMPRESS FILE '{Sql(archiveName)}' TO '{Sql("out_" + format)}';
");

        var extracted = Path.Combine(P("out_" + format), "orders.csv");
        Assert.True(File.Exists(extracted));
        Assert.Equal("id,amount\n1,100\n2,200\n", await File.ReadAllTextAsync(extracted));
    }

    [Fact]
    public async Task CompressAndDecompressFile_Gzip_AndOptionalToDefaults_Work()
    {
        var src = P("events.csv");
        await File.WriteAllTextAsync(src, "event_id,kind\n10,click\n");

        // COMPRESS FILE without TO with FORMAT = 'GZIP' defaults to events.csv.gz
        await RunAsync($"COMPRESS FILE '{Sql("events.csv")}' WITH (FORMAT = 'GZIP');");
        Assert.True(File.Exists(P("events.csv.gz")));

        // Delete original and DECOMPRESS FILE without TO -> restores events.csv
        File.Delete(src);
        await RunAsync($"DECOMPRESS FILE '{Sql("events.csv.gz")}';");
        Assert.True(File.Exists(src));
        Assert.Equal("event_id,kind\n10,click\n", await File.ReadAllTextAsync(src));

        // COMPRESS FILE without TO and no FORMAT defaults to .zip
        await RunAsync($"COMPRESS FILE '{Sql("events.csv")}';");
        Assert.True(File.Exists(P("events.csv.zip")));

        // DECOMPRESS FILE 'x.zip' without TO strips .zip in the same directory
        File.Delete(src);
        await RunAsync($"DECOMPRESS FILE '{Sql("events.csv.zip")}';");
        Assert.True(File.Exists(Path.Combine(P("events.csv"), "events.csv")));
    }

    [Fact]
    public async Task DecompressFile_DefaultDestinationStripsZipAndTarGzSuffixes()
    {
        var dir = P("bundle");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "a.csv"), "col\n1\n");

        await RunAsync($@"
COMPRESS DIRECTORY '{Sql("bundle")}' WITH (FORMAT = 'TAR_GZ');
COMPRESS DIRECTORY '{Sql("bundle")}';
");
        Assert.True(File.Exists(P("bundle.tar.gz")));
        Assert.True(File.Exists(P("bundle.zip")));

        Directory.Delete(dir, recursive: true);

        await RunAsync($"DECOMPRESS FILE '{Sql("bundle.tar.gz")}';");
        Assert.True(File.Exists(Path.Combine(P("bundle"), "a.csv")));

        Directory.Delete(dir, recursive: true);

        await RunAsync($"DECOMPRESS FILE '{Sql("bundle.zip")}';");
        Assert.True(File.Exists(Path.Combine(P("bundle"), "a.csv")));
    }

    [Fact]
    public async Task CompressAndDecompressDirectory_WithPatternAndOverwrite_Works()
    {
        var srcDir = P("tree");
        Directory.CreateDirectory(Path.Combine(srcDir, "sub"));
        await File.WriteAllTextAsync(Path.Combine(srcDir, "keep.csv"), "a,b\n1,2\n");
        await File.WriteAllTextAsync(Path.Combine(srcDir, "skip.txt"), "ignore");
        await File.WriteAllTextAsync(Path.Combine(srcDir, "sub", "nested.csv"), "c,d\n3,4\n");

        await RunAsync($@"
COMPRESS DIRECTORY '{Sql("tree")}' TO '{Sql("tree.tgz")}';
DECOMPRESS DIRECTORY '{Sql("tree.tgz")}' TO '{Sql("selective")}' WITH (PATTERN = '*.csv', OVERWRITE = ON);
");

        Assert.True(File.Exists(Path.Combine(P("selective"), "keep.csv")));
        Assert.True(File.Exists(Path.Combine(P("selective"), "sub", "nested.csv")));
        Assert.False(File.Exists(Path.Combine(P("selective"), "skip.txt")));

        await Assert.ThrowsAsync<ExecutionException>(() => RunAsync(
            $"DECOMPRESS DIRECTORY '{Sql("tree.tgz")}' TO '{Sql("selective")}' WITH (PATTERN = '*.csv', OVERWRITE = OFF);"));
    }

    [Fact]
    public async Task ArchiveList_InspectsZipTarAndTarGzWithoutExtracting()
    {
        var srcDir = P("inspect_src");
        Directory.CreateDirectory(Path.Combine(srcDir, "nested"));
        await File.WriteAllTextAsync(Path.Combine(srcDir, "alpha.csv"), "hello world");
        await File.WriteAllTextAsync(Path.Combine(srcDir, "nested", "beta.json"), "{\"ok\":true}");

        await RunAsync($@"
COMPRESS DIRECTORY '{Sql("inspect_src")}' TO '{Sql("inspect.zip")}';
COMPRESS DIRECTORY '{Sql("inspect_src")}' TO '{Sql("inspect.tar")}';
COMPRESS DIRECTORY '{Sql("inspect_src")}' TO '{Sql("inspect.tar.gz")}';
");

        var evaluator = await RunAsync($@"
SELECT Name, FullPath, Size, CompressedSize, LastModified, IsDirectory INTO #zip_list FROM ARCHIVE_LIST('{Sql("inspect.zip")}') ORDER BY FullPath;
SELECT Name, FullPath, Size, CompressedSize, LastModified, IsDirectory INTO #tar_list FROM ARCHIVE_LIST('{Sql("inspect.tar")}') ORDER BY FullPath;
SELECT Name, FullPath, Size, CompressedSize, LastModified, IsDirectory INTO #tgz_list FROM ARCHIVE_LIST('{Sql("inspect.tar.gz")}') ORDER BY FullPath;
");

        foreach (var tbl in new[] { "#zip_list", "#tar_list", "#tgz_list" })
        {
            var ds = evaluator.Connections[tbl] as InMemoryDataSource;
            Assert.NotNull(ds);
            var rows = (await ds.ReadBatches().ToListAsync()).SelectMany(b => b.Rows).ToList();
            Assert.Contains(rows, r => r["Name"]?.ToString() == "alpha.csv" && !(bool)r["IsDirectory"]! && Convert.ToDecimal(r["Size"]) == 11m);
            Assert.Contains(rows, r => r["FullPath"]?.ToString() == "nested/beta.json" && !(bool)r["IsDirectory"]!);
        }

        // Verify nothing was extracted to disk during ARCHIVE_LIST
        Assert.False(Directory.Exists(P("inspect")));
    }

    [Fact]
    public void SafeArchiveService_RejectsTarTraversalAndSymlinkAndDecompressionBomb()
    {
        var security = new SecurityService(NullLogger.Instance)
        {
            IsTestMode = false,
            ProtectionMode = PathProtectionMode.Defined
        };
        security.ApprovedSafeZones.Add(_root);
        var authorizer = new FileSystemPolicyAuthorizer(security);
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.SecurityService.IsTestMode = false;
        evaluator.SecurityService.ApprovedSafeZones.Add(_root);

        // 1. Path traversal in TAR
        var traversalTar = P("traversal.tar");
        using (var fs = File.Create(traversalTar))
        using (var writer = new TarWriter(fs, TarEntryFormat.Pax))
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "../escape.csv")
            {
                DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("bad"))
            };
            writer.WriteEntry(entry);
        }

        Assert.Throws<FileSystemPolicyDeniedException>(() =>
            SafeArchiveService.ExtractArchive(traversalTar, P("out_trav"), ArchiveFormat.Tar, true, evaluator, authorizer));
        Assert.False(File.Exists(P("escape.csv")));

        // 2. Symlink in TAR
        var symlinkTar = P("symlink.tar");
        using (var fs = File.Create(symlinkTar))
        using (var writer = new TarWriter(fs, TarEntryFormat.Pax))
        {
            var entry = new PaxTarEntry(TarEntryType.SymbolicLink, "link.txt")
            {
                LinkName = "/etc/passwd"
            };
            writer.WriteEntry(entry);
        }

        Assert.Throws<InvalidDataException>(() =>
            SafeArchiveService.ExtractArchive(symlinkTar, P("out_sym"), ArchiveFormat.Tar, true, evaluator, authorizer));

        // 3. Decompression ratio / size bomb bound
        var bombGz = P("bomb.txt.gz");
        using (var fs = File.Create(bombGz))
        using (var gz = new GZipStream(fs, CompressionMode.Compress))
        {
            gz.Write(new byte[32 * 1024]);
        }

        var tinyLimits = new ArchiveExtractionLimits(MaxEntries: 10, MaxEntryBytes: 1024, MaxTotalBytes: 1024, MaxCompressionRatio: 5, RatioCheckMinBytes: 512);
        Assert.Throws<InvalidDataException>(() =>
            SafeArchiveService.ExtractArchive(bombGz, P("bomb.txt"), ArchiveFormat.Gzip, true, evaluator, authorizer,
                destinationIsFileForGzip: true, limits: tinyLimits));
        Assert.False(File.Exists(P("bomb.txt")));
    }

    [Fact]
    public async Task ArchiveStatements_RoundTripThroughAstSerializer()
    {
        const string sql = "COMPRESS FILE 'a.csv' WITH(FORMAT='GZIP'); DECOMPRESS FILE 'a.tar.gz' TO 'out' WITH(OVERWRITE=OFF, PATTERN='*.csv');";
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var formatted = script.ToSql();
        var reparsed = new Parser(new Lexer(formatted).Tokenize(), formatted).Parse();
        Assert.Empty(reparsed.Diagnostics);
        await Task.CompletedTask;
    }
}
