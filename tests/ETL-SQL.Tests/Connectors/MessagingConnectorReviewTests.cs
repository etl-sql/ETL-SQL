using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Confluent.Kafka;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Email;
using ETL_SQL.Connectors.Kafka;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using ETL_SQL.Services;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

/// <summary>Regression tests for the 2026-11-01 review of ETL-SQL.Connectors.Messaging.</summary>
[Trait("CompatBreak", "0.22.0")]
public sealed class MessagingConnectorReviewTests
{
    private static Mock<IExecutionContext> Context()
    {
        var context = new Mock<IExecutionContext>();
        context.SetupGet(c => c.Logger).Returns(NullLogger.Instance);
        context.SetupGet(c => c.SecurityService).Returns(new SecurityService(NullLogger.Instance));
        context.SetupGet(c => c.ServiceProvider).Returns(new Mock<IServiceProvider>().Object);
        context.Setup(c => c.ResolvePath(It.IsAny<string>())).Returns<string>(p => p);
        return context;
    }

    // ── Kafka ────────────────────────────────────────────────────────────────

    [Fact]
    public void Kafka_CreateDataSource_UsesBootstrapServersFromOptions()
    {
        var source = (KafkaDataSource)new KafkaConnector().CreateDataSource(Context().Object, "",
            new Dictionary<string, string> { ["BOOTSTRAP_SERVERS"] = "localhost:9092", ["TOPIC"] = "t" });

        Assert.Equal("localhost:9092", source.BootstrapServers);
    }

    [Fact]
    public async Task Kafka_GetVersion_ValidatesEveryBroker()
    {
        var ex = await Record.ExceptionAsync(() => new KafkaConnector().GetVersionAsync(
            Context().Object, "localhost:9092,169.254.169.254:9092"));

        Assert.IsType<SecurityException>(ex);
    }

    [Fact]
    public void Kafka_BrokerHosts_ListsEveryBrokerIncludingBracketedIpv6()
    {
        Assert.Equal(new[] { "a", "b.example.com", "::1", "c" },
            KafkaConnector.BrokerHosts(" a:9092, b.example.com:9093 ,[::1]:9094,c"));
    }

    [Fact]
    public async Task Kafka_ReadBatches_StoresPartitionAndOffsetAsDecimal()
    {
        var consumer = new Mock<IConsumer<string, string>>();
        var calls = 0;
        consumer.Setup(c => c.Consume(It.IsAny<TimeSpan>())).Returns(() => ++calls == 1
            ? new ConsumeResult<string, string>
            {
                Topic = "t",
                Partition = new Partition(3),
                Offset = new Offset(9_000_000_000L),
                Message = new Message<string, string> { Key = "k", Value = "v", Timestamp = new Timestamp(DateTime.UtcNow) }
            }
            : null!);
        var source = new KafkaDataSource(Context().Object, "localhost:9092", "t",
            new Dictionary<string, string> { ["TIMEOUT_MS"] = "300" }, consumer.Object);

        var rows = new List<Row>();
        await foreach (var batch in source.ReadBatches()) rows.AddRange(batch.Rows);

        var row = Assert.Single(rows);
        Assert.Equal(3m, Assert.IsType<decimal>(row["Partition"]));
        Assert.Equal(9_000_000_000m, Assert.IsType<decimal>(row["Offset"]));
    }

    [Fact]
    public async Task Kafka_DisposingAWithTableClone_LeavesTheParentClientsOpen()
    {
        var consumer = new Mock<IConsumer<string, string>>();
        var producer = new Mock<IProducer<string, string>>();
        var parent = new KafkaDataSource(Context().Object, "localhost:9092", "t", null, consumer.Object, producer.Object);

        await parent.WithTable("other").DisposeAsync();
        consumer.Verify(c => c.Dispose(), Times.Never);
        producer.Verify(p => p.Dispose(), Times.Never);

        await parent.DisposeAsync();
        consumer.Verify(c => c.Dispose(), Times.Once);
        producer.Verify(p => p.Dispose(), Times.Once);
    }

    // ── SMTP ─────────────────────────────────────────────────────────────────

    private static Row EmailRow(string to, string? cc = null, string? attachments = null)
    {
        var table = new DataTable();
        table.SetColumns(new[] { "To", "Cc", "Subject", "Body", "Attachments" });
        var row = table.NewRow();
        row["To"] = to;
        row["Cc"] = cc;
        row["Subject"] = "s";
        row["Body"] = "b";
        row["Attachments"] = attachments;
        return row;
    }

    [Fact]
    public async Task Smtp_InvalidAddress_IsASanitizedExecutionException()
    {
        var smtp = new SmtpDataSource(Context().Object, new Dictionary<string, string> { ["HOST"] = "127.0.0.1", ["PORT"] = "1" });

        var ex = await Assert.ThrowsAsync<ExecutionException>(() => smtp.SendEmail(EmailRow("ok@example.com", cc: "not an address <")));

        Assert.StartsWith("SMTP connector error", ex.Message, StringComparison.Ordinal);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task Smtp_ProviderFailure_DoesNotChainTheRawException()
    {
        var smtp = new SmtpDataSource(Context().Object, new Dictionary<string, string> { ["HOST"] = "127.0.0.1", ["PORT"] = "1" });

        var ex = await Assert.ThrowsAsync<ExecutionException>(() => smtp.SendEmail(EmailRow("ok@example.com")));

        Assert.Null(ex.InnerException);
        Assert.DoesNotContain("127.0.0.1", ex.Message);
    }

    [Fact]
    public async Task Smtp_SecondAttachmentRefused_ReleasesTheFirstAttachmentFile()
    {
        var first = Path.Combine(Path.GetTempPath(), $"etlsql-smtp-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(first, "attachment");
        try
        {
            var context = Context();
            context.Setup(c => c.ResolvePath(It.IsAny<string>())).Returns<string>(p =>
                p == "refused.txt" ? throw new SecurityException("refused") : p);
            var smtp = new SmtpDataSource(context.Object, new Dictionary<string, string> { ["HOST"] = "127.0.0.1", ["PORT"] = "1" });

            await Assert.ThrowsAnyAsync<Exception>(() => smtp.SendEmail(EmailRow("ok@example.com", attachments: $"{first};refused.txt")));

            // A leaked read handle keeps the file locked against delete on Windows.
            File.Delete(first);
            Assert.False(File.Exists(first));
        }
        finally
        {
            if (File.Exists(first)) File.Delete(first);
        }
    }
}
