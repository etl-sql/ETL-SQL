using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Connectors;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Diagnostics;
using ETL_SQL.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ETL_SQL.Portal.Controllers;

/// <summary>
/// API surface for discovering connector schemas, parsing unformatted connection strings,
/// and executing layered zero-trust connection diagnostic probes.
/// </summary>
[ApiController]
[Route("api/connectors")]
[Authorize]
public class ConnectorsController(
    IConnectorRegistry connectorRegistry,
    ConnectionDiagnosticEngine diagnosticEngine,
    IExecutionContext context) : ControllerBase
{
    public sealed record ParseConnectionStringRequest(string? ConnectionString, string? HintProvider);
    public sealed record TestConnectionRequest(string? Alias, string? ConnectorType, string? Target, Dictionary<string, string>? Options, int ProbeTimeoutSeconds = 5);

    /// <summary>
    /// Returns the schema descriptor for a specific connector type, or all registered connector schemas.
    /// </summary>
    [HttpGet("schema")]
    public IActionResult GetSchemas([FromQuery] string? type)
    {
        if (!string.IsNullOrWhiteSpace(type))
        {
            var schema = connectorRegistry.GetConnectorSchema(type);
            return schema is not null
                ? Ok(schema)
                : NotFound(new { error = $"Connector type '{type}' not found." });
        }
        return Ok(connectorRegistry.GetAllConnectorSchemas());
    }

    /// <summary>
    /// Parses an unformatted ADO.NET/ODBC/URI connection string into structured connector options
    /// and extracts sensitive credentials into a suggested secret reference key.
    /// </summary>
    [HttpPost("parse-string")]
    public IActionResult ParseString([FromBody] ParseConnectionStringRequest request)
    {
        var result = ConnectionStringParser.Parse(request?.ConnectionString ?? string.Empty, request?.HintProvider);
        return Ok(result);
    }

    /// <summary>
    /// Executes a layered zero-trust diagnostic probe against the specified connector target and options
    /// without requiring the connection to be registered in the catalog or script first.
    /// </summary>
    [HttpPost("test")]
    public async Task<IActionResult> TestConnection([FromBody] TestConnectionRequest request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ConnectorType))
            return BadRequest(new { error = "ConnectorType is required for connection testing." });

        try
        {
            var report = await diagnosticEngine.DiagnoseTargetAsync(
                context,
                request.Alias ?? "test_connection",
                request.ConnectorType,
                request.Target ?? string.Empty,
                request.Options,
                request.ProbeTimeoutSeconds > 0 ? request.ProbeTimeoutSeconds : 5,
                ct);

            return Ok(report);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = SecretRedactor.Redact(ex.Message) });
        }
    }
}
