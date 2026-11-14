using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using PgpCore;
using Xunit;

namespace ETL_SQL.Tests.Hardening;

/// <summary>
/// DECRYPT FILE failures must name the cause without echoing key material, and a failed decrypt
/// must not leave a partial output file (ROADMAP FO06). Measured before the fix: a wrong SSH key
/// reported "Unknown error (0xc100000d)", a wrong password "Encrypted file authentication failed.",
/// a plain file "Unsupported encrypted file format", and every PGP failure left an output file.
/// </summary>
public sealed class FileDecryptErrorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "etlsql-decrypt-" + Guid.NewGuid().ToString("N"));
    private readonly string _plain;

    public FileDecryptErrorTests()
    {
        Directory.CreateDirectory(_dir);
        _plain = P("plain.csv");
        File.WriteAllText(_plain, "id,name\n1,alpha\n");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string P(string name) => Path.Combine(_dir, name);

    private (string publicPem, string privatePem) NewRsaPair(string name)
    {
        using var rsa = RSA.Create(2048);
        var pub = P(name + ".pub");
        var priv = P(name);
        File.WriteAllText(pub, rsa.ExportSubjectPublicKeyInfoPem());
        File.WriteAllText(priv, rsa.ExportPkcs8PrivateKeyPem());
        return (pub, priv);
    }

    private async Task<(string publicKey, string privateKey)> NewPgpPair(string name, string passphrase)
    {
        var pub = P(name + "_public.asc");
        var priv = P(name + "_private.asc");
        using var pgp = new PGP();
        await pgp.GenerateKeyAsync(new FileInfo(pub), new FileInfo(priv), name + " <" + name + "@example.com>", passphrase, 2048);
        return (pub, priv);
    }

    [Fact]
    public void WrongPassword_SaysPasswordOrTampered()
    {
        CryptoUtils.EncryptFile(_plain, P("a.enc"), "right-pass", overwrite: true);
        var error = Assert.Throws<ExecutionException>(() => CryptoUtils.DecryptFile(P("a.enc"), P("out.csv"), "wrong-pass", overwrite: true));
        Assert.Contains("password is wrong", error.Message);
        Assert.DoesNotContain("wrong-pass", error.Message);
        Assert.False(File.Exists(P("out.csv")));
    }

    [Fact]
    public void PlainFile_WithPassword_SaysItWasNotEncryptedThatWay()
    {
        var error = Assert.Throws<ExecutionException>(() => CryptoUtils.DecryptFile(_plain, P("out.csv"), "pass", overwrite: true));
        Assert.Contains("was not encrypted with PASSWORD", error.Message);
        Assert.False(File.Exists(P("out.csv")));
    }

    [Fact]
    public async Task WrongSshKey_SaysTheKeyDoesNotMatch()
    {
        var (pub1, _) = NewRsaPair("k1");
        var (_, priv2) = NewRsaPair("k2");
        await CryptoUtils.EncryptFileWithSshAsync(_plain, P("a.enc"), pub1, overwrite: true);

        var error = await Assert.ThrowsAsync<ExecutionException>(() => CryptoUtils.DecryptFileWithSshAsync(P("a.enc"), P("out.csv"), priv2, overwrite: true));
        Assert.Contains("private key does not match", error.Message);
        Assert.DoesNotContain("PRIVATE KEY", error.Message);
        Assert.False(File.Exists(P("out.csv")));
    }

    [Fact]
    public async Task PlainFile_WithSshKey_SaysItWasNotEncryptedThatWay()
    {
        var (_, priv) = NewRsaPair("k1");
        var error = await Assert.ThrowsAsync<ExecutionException>(() => CryptoUtils.DecryptFileWithSshAsync(_plain, P("out.csv"), priv, overwrite: true));
        Assert.Contains("was not encrypted with KEYFILE", error.Message);
    }

    [Fact]
    public async Task WrongPgpKey_FailsAndLeavesNoOutput()
    {
        var (pub1, _) = await NewPgpPair("one", "pp1");
        var (_, priv2) = await NewPgpPair("two", "pp2");
        await CryptoUtils.EncryptFileWithPgp(_plain, P("a.pgp"), pub1, overwrite: true);

        var error = await Assert.ThrowsAnyAsync<Exception>(() => CryptoUtils.DecryptFileWithPgp(P("a.pgp"), P("out.csv"), priv2, "pp2", overwrite: true));
        Assert.Contains("key", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(P("out.csv")));
        Assert.Empty(Directory.GetFiles(_dir, "out.csv*"));
    }

    [Fact]
    public async Task WrongPgpPassphrase_FailsAndLeavesNoOutput()
    {
        var (pub, priv) = await NewPgpPair("one", "pp1");
        await CryptoUtils.EncryptFileWithPgp(_plain, P("a.pgp"), pub, overwrite: true);

        var error = await Assert.ThrowsAnyAsync<Exception>(() => CryptoUtils.DecryptFileWithPgp(P("a.pgp"), P("out.csv"), priv, "bad", overwrite: true));
        Assert.Contains("passphrase", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bad", error.Message.Replace("passphrase", ""), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(_dir, "out.csv*"));
    }

    [Fact]
    public async Task PlainFile_WithPgpKey_FailsAndLeavesNoOutput()
    {
        var (_, priv) = await NewPgpPair("one", "pp1");
        await Assert.ThrowsAnyAsync<Exception>(() => CryptoUtils.DecryptFileWithPgp(_plain, P("out.csv"), priv, "pp1", overwrite: true));
        Assert.Empty(Directory.GetFiles(_dir, "out.csv*"));
    }

    [Fact]
    public async Task FailedPgpDecrypt_KeepsAnExistingDestination()
    {
        var (_, priv) = await NewPgpPair("one", "pp1");
        File.WriteAllText(P("out.csv"), "keep me");
        await Assert.ThrowsAnyAsync<Exception>(() => CryptoUtils.DecryptFileWithPgp(_plain, P("out.csv"), priv, "pp1", overwrite: true));
        Assert.Equal("keep me", File.ReadAllText(P("out.csv")));
    }
}
