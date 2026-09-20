using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ETL_SQL.Portal.Tests;

/// <summary>Generates Studio's browser paths from named, registered host endpoints.</summary>
internal static class StudioBrowserContractsGenerator
{
    internal sealed record Route(string Key, string Path, IReadOnlyList<string> Methods);

    internal static IReadOnlyList<Route> ReadRoutes(IEnumerable<RouteEndpoint> endpoints, string group)
    {
        var prefix = $"Studio.{group}.";
        var routes = endpoints.Select(endpoint => (Endpoint: endpoint,
                Name: endpoint.Metadata.GetMetadata<IRouteNameMetadata>()?.RouteName))
            .Where(item => item.Name?.StartsWith(prefix, StringComparison.Ordinal) == true)
            .Select(item =>
            {
                var key = item.Name![prefix.Length..];
                if (key.Length == 0 || !char.IsAsciiLetter(key[0]) || !key.All(char.IsAsciiLetterOrDigit))
                    throw new InvalidOperationException($"Invalid Studio route key: {key}");
                if (item.Endpoint.RoutePattern.Parameters.Count != 0)
                    throw new InvalidOperationException($"Studio route {key} requires a parameter builder.");
                var path = "/" + item.Endpoint.RoutePattern.RawText!.TrimStart('/');
                var methods = item.Endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
                if (methods is null || methods.Count == 0)
                    throw new InvalidOperationException($"Studio route {key} has no HTTP method contract.");
                return new Route(key, path, methods);
            })
            .OrderBy(route => route.Key, StringComparer.Ordinal)
            .ToList();
        if (routes.Count == 0 || routes.Select(route => route.Key).Distinct(StringComparer.Ordinal).Count() != routes.Count)
            throw new InvalidOperationException($"Studio {group} routes are empty or have duplicate keys.");
        return routes;
    }

    internal static void VerifySharedRoutes(IReadOnlyList<Route> routes, IEnumerable<RouteEndpoint> desktopEndpoints)
    {
        var endpoints = desktopEndpoints.ToList();
        foreach (var route in routes)
        {
            foreach (var method in route.Methods)
            {
                if (!endpoints.Any(endpoint =>
                    string.Equals("/" + endpoint.RoutePattern.RawText?.TrimStart('/'), route.Path, StringComparison.Ordinal)
                    && endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(method, StringComparer.Ordinal) == true))
                    throw new InvalidOperationException($"WorkstationEditor does not serve {method} {route.Path} ({route.Key}).");
            }
        }
    }

    internal static string Generate(IEnumerable<RouteEndpoint> portalEndpoints, IEnumerable<RouteEndpoint> desktopEndpoints)
    {
        var portal = portalEndpoints.ToList();
        var desktop = desktopEndpoints.ToList();
        var shared = ReadRoutes(portal, "Shared");
        VerifySharedRoutes(shared, desktop);
        var builder = new StringBuilder("""
            /**
             * Copyright 2026 Charles Clemens and ETL-SQL contributors
             * Licensed under the Apache License, Version 2.0.
             *
             * GENERATED FILE - DO NOT EDIT.
             * Source: named Studio endpoints registered by Portal and WorkstationEditor.
             * Regenerate: ETLSQL_UPDATE_BROWSER_CONTRACTS=1 dotnet test tests/ETL-SQL.Portal.Tests
             *   --filter FullyQualifiedName~BrowserContractsGeneratorTests
             * Then run node scripts/sync-assets.js.
             */


            """.ReplaceLineEndings("\n"));
        foreach (var (table, routes) in new[]
        {
            ("STUDIO_ROUTES", shared),
            ("STUDIO_CATALOG_ROUTES", ReadRoutes(portal, "Catalog")),
            ("STUDIO_WORKSPACE_ROUTES", ReadRoutes(desktop, "Workspace"))
        })
        {
            builder.Append($"export const {table} = Object.freeze({{\n");
            foreach (var route in routes)
                builder.Append($"    {route.Key}: {JsonSerializer.Serialize(route.Path)},\n");
            builder.Append("} as const);\n\n");
        }
        return builder.ToString();
    }
}
