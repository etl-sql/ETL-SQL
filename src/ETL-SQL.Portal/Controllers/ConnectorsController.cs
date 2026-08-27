using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Connectors;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Diagnostics;
using ETL_SQL.Core.Governance;
using ETL_SQL.Data;
using ETL_SQL.Gateway;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ETL_SQL.Portal.Controllers;

/// <summary>
/// API surface for discovering connector schemas, parsing unformatted connection strings,
/// discovering active Data Gateways and their published resources, and executing layered
/// zero-trust connection diagnostic probes.
/// </summary>
[ApiController]
[Route("api/connectors")]
[Authorize]
public class ConnectorsController(
    IConnectorRegistry connectorRegistry,
    ConnectionDiagnosticEngine diagnosticEngine,
    IExecutionContext context,
    RequestTenantContextAccessor? tenantAccessor = null,
    IGatewayEnrollmentStore? enrollmentStore = null,
    GatewaySessionRegistry? gatewayRegistry = null,
    PortalConfig? config = null) : ControllerBase
{
    public sealed record ParseConnectionStringRequest(string? ConnectionString, string? HintProvider);
    public sealed record TestConnectionRequest(string? Alias, string? ConnectorType, string? Target, Dictionary<string, string>? Options, int ProbeTimeoutSeconds = 5);

    private string CurrentTenant => tenantAccessor?.Current?.Tenant.Value ?? (string.IsNullOrWhiteSpace(config?.TenantId) ? "default" : config.TenantId);

    public sealed record DiscoveredGatewayDto(
        string Id,
        string Name,
        bool IsOnline,
        int ActiveNodes,
        int TotalNodes,
        string? Region = null,
        string? Status = null,
        IReadOnlyList<GatewayEnrollmentController.GatewayDiscoveredResourceDto>? Resources = null);

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

    /// <summary>
    /// Discovers active/enrolled Data Gateways and their approved published resources for the authenticated tenant.
    /// Never returns physical targets, local connection strings, or credentials.
    /// </summary>
    [HttpGet("gateways")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetGateways(CancellationToken ct)
    {
        var tenantId = CurrentTenant;
        var enrollments = enrollmentStore != null
            ? await enrollmentStore.ListByTenantAsync(tenantId, ct)
            : [];
        var activeClusters = gatewayRegistry?.ListActiveClusters(tenantId) ?? [];
        var clusterMap = activeClusters.ToDictionary(c => c.GatewayId, StringComparer.OrdinalIgnoreCase);

        var list = enrollments
            .Where(e => e.State != GatewayEnrollmentState.Revoked)
            .Select(e =>
            {
                clusterMap.TryGetValue(e.GatewayId, out var cluster);
                var isOnline = cluster is { ActiveNodes: > 0 };
                IReadOnlyList<GatewayEnrollmentController.GatewayDiscoveredResourceDto>? publishedResources = null;
                if (isOnline && gatewayRegistry != null && gatewayRegistry.TryGet(tenantId, e.GatewayId, out var session) && session != null)
                {
                    publishedResources = session.PublishedResources
                        .Where(r => r.State == GatewayResourceState.Approved)
                        .Select(r => new GatewayEnrollmentController.GatewayDiscoveredResourceDto(
                            r.ResourceId,
                            r.ConnectorType,
                            r.AllowedOperations.ToString(),
                            r.State.ToString(),
                            IsOnline: session.IsActive,
                            LastSeenUtc: session.LastSeenUtc))
                        .ToList();
                }

                return new DiscoveredGatewayDto(
                    Id: e.GatewayId,
                    Name: e.GatewayId,
                    IsOnline: isOnline,
                    ActiveNodes: cluster?.ActiveNodes ?? 0,
                    TotalNodes: cluster?.TotalNodes ?? 0,
                    Region: "On-Premises",
                    Status: isOnline ? "Active" : "Disconnected",
                    Resources: publishedResources);
            }).ToList();

        return Ok(list);
    }

    /// <summary>
    /// Returns approved published resources for a specific active gateway session scoped to the authenticated tenant.
    /// Never returns physical targets, local connection strings, or credentials.
    /// </summary>
    [HttpGet("gateways/{gatewayId}/resources")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetGatewayResources(string gatewayId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(gatewayId))
            return BadRequest(new { error = "GatewayId is required." });

        var tenantId = CurrentTenant;
        if (enrollmentStore != null)
        {
            var enrollment = await enrollmentStore.FindByGatewayAsync(tenantId, gatewayId.Trim(), ct);
            if (enrollment is null || enrollment.State == GatewayEnrollmentState.Revoked)
                return NotFound(new { error = $"Gateway '{gatewayId}' was not found for the active tenant." });
        }

        if (gatewayRegistry is null || !gatewayRegistry.TryGet(tenantId, gatewayId.Trim(), out var session) || session is null || !session.IsActive)
        {
            return Ok(Array.Empty<GatewayEnrollmentController.GatewayDiscoveredResourceDto>());
        }

        var resources = session.PublishedResources
            .Where(r => r.State == GatewayResourceState.Approved)
            .Select(r => new GatewayEnrollmentController.GatewayDiscoveredResourceDto(
                r.ResourceId,
                r.ConnectorType,
                r.AllowedOperations.ToString(),
                r.State.ToString(),
                IsOnline: session.IsActive,
                LastSeenUtc: session.LastSeenUtc))
            .ToList();

        return Ok(resources);
    }
}
