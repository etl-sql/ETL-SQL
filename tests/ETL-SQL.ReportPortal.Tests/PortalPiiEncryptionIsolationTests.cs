using System;
using System.Data.Common;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ETL_SQL.ReportPortal.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ETL_SQL.ReportPortal.Tests;

/// <summary>
/// Proves PII encryption is owned by each context rather than a process-global static: two hosts in
/// one process, each with its own Data Protection key ring, must not be able to read one another's
/// encrypted PII, and the model cache must not leak one host's protector to the other.
/// </summary>
[Trait("Category", "Portal")]
public sealed class PortalPiiEncryptionIsolationTests : IDisposable
{
    private readonly string _scratch =
        Path.Combine(Path.GetTempPath(), "pii_isolation_" + Guid.NewGuid().ToString("N")[..8]);

    public PortalPiiEncryptionIsolationTests() => Directory.CreateDirectory(_scratch);

    public void Dispose()
    {
        try { Directory.Delete(_scratch, recursive: true); } catch { }
    }

    private DbContextOptions<PortalDbContext> OptionsFor(PortalPiiProtector protector) =>
        new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_scratch, "shared.db")}")
            .UsePortalEncryption(protector)
            .Options;

    [Fact]
    public async Task TwoHostsWithDifferentKeyRings_CannotReadEachOthersPii()
    {
        // Two independent hosts, each with its own key ring — the multi-host scenario the old static
        // provider mishandled by letting the second host overwrite the first's protector.
        var hostA = PortalPiiProtector.Create(new EphemeralDataProtectionProvider());
        var hostB = PortalPiiProtector.Create(new EphemeralDataProtectionProvider());

        // Host A creates the schema and writes an encrypted user.
        await using (var dbA = new PortalDbContext(OptionsFor(hostA)))
        {
            await dbA.Database.MigrateAsync();
            dbA.Users.Add(new PortalUser { UserName = "shared_user", Email = "secret@example.com" });
            await dbA.SaveChangesAsync();

            // Encrypted at rest.
            Assert.StartsWith("dp:", await ReadRawEmailAsync(dbA), StringComparison.Ordinal);
        }

        // Host B, with a different key ring, must NOT be able to decrypt Host A's PII. If the model
        // cache leaked Host A's protector into Host B (the bug this guards), decryption would silently
        // succeed instead of throwing.
        await using (var dbB = new PortalDbContext(OptionsFor(hostB)))
        {
            var ex = await Assert.ThrowsAnyAsync<Exception>(
                () => dbB.Users.FirstAsync(u => u.UserName == "shared_user"));
            Assert.True(
                HasCryptographicCause(ex),
                $"Expected a CryptographicException in the chain, got: {ex}");
        }

        // Host A can still round-trip its own data — its protector was not disposed or replaced.
        await using (var dbA2 = new PortalDbContext(OptionsFor(hostA)))
        {
            var user = await dbA2.Users.FirstAsync(u => u.UserName == "shared_user");
            Assert.Equal("secret@example.com", user.Email);
        }
    }

    private static async Task<string> ReadRawEmailAsync(PortalDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT \"Email\" FROM \"AspNetUsers\" WHERE \"UserName\" = 'shared_user'";
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static bool HasCryptographicCause(Exception? ex)
    {
        for (; ex != null; ex = ex.InnerException)
            if (ex is CryptographicException)
                return true;
        return false;
    }
}
