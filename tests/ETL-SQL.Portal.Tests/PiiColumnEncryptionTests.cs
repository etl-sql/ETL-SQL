using System.IO;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public sealed class PiiColumnEncryptionTests : IDisposable
{
    private readonly string _scratch =
        Path.Combine(Path.GetTempPath(), "pii_encryption_" + Guid.NewGuid().ToString("N")[..8]);

    private readonly PortalPiiProtector _protector =
        PortalPiiProtector.Create(new EphemeralDataProtectionProvider());

    public PiiColumnEncryptionTests()
    {
        Directory.CreateDirectory(_scratch);
    }

    public void Dispose()
    {
        try { Directory.Delete(_scratch, recursive: true); } catch { }
    }

    [Fact]
    public async Task SaveAndRetrievePII_EncryptsAndDecryptsCorrectly()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_scratch, "portal.db")}")
            .UsePortalEncryption(_protector)
            .Options;
        await using var db = new PortalDbContext(options);
        await db.Database.MigrateAsync();

        // 1. Save data with sensitive PII fields
        var user = new PortalUser
        {
            UserName = "test_user_pii",
            Email = "john.doe@example.com",
            NormalizedEmail = "JOHN.DOE@EXAMPLE.COM",
            FirstName = "John",
            LastName = "Doe",
            PhoneNumber = "555-0199"
        };
        db.Users.Add(user);

        var folder = new Folder
        {
            Id = 1,
            Name = "Root",
            Path = "/"
        };
        db.Folders.Add(folder);

        var report = new Report
        {
            Id = 1,
            Name = "Test Report",
            ScriptPath = "test.etlsql",
            FolderId = 1,
            CreatedBy = 1
        };
        db.Reports.Add(report);

        var subscription = new Subscription
        {
            ReportId = 1,
            UserId = 1,
            Recipients = "alice@example.com;bob@example.com",
            SmtpAlias = "default-smtp"
        };
        db.Subscriptions.Add(subscription);

        await db.SaveChangesAsync();

        // 2. Query using a raw ADO.NET connection to verify that data is indeed encrypted on disk
        using (var connection = db.Database.GetDbConnection())
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT email, first_name, last_name, phone_number FROM asp_net_users WHERE user_name = 'test_user_pii'";
            using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());

            var emailVal = reader.GetString(0);
            var firstNameVal = reader.GetString(1);
            var lastNameVal = reader.GetString(2);
            var phoneVal = reader.GetString(3);

            // Verify they do not contain cleartext values
            Assert.NotEqual("john.doe@example.com", emailVal);
            Assert.NotEqual("John", firstNameVal);
            Assert.NotEqual("Doe", lastNameVal);
            Assert.NotEqual("555-0199", phoneVal);

            // Verify they are encrypted strings (usually starts with standard Data Protection header)
            Assert.StartsWith("dp:", emailVal, StringComparison.Ordinal);
            Assert.True(emailVal.Length > 20);
        }

        // 3. Query through EF Core to verify automated decryption
        await using var dbRead = new PortalDbContext(options);
        var readUser = await dbRead.Users.SingleAsync(u => u.UserName == "test_user_pii");
        Assert.Equal("john.doe@example.com", readUser.Email);
        Assert.Equal("John", readUser.FirstName);
        Assert.Equal("Doe", readUser.LastName);
        Assert.Equal("555-0199", readUser.PhoneNumber);

        var readSub = await dbRead.Subscriptions.FirstAsync();
        Assert.Equal("alice@example.com;bob@example.com", readSub.Recipients);

        await dbRead.Database.OpenConnectionAsync(CancellationToken.None);
        await using var recipientCommand = dbRead.Database.GetDbConnection().CreateCommand();
        recipientCommand.CommandText = "SELECT recipients FROM subscriptions LIMIT 1";
        var storedRecipients = Assert.IsType<string>(await recipientCommand.ExecuteScalarAsync(CancellationToken.None));
        Assert.StartsWith("dp:", storedRecipients, StringComparison.Ordinal);
        Assert.DoesNotContain("alice@example.com", storedRecipients, StringComparison.Ordinal);

    }
}
