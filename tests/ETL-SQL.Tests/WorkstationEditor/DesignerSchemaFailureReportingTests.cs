using System.Net;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Services;
using ETL_SQL.WorkstationEditor;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.WorkstationEditor;

/// <summary>
/// The reproduction for "the paginated dataset wizard reports no tables for a connection that has
/// them", driven at the surface the wizard actually calls.
///
/// <para>The wizard has one input for this step: the table list from
/// <c>GET /api/designer/schema</c>. <see cref="MetadataManager.GetTablesAsync"/> answered an empty
/// list to four different situations — a connection nobody registered, a connection whose connector
/// type this host has not loaded, a connection that threw on connect, and a connection that
/// genuinely holds no tables — and logged the exception where nobody was looking. The route's own
/// <c>catch</c> could not help, because nothing ever reached it. So the author was told "this
/// connection reported no tables you can read" about a connection whose read had failed outright,
/// which is a confident statement the server never made.</para>
///
/// <para>These assert the status code and that the reason reaches the caller, because that is the
/// difference the wizard renders on. A test that only asserted "the table list is empty" passes in
/// every one of these cases, and that is what let this survive.</para>
/// </summary>
public sealed class DesignerSchemaFailureReportingTests
{
    private const string DocumentUri = "file:///probe.etlsql";

    private static MetadataManager NewManager() =>
        new(NullLogger.Instance, new ConnectorRegistry())
        {
            SchemaCacheDirectory = null,
            DisableBackgroundRefresh = true
        };

    [Fact]
    public async Task UnknownConnection_IsNotReportedAsAConnectionWithNoTables()
    {
        var manager = NewManager();

        var read = await manager.TryGetTablesAsync("NoSuchConnection", DocumentUri);

        Assert.Equal(SchemaReadOutcome.UnknownConnection, read.Outcome);
        Assert.Empty(read.Tables);
        Assert.Contains("NoSuchConnection", read.Error);

        // The lossy read is deliberately unchanged: completion runs at a caret and must not throw.
        Assert.Empty(await manager.GetTablesAsync("NoSuchConnection", DocumentUri));
    }

    [Fact]
    public async Task ConnectorTypeThisHostDoesNotHave_IsReportedAsAFailure()
    {
        var manager = NewManager();
        manager.RegisterDocumentConnection(
            DocumentUri, "Warehouse", "NOT_A_REAL_CONNECTOR", "Server=nowhere;Database=none;");

        var read = await manager.TryGetTablesAsync("Warehouse", DocumentUri);

        Assert.Equal(SchemaReadOutcome.Failed, read.Outcome);
        Assert.Empty(read.Tables);
        Assert.Contains("NOT_A_REAL_CONNECTOR", read.Error);
    }

    [Fact]
    public async Task SchemaRoute_AnswersNotFoundForAnUnknownConnection_RatherThanAnEmptyTableList()
    {
        using var temp = new TempEditorWorkspace();
        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, null, 0, false, "test-token", StudioMode: true));
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/designer/schema?connection=NoSuchConnection&documentUri={Uri.EscapeDataString(DocumentUri)}");
        request.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        var response = await client.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        // Before the fix this was 200 with `"tables":[]`, and an empty list is exactly what selects
        // the wizard's zero-tables branch — so a 404's worth of information rendered as a statement
        // about the connection's contents.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("\"tables\"", payload);
        Assert.Contains("NoSuchConnection", payload);
    }

    [Fact]
    public async Task SchemaRoute_AnswersBadRequestWhenTheConnectorIsNotLoaded_RatherThanAnEmptyTableList()
    {
        using var temp = new TempEditorWorkspace();
        await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            temp.Root, null, 0, false, "test-token", StudioMode: true));
        await app.StartAsync();

        app.Services.GetRequiredService<IMetadataManager>()
            .RegisterDocumentConnection(DocumentUri, "Warehouse", "NOT_A_REAL_CONNECTOR", "Server=nowhere;");

        using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/designer/schema?connection=Warehouse&documentUri={Uri.EscapeDataString(DocumentUri)}");
        request.Headers.Add("X-ETLSQL-EDITOR-TOKEN", "test-token");
        var response = await client.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("\"tables\"", payload);
        Assert.Contains("NOT_A_REAL_CONNECTOR", payload);
    }

    /// <summary>A throwaway workspace root; the editor host refuses to start without one.</summary>
    private sealed class TempEditorWorkspace : IDisposable
    {
        public TempEditorWorkspace()
        {
            Root = Path.Combine(
                Path.GetTempPath(), "etl-sql-schema-failure-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
