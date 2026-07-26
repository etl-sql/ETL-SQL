using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using ETL_SQL.Common;
using ETL_SQL.Connectors.S3;
using ETL_SQL.Connectors.Sqlite;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Quality;
using ETL_SQL.Data;
using ETL_SQL.Services;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Connectors
{
    [Trait("Category", "Connectors")]
    public class SqliteAndS3ConnectorTests
    {
        private Mock<IExecutionContext> CreateMockContext()
        {
            var mockLogger = new Mock<ILogger>();
            var securityService = new SecurityService(mockLogger.Object);
            var mockContext = new Mock<IExecutionContext>();
            mockContext.Setup(c => c.SecurityService).Returns(securityService);
            mockContext.Setup(c => c.Logger).Returns(mockLogger.Object);
            mockContext.Setup(c => c.ResolvePath(It.IsAny<string>())).Returns<string>(p => p);
            return mockContext;
        }

        // ── SQLite Connector Tests ───────────────────────────────────────────

        [Fact]
        public void SqliteConnector_Metadata_IsCorrect()
        {
            var connector = new SqliteConnector();
            Assert.Equal("SQLITE", connector.Name);
            Assert.Contains("SQLITE3", connector.Aliases);
            Assert.False(connector.IsFileBased);
            Assert.NotEmpty(connector.GetHelp());
            Assert.NotEmpty(connector.GetSupportedFunctions());
            Assert.NotEmpty(connector.GetSupportedKeywords());
            Assert.NotEmpty(connector.GetSupportedOptions());
            Assert.Contains("DATABASE", connector.GetSupportedOptions().Keys);
            Assert.DoesNotContain("PATH", connector.GetSupportedOptions().Keys);
            Assert.DoesNotContain("PASSWORD", connector.GetSupportedOptions().Keys);
        }

        [Fact]
        public void SqliteConnector_CreateDataSource_ResolvesPath()
        {
            var mockContext = CreateMockContext();
            string rawPath = "subfolder/mydb.db";
            string resolvedPath = "C:\\Absolute\\subfolder\\mydb.db";

            mockContext.Setup(c => c.ResolvePath(rawPath)).Returns(resolvedPath);

            var connector = new SqliteConnector();
            var options = new Dictionary<string, string> { { "DATABASE", rawPath } };
            string connStr = connector.BuildConnectionString(options);
            var dataSource = (SqliteDataSource)connector.CreateDataSource(mockContext.Object, connStr, options);

            mockContext.Verify(c => c.ResolvePath(rawPath), Times.Once);
            Assert.Contains(resolvedPath, dataSource.ConnectionString);
        }

        [Fact]
        public void SqliteConnector_CreateDataSource_InMemory_NoPathResolution()
        {
            var mockContext = CreateMockContext();
            var connector = new SqliteConnector();
            var options = new Dictionary<string, string> { { "DATABASE", ":memory:" } };
            string connStr = connector.BuildConnectionString(options);
            var dataSource = (SqliteDataSource)connector.CreateDataSource(mockContext.Object, connStr, options);

            mockContext.Verify(c => c.ResolvePath(It.IsAny<string>()), Times.Never);
            Assert.Contains(":memory:", dataSource.ConnectionString);
        }

        [Fact]
        public void SqliteConnector_SharedMemoryMode_DoesNotResolveDatabaseNameAsPath()
        {
            var mockContext = CreateMockContext();

            var dataSource = (SqliteDataSource)new SqliteConnector().CreateDataSource(
                mockContext.Object, "Data Source=shared-cache;Mode=Memory;Cache=Shared;");

            mockContext.Verify(c => c.ResolvePath(It.IsAny<string>()), Times.Never);
            Assert.Contains("Mode=Memory", dataSource.ConnectionString);
        }

        [Fact]
        public void SqliteConnector_RawPassword_IsRejectedWithoutSqlCipher()
        {
            var mockContext = CreateMockContext();

            var error = Assert.Throws<ExecutionException>(() =>
                new SqliteConnector().CreateDataSource(
                    mockContext.Object, "Data Source=:memory:;Password=not-encryption;"));

            Assert.Contains("does not ship SQLCipher", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("not-encryption", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void SqliteConnector_RawConnectionString_ResolvesOnlyDataSourcePath()
        {
            var mockContext = CreateMockContext();
            var rawPath = "data/read-only.db";
            var resolvedPath = Path.Combine(Path.GetTempPath(), "data", "read-only.db");
            mockContext.Setup(c => c.ResolvePath(rawPath)).Returns(resolvedPath);

            var dataSource = (SqliteDataSource)new SqliteConnector().CreateDataSource(
                mockContext.Object, $"Data Source={rawPath};Mode=ReadOnly;");

            mockContext.Verify(c => c.ResolvePath(rawPath), Times.Once);
            Assert.Contains(resolvedPath, dataSource.ConnectionString);
            Assert.Contains("Mode=ReadOnly", dataSource.ConnectionString);
        }

        [Fact]
        public void SqliteConnector_ProtectedPathFailure_IsAnExecutionException()
        {
            var mockContext = CreateMockContext();
            mockContext.Setup(c => c.ResolvePath(It.IsAny<string>()))
                .Throws(new ExecutionException("Path is outside approved roots."));

            var error = Assert.Throws<ExecutionException>(() =>
                new SqliteConnector().CreateDataSource(
                    mockContext.Object, "Data Source=C:\\Windows\\System32\\blocked.db;"));

            Assert.Contains("approved roots", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task SqliteDataSource_ConnectionOpenFailure_IsSanitized()
        {
            var mockContext = CreateMockContext();
            var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.db");
            mockContext.Setup(c => c.ResolvePath(missing)).Returns(missing);
            var dataSource = new SqliteDataSource(
                mockContext.Object, $"Data Source={missing};Mode=ReadOnly;");

            var error = await Assert.ThrowsAsync<ExecutionException>(() => dataSource.GetTablesAsync());

            Assert.DoesNotContain("Data Source=", error.Message, StringComparison.OrdinalIgnoreCase);
            await dataSource.DisposeAsync();
        }

        [Fact]
        public async Task SqliteDataSource_DisposingTableView_DoesNotOwnRootTransaction()
        {
            var mockContext = CreateMockContext();
            var options = new Dictionary<string, string> { ["DATABASE"] = ":memory:" };
            var connector = new SqliteConnector();
            var root = (SqliteDataSource)connector.CreateDataSource(
                mockContext.Object, connector.BuildConnectionString(options), options);

            await root.BeginTransactionAsync();
            await foreach (var _ in root.ExecuteRawSql("CREATE TABLE items (id INTEGER)")) { }
            var tableView = (SqliteDataSource)root.WithTable("items");
            await tableView.DisposeAsync();
            await foreach (var _ in root.ExecuteRawSql("INSERT INTO items (id) VALUES (1)")) { }

            var count = 0L;
            await foreach (var batch in root.ExecuteRawSql("SELECT COUNT(*) AS count FROM items"))
                count = Convert.ToInt64(batch.Rows[0]["count"]);

            Assert.Equal(1, count);
            await root.RollbackAsync();
            await root.DisposeAsync();
            await root.DisposeAsync();
        }

        [Fact]
        public async Task SqliteDataSource_InMemory_ReadWrite_Success()
        {
            var mockContext = CreateMockContext();
            var connector = new SqliteConnector();
            var options = new Dictionary<string, string> { { "DATABASE", ":memory:" } };
            string connStr = connector.BuildConnectionString(options);
            var dataSource = (SqliteDataSource)connector.CreateDataSource(mockContext.Object, connStr, options);

            var sessionSource = (SqliteDataSource)dataSource.WithTable("users");

            try
            {
                // Root and table views share transaction state without sharing disposal ownership.
                await dataSource.BeginTransactionAsync();
                await foreach (var _ in dataSource.ExecuteRawSql(
                    "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, role TEXT)")) { }

                // Write batches
                var table = new DataTable();
                table.SetColumns(new[] { "id", "name", "role" });
                await table.AddRowAsync(new Row { ["id"] = 1L, ["name"] = "Alice", ["role"] = "Admin" });
                await table.AddRowAsync(new Row { ["id"] = 2L, ["name"] = "Bob", ["role"] = "User" });

                async IAsyncEnumerable<DataTable> GetBatches()
                {
                    yield return table;
                    await Task.CompletedTask;
                }

                await sessionSource.WriteBatches(GetBatches(), append: true);
                await dataSource.CommitAsync();

                // To read the in-memory data, we must start another transaction or keep connection open,
                // because once CommitAsync finishes, the transactional connection is closed and database is destroyed.
                // Let's verify that a new session starts clean (empty / table doesn't exist).
                var verifySource = (SqliteDataSource)dataSource.WithTable("users");
                var ex = await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(async () =>
                {
                    await foreach (var batch in verifySource.ReadBatches()) { }
                });
                Assert.Contains("no such table", ex.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                await sessionSource.DisposeAsync();
                await dataSource.DisposeAsync();
            }
        }


        [Fact]
        public async Task SqliteDataSource_ExecuteNonQueryAndReader_Success()
        {
            var mockContext = CreateMockContext();
            var tempDb = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.db");
            mockContext.Setup(c => c.ResolvePath(tempDb)).Returns(tempDb);

            var connectionString = $"Data Source={tempDb}";
            var dataSource = new SqliteDataSource(mockContext.Object, connectionString);
            SqliteDataSource? writeSource = null;
            SqliteDataSource? readSource = null;

            try
            {
                // Create a table using normal SQLite connection
                using (var conn = new Microsoft.Data.Sqlite.SqliteConnection(connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new Microsoft.Data.Sqlite.SqliteCommand("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, role TEXT)", conn))
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                // Write batches
                var table = new DataTable();
                table.SetColumns(new[] { "id", "name", "role" });
                await table.AddRowAsync(new Row { ["id"] = 1L, ["name"] = "Alice", ["role"] = "Admin" });
                await table.AddRowAsync(new Row { ["id"] = 2L, ["name"] = "Bob", ["role"] = "User" });

                async IAsyncEnumerable<DataTable> GetBatches()
                {
                    yield return table;
                    await Task.CompletedTask;
                }

                writeSource = (SqliteDataSource)dataSource.WithTable("users");
                await writeSource.WriteBatches(GetBatches(), append: true);

                // Read batches
                readSource = (SqliteDataSource)dataSource.WithTable("users");
                var batches = new List<DataTable>();
                await foreach (var batch in readSource.ReadBatches())
                {
                    batches.Add(batch);
                }

                Assert.Single(batches);
                Assert.Equal(2, batches[0].Rows.Count);
                Assert.Equal("Alice", batches[0].Rows[0]["name"]);
                Assert.Equal("Bob", batches[0].Rows[1]["name"]);
            }
            finally
            {
                if (writeSource != null) await writeSource.DisposeAsync();
                if (readSource != null) await readSource.DisposeAsync();
                await dataSource.DisposeAsync();

                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (File.Exists(tempDb))
                {
                    File.Delete(tempDb);
                }
            }
        }

        [Fact]
        public async Task SqliteDataSource_DataQualityRetentionPrunesOldRows()
        {
            var mockContext = CreateMockContext();
            var tempDb = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.db");
            mockContext.Setup(c => c.ResolvePath(tempDb)).Returns(tempDb);

            var connectionString = $"Data Source={tempDb}";
            var dataSource = new SqliteDataSource(mockContext.Object, connectionString);
            SqliteDataSource? writeSource = null;
            SqliteDataSource? readSource = null;

            try
            {
                await foreach (var _ in dataSource.ExecuteRawSql(
                    $"CREATE TABLE dq (id INTEGER, \"{DataQualityColumns.Timestamp}\" TEXT, "
                    + $"\"{DataQualityColumns.CaptureScope}\" TEXT, \"{DataQualityColumns.Status}\" TEXT)")) { }

                var table = new DataTable();
                table.SetColumns(new[]
                {
                    "id",
                    DataQualityColumns.Timestamp,
                    DataQualityColumns.CaptureScope,
                    DataQualityColumns.Status
                });
                await table.AddRowAsync(new Row
                {
                    ["id"] = 1L,
                    [DataQualityColumns.Timestamp] = DateTime.UtcNow.AddDays(-45),
                    [DataQualityColumns.CaptureScope] = "job:a",
                    [DataQualityColumns.Status] = DataQualityColumns.ReplayedStatus
                });
                await table.AddRowAsync(new Row
                {
                    ["id"] = 2L,
                    [DataQualityColumns.Timestamp] = DateTime.UtcNow,
                    [DataQualityColumns.CaptureScope] = "job:a",
                    [DataQualityColumns.Status] = DataQualityColumns.ReplayedStatus
                });
                await table.AddRowAsync(new Row
                {
                    ["id"] = 3L,
                    [DataQualityColumns.Timestamp] = DateTime.UtcNow.AddDays(-45),
                    [DataQualityColumns.CaptureScope] = "job:b",
                    [DataQualityColumns.Status] = DataQualityColumns.ReplayedStatus
                });
                await table.AddRowAsync(new Row
                {
                    ["id"] = 4L,
                    [DataQualityColumns.Timestamp] = DateTime.UtcNow.AddDays(-45),
                    [DataQualityColumns.CaptureScope] = "job:a",
                    [DataQualityColumns.Status] = DataQualityColumns.QuarantinedStatus
                });

                async IAsyncEnumerable<DataTable> GetBatches()
                {
                    yield return table;
                    await Task.CompletedTask;
                }

                writeSource = (SqliteDataSource)dataSource.WithTable("dq");
                await writeSource.WriteBatches(GetBatches(), append: true);

                var pruned = await writeSource.PruneDataQualityRowsAsync(
                    DataQualityColumns.Timestamp,
                    DateTime.UtcNow.AddDays(-30),
                    DataQualityColumns.CaptureScope,
                    "job:a",
                    CancellationToken.None);

                readSource = (SqliteDataSource)dataSource.WithTable("dq");
                var ids = new List<long>();
                await foreach (var batch in readSource.ReadBatches())
                    ids.AddRange(batch.Rows.Select(r => Convert.ToInt64(r["id"])));

                Assert.Equal(1, pruned);
                Assert.Equal(new[] { 2L, 3L, 4L }, ids);
            }
            finally
            {
                if (writeSource != null) await writeSource.DisposeAsync();
                if (readSource != null) await readSource.DisposeAsync();
                await dataSource.DisposeAsync();

                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (File.Exists(tempDb))
                {
                    File.Delete(tempDb);
                }
            }
        }

        [Fact]
        public async Task SqliteDataSource_Transactions_Commit()
        {
            var mockContext = CreateMockContext();
            var tempDb = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.db");
            mockContext.Setup(c => c.ResolvePath(tempDb)).Returns(tempDb);

            var connectionString = $"Data Source={tempDb}";
            var dataSource = new SqliteDataSource(mockContext.Object, connectionString);
            SqliteDataSource? txSource = null;
            SqliteDataSource? readSource = null;

            try
            {
                using (var conn = new Microsoft.Data.Sqlite.SqliteConnection(connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new Microsoft.Data.Sqlite.SqliteCommand("CREATE TABLE logs (msg TEXT)", conn))
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                txSource = (SqliteDataSource)dataSource.WithTable("logs");

                await txSource.BeginTransactionAsync();

                var table = new DataTable();
                table.SetColumns(new[] { "msg" });
                await table.AddRowAsync(new Row { ["msg"] = "Log entry 1" });

                async IAsyncEnumerable<DataTable> GetBatches()
                {
                    yield return table;
                    await Task.CompletedTask;
                }

                await txSource.WriteBatches(GetBatches(), append: true);
                await txSource.CommitAsync();

                // Verify written
                readSource = (SqliteDataSource)dataSource.WithTable("logs");
                var batches = new List<DataTable>();
                await foreach (var batch in readSource.ReadBatches())
                {
                    batches.Add(batch);
                }

                Assert.Single(batches);
                Assert.Equal("Log entry 1", batches[0].Rows[0]["msg"]);
            }
            finally
            {
                if (txSource != null) await txSource.DisposeAsync();
                if (readSource != null) await readSource.DisposeAsync();
                await dataSource.DisposeAsync();

                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (File.Exists(tempDb))
                {
                    File.Delete(tempDb);
                }
            }
        }

        [Fact]
        public async Task SqliteDataSource_Transactions_Rollback()
        {
            var mockContext = CreateMockContext();
            var tempDb = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.db");
            mockContext.Setup(c => c.ResolvePath(tempDb)).Returns(tempDb);

            var connectionString = $"Data Source={tempDb}";
            var dataSource = new SqliteDataSource(mockContext.Object, connectionString);
            SqliteDataSource? txSource = null;
            SqliteDataSource? readSource = null;

            try
            {
                using (var conn = new Microsoft.Data.Sqlite.SqliteConnection(connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new Microsoft.Data.Sqlite.SqliteCommand("CREATE TABLE logs (msg TEXT)", conn))
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                txSource = (SqliteDataSource)dataSource.WithTable("logs");

                await txSource.BeginTransactionAsync();

                var table = new DataTable();
                table.SetColumns(new[] { "msg" });
                await table.AddRowAsync(new Row { ["msg"] = "Rollback entry" });

                async IAsyncEnumerable<DataTable> GetBatches()
                {
                    yield return table;
                    await Task.CompletedTask;
                }

                await txSource.WriteBatches(GetBatches(), append: true);
                await txSource.RollbackAsync();

                // Verify empty
                readSource = (SqliteDataSource)dataSource.WithTable("logs");
                var batches = new List<DataTable>();
                await foreach (var batch in readSource.ReadBatches())
                {
                    batches.Add(batch);
                }

                Assert.Empty(batches);
            }
            finally
            {
                if (txSource != null) await txSource.DisposeAsync();
                if (readSource != null) await readSource.DisposeAsync();
                await dataSource.DisposeAsync();

                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (File.Exists(tempDb))
                {
                    File.Delete(tempDb);
                }
            }
        }

        // ── S3 Connector Tests ───────────────────────────────────────────────

        [Fact]
        public void S3Connector_Metadata_IsCorrect()
        {
            var connector = new S3Connector();
            Assert.Equal("S3", connector.Name);
            Assert.Contains("AWS_S3", connector.Aliases);
            Assert.NotEmpty(connector.GetHelp());
            Assert.NotEmpty(connector.GetSupportedOptions());
            Assert.DoesNotContain("ACCESSKEY", connector.GetSupportedOptions().Keys);
            Assert.DoesNotContain("SECRETKEY", connector.GetSupportedOptions().Keys);
        }

        [Fact]
        public void S3Connector_EgressSecurity_ValidatesHost()
        {
            var mockLogger = new Mock<ILogger>();
            var securityService = new SecurityService(mockLogger.Object)
            {
                IsTestMode = false
            };
            securityService.AllowedHosts.Clear();

            var mockContext = new Mock<IExecutionContext>();
            mockContext.Setup(c => c.SecurityService).Returns(securityService);
            mockContext.Setup(c => c.Logger).Returns(mockLogger.Object);

            var options = new Dictionary<string, string>
            {
                { "ENDPOINT", "https://my-custom-r2-endpoint.cloudflare.com" },
                { "BUCKET", "my-bucket" }
            };

            Assert.Throws<ETL_SQL.Services.SecurityException>(() =>
                new S3Connector(mockContext.Object, "my-bucket", options)
            );
        }

        [Fact]
        public async Task S3Connector_UploadFile_Success()
        {
            var mockContext = CreateMockContext();
            var mockS3 = new Mock<IAmazonS3>();

            var localTemp = Path.GetTempFileName();
            File.WriteAllText(localTemp, "file content");

            try
            {
                mockS3.Setup(s => s.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new PutObjectResponse { HttpStatusCode = HttpStatusCode.OK });

                var options = new Dictionary<string, string> { { "BUCKET", "test-bucket" } };
                var connector = new S3Connector(mockContext.Object, "test-bucket", options, mockS3.Object);

                await connector.UploadFileAsync(localTemp, "remote/path.txt");

                mockS3.Verify(s => s.PutObjectAsync(It.Is<PutObjectRequest>(r =>
                    r.BucketName == "test-bucket" &&
                    r.Key == "remote/path.txt" &&
                    r.FilePath == localTemp
                ), It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                if (File.Exists(localTemp)) File.Delete(localTemp);
            }
        }

        [Fact]
        public async Task S3Connector_DownloadFile_Success()
        {
            var mockContext = CreateMockContext();
            var mockS3 = new Mock<IAmazonS3>();

            var localTemp = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");

            try
            {
                var responseStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("s3 content"));
                var getResponse = new GetObjectResponse
                {
                    ResponseStream = responseStream,
                    HttpStatusCode = HttpStatusCode.OK
                };

                mockS3.Setup(s => s.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(getResponse);

                var options = new Dictionary<string, string> { { "BUCKET", "test-bucket" } };
                var connector = new S3Connector(mockContext.Object, "test-bucket", options, mockS3.Object);

                await connector.DownloadFileAsync("remote/path.txt", localTemp);

                Assert.True(File.Exists(localTemp));
                Assert.Equal("s3 content", File.ReadAllText(localTemp));

                mockS3.Verify(s => s.GetObjectAsync(It.Is<GetObjectRequest>(r =>
                    r.BucketName == "test-bucket" &&
                    r.Key == "remote/path.txt"
                ), It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                if (File.Exists(localTemp)) File.Delete(localTemp);
            }
        }

        [Fact]
        public async Task S3Connector_DeleteFile_Success()
        {
            var mockContext = CreateMockContext();
            var mockS3 = new Mock<IAmazonS3>();

            mockS3.Setup(s => s.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteObjectResponse { HttpStatusCode = HttpStatusCode.NoContent });

            var options = new Dictionary<string, string> { { "BUCKET", "test-bucket" } };
            var connector = new S3Connector(mockContext.Object, "test-bucket", options, mockS3.Object);

            await connector.DeleteFileAsync("remote/file.txt");

            mockS3.Verify(s => s.DeleteObjectAsync(It.Is<DeleteObjectRequest>(r =>
                r.BucketName == "test-bucket" &&
                r.Key == "remote/file.txt"
            ), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task S3Connector_FileExists_Success()
        {
            var mockContext = CreateMockContext();
            var mockS3 = new Mock<IAmazonS3>();

            mockS3.Setup(s => s.GetObjectMetadataAsync("test-bucket", "remote/file.txt", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GetObjectMetadataResponse { HttpStatusCode = HttpStatusCode.OK });

            var options = new Dictionary<string, string> { { "BUCKET", "test-bucket" } };
            var connector = new S3Connector(mockContext.Object, "test-bucket", options, mockS3.Object);

            var exists = await connector.FileExistsAsync("remote/file.txt");

            Assert.True(exists);
        }

        [Fact]
        public async Task S3Connector_DirectoryExists_Success()
        {
            var mockContext = CreateMockContext();
            var mockS3 = new Mock<IAmazonS3>();

            mockS3.Setup(s => s.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ListObjectsV2Response
                {
                    KeyCount = 1,
                    HttpStatusCode = HttpStatusCode.OK
                });

            var options = new Dictionary<string, string> { { "BUCKET", "test-bucket" } };
            var connector = new S3Connector(mockContext.Object, "test-bucket", options, mockS3.Object);

            var exists = await connector.DirectoryExistsAsync("remote/dir");

            Assert.True(exists);
            mockS3.Verify(s => s.ListObjectsV2Async(It.Is<ListObjectsV2Request>(r =>
                r.BucketName == "test-bucket" &&
                r.Prefix == "remote/dir/" &&
                r.MaxKeys == 1
            ), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task S3Connector_RenameFile_Success()
        {
            var mockContext = CreateMockContext();
            var mockS3 = new Mock<IAmazonS3>();

            // Mock FileExistsAsync for checking if file exists (returns false for destination)
            mockS3.Setup(s => s.GetObjectMetadataAsync("test-bucket", "dest.txt", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new AmazonS3Exception("Not Found") { StatusCode = HttpStatusCode.NotFound });

            mockS3.Setup(s => s.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CopyObjectResponse { HttpStatusCode = HttpStatusCode.OK });

            mockS3.Setup(s => s.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteObjectResponse { HttpStatusCode = HttpStatusCode.NoContent });

            var options = new Dictionary<string, string> { { "BUCKET", "test-bucket" } };
            var connector = new S3Connector(mockContext.Object, "test-bucket", options, mockS3.Object);

            await connector.RenameFileAsync("src.txt", "dest.txt");

            mockS3.Verify(s => s.CopyObjectAsync(It.Is<CopyObjectRequest>(r =>
                r.SourceBucket == "test-bucket" &&
                r.SourceKey == "src.txt" &&
                r.DestinationBucket == "test-bucket" &&
                r.DestinationKey == "dest.txt"
            ), It.IsAny<CancellationToken>()), Times.Once);

            mockS3.Verify(s => s.DeleteObjectAsync(It.Is<DeleteObjectRequest>(r =>
                r.BucketName == "test-bucket" &&
                r.Key == "src.txt"
            ), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task S3Connector_DeleteDirectory_Success()
        {
            var mockContext = CreateMockContext();
            var mockS3 = new Mock<IAmazonS3>();

            var listResponse = new ListObjectsV2Response
            {
                HttpStatusCode = HttpStatusCode.OK,
                IsTruncated = false,
                S3Objects = new List<S3Object>
                {
                    new S3Object { Key = "dir/file1.txt" },
                    new S3Object { Key = "dir/sub/file2.txt" }
                }
            };

            mockS3.Setup(s => s.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(listResponse);

            mockS3.Setup(s => s.DeleteObjectsAsync(It.IsAny<DeleteObjectsRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteObjectsResponse { HttpStatusCode = HttpStatusCode.OK });

            var options = new Dictionary<string, string> { { "BUCKET", "test-bucket" } };
            var connector = new S3Connector(mockContext.Object, "test-bucket", options, mockS3.Object);

            await connector.DeleteDirectoryAsync("dir");

            mockS3.Verify(s => s.ListObjectsV2Async(It.Is<ListObjectsV2Request>(r =>
                r.BucketName == "test-bucket" &&
                r.Prefix == "dir/"
            ), It.IsAny<CancellationToken>()), Times.Once);

            mockS3.Verify(s => s.DeleteObjectsAsync(It.Is<DeleteObjectsRequest>(r =>
                r.BucketName == "test-bucket" &&
                r.Objects.Count == 2 &&
                r.Objects.Any(o => o.Key == "dir/file1.txt") &&
                r.Objects.Any(o => o.Key == "dir/sub/file2.txt")
            ), It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
