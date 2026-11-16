using Confluent.Kafka;
using ETL_SQL.Connectors.Email;
using ETL_SQL.Connectors.Kafka;
using ETL_SQL.Core.Common.Exceptions;
using MailKit.Security;
using Xunit;

namespace ETL_SQL.Tests.Connectors
{
    [Trait("Connector", "MULTIPLE")]
    public class TransportSecurityOptionTests
    {
        [Theory]
        [InlineData("Ssl", SecurityProtocol.Ssl)]
        [InlineData("SaslSsl", SecurityProtocol.SaslSsl)]
        [InlineData("SASL_SSL", SecurityProtocol.SaslSsl)]
        [InlineData("sasl_plaintext", SecurityProtocol.SaslPlaintext)]
        [InlineData("Plaintext", SecurityProtocol.Plaintext)]
        public void Kafka_SecurityProtocol_AcceptsDocumentedAndLibrdkafkaNames(string value, SecurityProtocol expected) =>
            Assert.Equal(expected, KafkaDataSource.ParseSecurityProtocol(value));

        [Fact]
        public void Kafka_UnknownSecurityProtocol_IsRefusedNotDowngraded() =>
            Assert.Throws<ExecutionException>(() => KafkaDataSource.ParseSecurityProtocol("TLS"));

        [Theory]
        [InlineData(true, 587, SecureSocketOptions.StartTls)]
        [InlineData(true, 25, SecureSocketOptions.StartTls)]
        [InlineData(true, 465, SecureSocketOptions.SslOnConnect)]
        [InlineData(false, 587, SecureSocketOptions.StartTlsWhenAvailable)]
        public void Smtp_UseSsl_RequiresTlsInTheModeThePortSpeaks(bool useSsl, int port, SecureSocketOptions expected) =>
            Assert.Equal(expected, SmtpDataSource.TlsMode(useSsl, port));
    }
}
