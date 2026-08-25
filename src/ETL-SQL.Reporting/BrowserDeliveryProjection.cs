using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ETL_SQL.Reporting
{
    /// <summary>
    /// The single boundary between a report manifest and a browser client.
    ///
    /// <para>A <see cref="ReportManifest"/> is the server's working object: it carries the full
    /// semantic contracts — <c>ChartSpec</c>, <c>ChartDataSet</c>, <c>PlotPlan</c> — because PDF,
    /// markdown, terminal, and static export all resolve against them. A browser needs none of that.
    /// It draws the server-rendered <c>nativeSvg</c>, reads <c>rows</c>, and acts on the compact
    /// resolved <c>interaction</c> contract. Shipping five representations of one chart when two are
    /// consumed trades once-cached library bytes for uncached per-report bytes on every load.</para>
    ///
    /// <para>Every property is classified exactly once, here. A new manifest property must be added
    /// to <see cref="ServerOnlyVisualProperties"/> or left delivered deliberately;
    /// <c>BrowserDeliveryProjectionTests</c> fails on any property this class has not classified, so
    /// the wire contract cannot grow by accident.</para>
    /// </summary>
    public static class BrowserDeliveryProjection
    {
        /// <summary>
        /// Visual properties the server owns and normal browser delivery never receives. Full
        /// contracts remain available to server renderers, to tests, and to the explicitly authorized
        /// diagnostic projection.
        /// </summary>
        // COMPAT_BREAK: 0.19 — normal browser delivery no longer carries the semantic contracts.
        public static readonly IReadOnlySet<string> ServerOnlyVisualProperties =
            new HashSet<string>(StringComparer.Ordinal)
            {
                nameof(VisualManifest.ChartSpec),
                nameof(VisualManifest.ChartData),
                nameof(VisualManifest.PlotPlan),
                // Superseded by the compact resolved `interaction` contract. Kept on the server
                // object for the diagnostic projection and for legacy snapshot round-trips.
                nameof(VisualManifest.Interactions)
            };

        /// <summary>Micro-chart properties browser delivery drops; the browser draws `svg` and reads
        /// `plainText`/`accessibleLabel`, never the resolved plan behind them.</summary>
        public static readonly IReadOnlySet<string> ServerOnlyMicroChartProperties =
            new HashSet<string>(StringComparer.Ordinal) { nameof(MicroChartManifest.PlotPlan) };

        private static readonly JsonSerializerOptions BrowserOptions = Create(includeSemanticContracts: false);
        private static readonly JsonSerializerOptions DiagnosticOptions = Create(includeSemanticContracts: true);

        /// <summary>Serializer options for normal browser delivery.</summary>
        public static JsonSerializerOptions Options => BrowserOptions;

        /// <summary>
        /// Serializer options that retain the full semantic contracts. Reserved for the explicitly
        /// authorized diagnostic output — never reachable from a general production query flag.
        /// </summary>
        public static JsonSerializerOptions AuthorizedDiagnosticOptions => DiagnosticOptions;

        /// <summary>Serializes a manifest for a normal browser client.</summary>
        public static string Serialize(ReportManifest manifest)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            return JsonSerializer.Serialize(manifest, BrowserOptions);
        }

        /// <summary>
        /// Serializes a manifest with the full semantic contracts intact. The caller must have
        /// already established that the requester is authorized for diagnostic output.
        /// </summary>
        public static string SerializeAuthorizedDiagnostic(ReportManifest manifest)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            return JsonSerializer.Serialize(manifest, DiagnosticOptions);
        }

        /// <summary>
        /// Re-projects a manifest already serialized to JSON — a stored snapshot, an artifact read
        /// back from storage — through the same classification, so a stored payload cannot reach a
        /// browser carrying contracts a freshly built one would have dropped.
        /// </summary>
        public static string ProjectStoredJson(string manifestJson)
        {
            if (string.IsNullOrWhiteSpace(manifestJson)) return manifestJson;
            var manifest = JsonSerializer.Deserialize<ReportManifest>(manifestJson)
                ?? throw new JsonException("The stored report manifest payload was null.");
            return Serialize(manifest);
        }

        private static JsonSerializerOptions Create(bool includeSemanticContracts)
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = false };
            if (includeSemanticContracts) return options;

            options.TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers = { DropServerOnlyProperties }
            };
            return options;
        }

        private static void DropServerOnlyProperties(JsonTypeInfo type)
        {
            var dropped = type.Type == typeof(VisualManifest) ? ServerOnlyVisualProperties
                : type.Type == typeof(MicroChartManifest) ? ServerOnlyMicroChartProperties
                : null;
            if (dropped is null) return;

            foreach (var property in type.Properties.Where(item =>
                item.AttributeProvider is System.Reflection.PropertyInfo info && dropped.Contains(info.Name)).ToList())
            {
                property.ShouldSerialize = static (_, _) => false;
            }
        }
    }
}
