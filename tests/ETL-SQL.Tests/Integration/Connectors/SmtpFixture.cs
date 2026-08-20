using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace ETL_SQL.Tests.Integration.Connectors
{
    /// <summary>
    /// Starts a real axllent/mailpit container for SMTP integration tests.
    /// MailPit accepts all SMTP traffic on port 1025 and exposes an HTTP API on port 8025
    /// that lets tests read received messages.
    /// </summary>
    public class SmtpFixture : IAsyncLifetime
    {
        private IContainer? _container;

        public string SmtpHost => "localhost";
        public int SmtpPort { get; private set; }
        public int ApiPort { get; private set; }

        public async Task InitializeAsync()
        {
            _container = new ContainerBuilder("axllent/mailpit:latest")
                .WithName($"etl-sql-smtp-{Guid.NewGuid():N}")
                .WithLabel("test-suite", "ETL-SQL.Integration")
                .WithPortBinding(1025, true)   // SMTP
                .WithPortBinding(8025, true)   // HTTP API
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r =>
                    r.ForPort(8025).ForPath("/api/v1/info")))
                .Build();

            await _container.StartAsync();
            SmtpPort = _container.GetMappedPublicPort(1025);
            ApiPort = _container.GetMappedPublicPort(8025);
        }

        public async Task DisposeAsync()
        {
            if (_container != null)
                await _container.DisposeAsync();
        }

        /// <summary>
        /// Reads all received messages from the MailPit API and returns them as parsed JSON.
        /// </summary>
        public async Task<JsonElement> GetMessagesAsync()
        {
            using var http = new HttpClient();
            var json = await http.GetStringAsync($"http://localhost:{ApiPort}/api/v1/messages");
            return JsonDocument.Parse(json).RootElement;
        }

        /// <summary>Returns the total number of messages received since the container started.</summary>
        public async Task<int> GetMessageCountAsync()
        {
            var root = await GetMessagesAsync();
            if (root.TryGetProperty("total", out var total)) return total.GetInt32();
            if (root.TryGetProperty("messages_count", out var mc)) return mc.GetInt32();
            // Fallback: count array elements in "messages"
            if (root.TryGetProperty("messages", out var msgs) && msgs.ValueKind == JsonValueKind.Array)
                return msgs.GetArrayLength();
            return 0;
        }
    }

    [CollectionDefinition("SMTP collection")]
    public class SmtpCollection : ICollectionFixture<SmtpFixture> { }
}
