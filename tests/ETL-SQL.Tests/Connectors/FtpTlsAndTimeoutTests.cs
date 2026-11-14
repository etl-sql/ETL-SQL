using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Connectors;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

/// <summary>
/// Unit tests for FTP connector TIMEOUT_SECONDS defaults and overrides, plus strict FTPS TLS
/// certificate verification, fingerprint pinning, and explicit opt-out semantics (ROADMAP RF02).
/// </summary>
public sealed class FtpTlsAndTimeoutTests
{
    private static IExecutionContext CreateContext() =>
        DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

    private static X509Certificate2 CreateEphemeralCert(string cn = "CN=ftps.test")
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(cn, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }

    [Fact]
    public void TimeoutSeconds_DefaultsTo30_AndHonorsOverride()
    {
        var context = CreateContext();
        var def = (FtpConnector)new FtpConnector().CreateDataSource(context, "ftp.example.com", new Dictionary<string, string>
        {
            ["USER"] = "ftpuser",
            ["PASSWORD"] = "ftppass"
        });
        def.ConfigureClient(c =>
        {
            Assert.Equal(30_000, c.Config.ConnectTimeout);
            Assert.Equal(30_000, c.Config.ReadTimeout);
            Assert.Equal(30_000, c.Config.WriteTimeout);
            Assert.Equal(30_000, c.Config.DataConnectionConnectTimeout);
            Assert.Equal(30_000, c.Config.DataConnectionReadTimeout);
            Assert.Equal(30_000, c.Config.DataConnectionWriteTimeout);
            Assert.False(c.Config.ValidateAnyCertificate);
        });

        var custom = (FtpConnector)new FtpConnector().CreateDataSource(context, "ftp.example.com", new Dictionary<string, string>
        {
            ["USER"] = "ftpuser",
            ["PASSWORD"] = "ftppass",
            ["TIMEOUT_SECONDS"] = "75"
        });
        custom.ConfigureClient(c =>
        {
            Assert.Equal(75_000, c.Config.ConnectTimeout);
            Assert.Equal(75_000, c.Config.DataConnectionReadTimeout);
        });
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-10")]
    [InlineData("abc")]
    public void TimeoutSeconds_RejectsNonPositiveOrNonNumeric(string value)
    {
        var context = CreateContext();
        Assert.Throws<ExecutionException>(() => new FtpConnector().CreateDataSource(
            context,
            "ftp.example.com",
            new Dictionary<string, string> { ["TIMEOUT_SECONDS"] = value }));
    }

    [Fact]
    public void AllowUntrustedCert_RejectsUnrecognizedSpelling()
    {
        var context = CreateContext();
        Assert.Throws<ExecutionException>(() => new FtpConnector().CreateDataSource(
            context,
            "ftp.example.com",
            new Dictionary<string, string> { ["ALLOW_UNTRUSTED_CERT"] = "MAYBE" }));
    }

    [Fact]
    public void EvaluateServerCertificate_IsStrictByDefault()
    {
        using var cert = CreateEphemeralCert();

        Assert.Equal(
            FtpConnector.TlsCertDecision.TrustedByChain,
            FtpConnector.EvaluateServerCertificate(null, false, SslPolicyErrors.None, cert));

        Assert.Equal(
            FtpConnector.TlsCertDecision.RejectedUntrusted,
            FtpConnector.EvaluateServerCertificate(null, false, SslPolicyErrors.RemoteCertificateChainErrors, cert));

        Assert.Equal(
            FtpConnector.TlsCertDecision.UntrustedAllowed,
            FtpConnector.EvaluateServerCertificate(null, true, SslPolicyErrors.RemoteCertificateNameMismatch, cert));
    }

    [Fact]
    public void EvaluateServerCertificate_PinMatchingAndMismatchOverridesOptOut()
    {
        using var cert = CreateEphemeralCert("CN=pin.test");
        using var other = CreateEphemeralCert("CN=other.test");

        string sha256 = cert.GetCertHashString(HashAlgorithmName.SHA256);
        string colonized = "SHA256:" + string.Join(":", Enumerable.Range(0, sha256.Length / 2)
            .Select(i => sha256.Substring(i * 2, 2).ToLowerInvariant()));

        // Matching pin accepts even a self-signed certificate with policy errors.
        Assert.Equal(
            FtpConnector.TlsCertDecision.TrustedByPin,
            FtpConnector.EvaluateServerCertificate(colonized, false, SslPolicyErrors.RemoteCertificateChainErrors, cert));

        // Mismatched pin rejects even when chain would pass AND even when ALLOW_UNTRUSTED_CERT is true.
        Assert.Equal(
            FtpConnector.TlsCertDecision.RejectedMismatch,
            FtpConnector.EvaluateServerCertificate(sha256, false, SslPolicyErrors.None, other));
        Assert.Equal(
            FtpConnector.TlsCertDecision.RejectedMismatch,
            FtpConnector.EvaluateServerCertificate(sha256, true, SslPolicyErrors.RemoteCertificateChainErrors, other));
    }

    [Fact]
    public void GetSupportedOptions_IncludesTimeoutAndTlsCertificateOptions()
    {
        var supported = new FtpConnector().GetSupportedOptions();
        Assert.True(supported.ContainsKey("TIMEOUT_SECONDS"));
        Assert.True(supported.ContainsKey("CERT_FINGERPRINT"));
        Assert.True(supported.ContainsKey("ALLOW_UNTRUSTED_CERT"));
    }
}
