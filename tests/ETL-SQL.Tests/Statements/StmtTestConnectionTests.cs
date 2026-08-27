using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Diagnostics;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements
{
    /// <summary>
    /// Covers TEST CONNECTION parsing (including the soft-keyword guarantee that "test" remains a
    /// usable identifier) and the governed diagnostic handler's behaviour for a non-network connection.
    /// Socket-level DNS/TCP/TLS paths are exercised by manual/end-to-end verification.
    /// </summary>
    public class StmtTestConnectionTests
    {
        private static Script Parse(string sql)
        {
            var lexer = new Lexer(sql);
            var parser = new Parser(lexer.Tokenize());
            return parser.Parse();
        }

        private static async Task Execute(string sql, Evaluator evaluator)
        {
            var script = Parse(sql);
            await evaluator.Evaluate(script);
        }

        [Fact]
        public void ParsesTestConnection()
        {
            var script = Parse("TEST CONNECTION prod;");
            var stmt = Assert.IsType<TestConnectionStatement>(script.Statements.Single());
            Assert.Equal("prod", stmt.ConnectionName);
            Assert.Null(stmt.IntoTable);
        }

        [Fact]
        public void ParsesTestConnectionIntoTempTable()
        {
            var script = Parse("TEST CONNECTION prod INTO #diag;");
            var stmt = Assert.IsType<TestConnectionStatement>(script.Statements.Single());
            Assert.Equal("prod", stmt.ConnectionName);
            Assert.Equal("#diag", stmt.IntoTable);
        }

        [Fact]
        public void TestRemainsAUsableIdentifier()
        {
            // The soft keyword must not reserve "test": these must still parse without error.
            _ = Parse("SELECT * FROM test;");
            _ = Parse("CREATE TABLE test (id INT);");
        }

        [Fact]
        public async Task MissingConnectionThrows()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            await Assert.ThrowsAsync<ExecutionException>(() => Execute("TEST CONNECTION does_not_exist;", evaluator));
        }

        [Fact]
        public async Task ReachableTcpEndpointReportsDnsAndTcpOk()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            var context = (IExecutionContext)evaluator;
            context.PreviewLimit = 0; // don't open the datasource on CREATE

            using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
                await Execute(
                    $"CREATE CONNECTION diag_tcp AS MSSQL(SERVER='127.0.0.1', PORT={port}, DATABASE='x', USERNAME='u', PASSWORD='p');",
                    evaluator);
                await Execute("TEST CONNECTION diag_tcp;", evaluator);

                var table = Assert.IsType<DataTable>(context.LastResult);
                var status = table.Rows.ToDictionary(r => (string)r["Layer"]!, r => (string)r["Status"]!);

                Assert.Equal("OK", status.GetValueOrDefault("DNS"));
                Assert.Equal("OK", status.GetValueOrDefault("TCP"));
                Assert.Equal("FAILED", status.GetValueOrDefault("AUTH"));
            }
            finally
            {
                listener.Stop();
            }
        }

        [Fact]
        public async Task PortalCoreEntryPoint_ReachableTcp_ReportsDnsAndTcpOk()
        {
            // Exercises the context-free overload the Portal "Test connection" button uses:
            // connection details in, governed DNS+TCP probe out, no IExecutionContext.
            var provider = ETL_SQL.Program.ServiceProvider;
            var registry = provider.GetRequiredService<ETL_SQL.Data.IConnectorRegistry>();
            var security = ((IExecutionContext)provider.GetRequiredService<Evaluator>()).SecurityService;
            var engine = new ETL_SQL.Core.Diagnostics.ConnectionDiagnosticEngine(registry);

            using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
                var snapshot = ETL_SQL.Core.Governance.ExecutionPolicySnapshot.Capture(
                    ETL_SQL.Core.Governance.EnterprisePolicyRuntime.Current, "unit",
                    ETL_SQL.Core.Governance.ScriptExecutionMode.Batch, "unit-test");
                var options = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["SERVER"] = "127.0.0.1",
                    ["PORT"] = port.ToString(),
                };

                var report = await engine.DiagnoseAsync(
                    "probe", "MSSQL", "Server=127.0.0.1", options, security, snapshot, 5, default);

                var status = report.Steps.ToDictionary(s => s.Layer, s => s.Status);
                Assert.Equal(ETL_SQL.Core.Diagnostics.DiagnosticStatus.Ok, status.GetValueOrDefault("DNS"));
                Assert.Equal(ETL_SQL.Core.Diagnostics.DiagnosticStatus.Ok, status.GetValueOrDefault("TCP"));
                Assert.Equal(ETL_SQL.Core.Diagnostics.DiagnosticStatus.Failed, status.GetValueOrDefault("AUTH"));
            }
            finally
            {
                listener.Stop();
            }
        }

        [Fact]
        public async Task AuthProbeConnector_AddsAuthOkWithoutLeakingSecret()
        {
            var registry = new TestConnectorRegistry(new AuthProbeConnector());
            var security = ((IExecutionContext)ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>()).SecurityService;
            var engine = new ConnectionDiagnosticEngine(registry);
            var snapshot = ETL_SQL.Core.Governance.ExecutionPolicySnapshot.Capture(
                ETL_SQL.Core.Governance.EnterprisePolicyRuntime.Current, "unit",
                ETL_SQL.Core.Governance.ScriptExecutionMode.Batch, "unit-test");

            using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
                var report = await engine.DiagnoseAsync(
                    "probe", "AUTHMOCK", "127.0.0.1",
                    new Dictionary<string, string>
                    {
                        ["HOST"] = "127.0.0.1",
                        ["PORT"] = port.ToString(),
                        ["PASSWORD"] = "super-secret-value"
                    },
                    security, snapshot, 5, default);

                var auth = report.Steps.Single(s => s.Layer == "AUTH");
                Assert.Equal(DiagnosticStatus.Ok, auth.Status);
                Assert.DoesNotContain("super-secret-value", string.Join(" ", report.Steps.Select(s => s.Detail + " " + s.Remedy)));
            }
            finally
            {
                listener.Stop();
            }
        }

        [Fact]
        public async Task NonNetworkConnectionReportsPolicyOkAndNetworkSkipped()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            var context = (IExecutionContext)evaluator;

            await Execute("CREATE CONNECTION diag_mock AS MOCKDB();", evaluator);
            await Execute("TEST CONNECTION diag_mock;", evaluator);

            var table = Assert.IsType<DataTable>(context.LastResult);
            Assert.Equal(new[] { "Layer", "Status", "Detail", "Remedy" }, table.ColumnNames.ToArray());

            var policy = table.Rows.Single(r => (string?)r["Layer"] == "POLICY");
            Assert.Equal("OK", policy["Status"]);

            var network = table.Rows.Single(r => (string?)r["Layer"] == "NETWORK");
            Assert.Equal("SKIPPED", network["Status"]);
        }

        [Fact]
        public async Task DiagnoseTargetAsync_SerializesDiagnosticStatusAsString()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            var context = (IExecutionContext)evaluator;
            var registry = ETL_SQL.Program.ServiceProvider.GetRequiredService<IConnectorRegistry>();
            var engine = new ConnectionDiagnosticEngine(registry);

            var report = await engine.DiagnoseTargetAsync(
                context,
                "mock_target",
                "MOCKDB",
                string.Empty,
                new Dictionary<string, string>(),
                1);

            Assert.True(report.Succeeded);
            var json = System.Text.Json.JsonSerializer.Serialize(report);
            Assert.Contains("\"Status\":\"Ok\"", json);
            Assert.Contains("\"Status\":\"Skipped\"", json);
            Assert.DoesNotContain("\"Status\":0", json);
        }

        private sealed class AuthProbeConnector : IConnector, IConnectionDiagnosticAuthProbe
        {
            public string Name => "AUTHMOCK";
            public IReadOnlyList<string> Aliases => [];
            public Task<string> GetVersionAsync(IExecutionContext context, string connectionString) => Task.FromResult("authmock");
            public HashSet<string> GetSupportedFunctions() => [];
            public HashSet<string> GetSupportedKeywords() => [];
            public Dictionary<string, string[]> GetSupportedOptions() => [];
            public Dictionary<string, string[]> GetOptionValues() => [];
            public string GetHelp() => "Auth probe mock.";
            public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null) =>
                throw new System.NotSupportedException();
            public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
            public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
            public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) => Task.FromResult(Enumerable.Empty<string>());
            public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
            public string? GetHost(string connectionString, Dictionary<string, string>? options = null) => options?.GetValueOrDefault("HOST") ?? connectionString;

            public Task<IReadOnlyList<DiagnosticStep>> DiagnoseAuthenticationAsync(
                ConnectionDiagnosticAuthContext context,
                CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<DiagnosticStep>>(
                    [new DiagnosticStep("AUTH", DiagnosticStatus.Ok, "Mock authentication succeeded.")]);
        }

        private sealed class TestConnectorRegistry(IConnector connector) : IConnectorRegistry
        {
            public void Register(IConnector connector) { }
            public IConnector? GetConnector(string name) =>
                string.Equals(name, connector.Name, System.StringComparison.OrdinalIgnoreCase) ? connector : null;
            public IEnumerable<string> GetRegisteredNames() => [connector.Name];
            public HashSet<string> GetAllConnectorKeywords() => [];
            public HashSet<string> GetAllConnectorFunctions() => [];
            public Dictionary<string, string[]> GetAllConnectorOptionValues() => [];
            public IEnumerable<ConnectorSchemaDescriptor> GetAllConnectorSchemas() => [connector.GetSchemaDescriptor()];
            public ConnectorSchemaDescriptor? GetConnectorSchema(string connectorType) =>
                string.Equals(connectorType, connector.Name, System.StringComparison.OrdinalIgnoreCase) ? connector.GetSchemaDescriptor() : null;
        }
    }
}
