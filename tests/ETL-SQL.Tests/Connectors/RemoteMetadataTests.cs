using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors;
using ETL_SQL.Connectors.S3;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

[Trait("Category", "Connectors")]
public sealed class RemoteMetadataTests
{
    private static readonly string[] ExpectedColumns =
    [
        "Name", "FullPath", "Size", "LastModified", "IsDirectory",
        "CreationTime", "Permissions", "Owner", "IsSymlink"
    ];

    [Fact]
    public async Task AllRemoteConnectors_PinStandardNineColumnList()
    {
        Assert.Equal(ExpectedColumns, FileMetaData.StandardColumns);

        var sftpCols = (await new SftpConnector().GetColumnsAsync()).ToArray();
        var ftpCols = (await new FtpConnector().GetColumnsAsync()).ToArray();
        var s3Cols = (await new S3Connector().GetColumnsAsync()).ToArray();
        var blobCols = (await new AzureBlobConnector().GetColumnsAsync()).ToArray();

        Assert.Equal(ExpectedColumns, sftpCols);
        Assert.Equal(ExpectedColumns, ftpCols);
        Assert.Equal(ExpectedColumns, s3Cols);
        Assert.Equal(ExpectedColumns, blobCols);
    }

    [Fact]
    public async Task RemoteFileList_ExposesAllNineColumnsAndNullsWhenAbsent()
    {
        var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
        var dsMock = new Mock<IDataSource>();
        var remoteMock = dsMock.As<IRemoteFileSystem>();

        static async IAsyncEnumerable<FileMetaData> FakeFiles()
        {
            yield return new FileMetaData
            {
                Name = "report.csv",
                FullPath = "/in/report.csv",
                Size = 42,
                LastModified = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc),
                IsDirectory = false,
                CreationTime = new DateTime(2026, 11, 22, 10, 0, 0, DateTimeKind.Utc),
                Permissions = "rw-r-----",
                Owner = "1001",
                IsSymlink = false
            };
            yield return new FileMetaData
            {
                Name = "blob.parquet",
                FullPath = "in/blob.parquet",
                Size = 100,
                LastModified = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc),
                IsDirectory = false
            };
            await Task.CompletedTask;
        }

        remoteMock.Setup(r => r.ListFilesAsync("/in")).Returns(FakeFiles());
        ((IExecutionContext)evaluator).Connections["rem_meta"] = dsMock.Object;

        const string sql = "SELECT * INTO #listed FROM REMOTE_FILE_LIST(rem_meta, '/in');";
        await evaluator.Evaluate(new Parser(new Lexer(sql).Tokenize(), sql).Parse());

        var batches = await ((IExecutionContext)evaluator).Connections["#listed"].ReadBatches().ToListAsync();
        var table = Assert.Single(batches);
        Assert.Equal(ExpectedColumns, table.ColumnNames.ToArray());
        Assert.Equal(2, table.Rows.Count);

        Assert.Equal("rw-r-----", table.Rows[0]["Permissions"]);
        Assert.Equal("1001", table.Rows[0]["Owner"]);
        Assert.Equal(false, table.Rows[0]["IsSymlink"]);
        Assert.NotNull(table.Rows[0]["CreationTime"]);

        Assert.Null(table.Rows[1]["CreationTime"]);
        Assert.Null(table.Rows[1]["Permissions"]);
        Assert.Null(table.Rows[1]["Owner"]);
        Assert.Null(table.Rows[1]["IsSymlink"]);
    }
}
