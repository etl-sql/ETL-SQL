using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Reporting;

namespace ETL_SQL.Reporting
{
    /// <summary>
    /// Enforces <see cref="DetailSurfaceLimits.MaxManifestBytes"/> once the manifest is
    /// complete.
    /// </summary>
    /// <remarks>
    /// The other detail-surface budgets are structural and can be checked while resolving the
    /// AST. Payload size cannot: what a popover actually costs to open is the serialized size
    /// of the container and visuals it renders — including their rows — and those only exist
    /// after the full manifest has been built. This guard therefore runs last, and fails
    /// closed rather than shipping a report whose detail surface would stall on open.
    /// </remarks>
    public static class DetailSurfacePayloadGuard
    {
        private static readonly JsonSerializerOptions MeasurementOptions = new()
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Measures every detail surface in <paramref name="manifest"/> and throws when one
        /// exceeds the payload budget.
        /// </summary>
        /// <exception cref="ExecutionException">A surface exceeds the byte budget.</exception>
        public static void Enforce(ReportManifest manifest)
        {
            ArgumentNullException.ThrowIfNull(manifest);

            foreach (var visual in manifest.Visuals ?? [])
            {
                if (visual.Tooltip == null) continue;

                int bytes = Measure(visual.Tooltip, manifest);
                if (bytes <= DetailSurfaceLimits.MaxManifestBytes) continue;

                throw new ExecutionException(
                    $"[{DetailSurfaceDiagnostics.ManifestBytesExceeded}] The detail surface on " +
                    $"'{visual.Name}' would serialize {bytes:N0} bytes, exceeding the limit of " +
                    $"{DetailSurfaceLimits.MaxManifestBytes:N0}. Reduce the rows its visuals " +
                    "return — filter the detail query by @hover_value, or aggregate it — so the " +
                    "popover stays responsive to open.");
            }
        }

        /// <summary>
        /// Serialized size of one detail surface: the tooltip projection plus every visual it
        /// renders, resolved through any referenced container graph.
        /// </summary>
        public static int Measure(TooltipManifest tooltip, ReportManifest manifest)
        {
            ArgumentNullException.ThrowIfNull(tooltip);
            ArgumentNullException.ThrowIfNull(manifest);

            int bytes = JsonSerializer.SerializeToUtf8Bytes(tooltip, MeasurementOptions).Length;

            var names = new HashSet<string>(
                DetailSurfaceProjection.ResolvedVisualNames(tooltip), StringComparer.OrdinalIgnoreCase);
            if (names.Count == 0) return bytes;

            foreach (var visual in manifest.Visuals ?? [])
            {
                if (!names.Contains(visual.Name)) continue;
                bytes += JsonSerializer.SerializeToUtf8Bytes(visual, MeasurementOptions).Length;
            }

            var containerRef = tooltip.ContainerRef;
            if (!string.IsNullOrEmpty(containerRef))
            {
                var container = (manifest.Containers ?? [])
                    .FirstOrDefault(c => string.Equals(c.Name, containerRef, StringComparison.OrdinalIgnoreCase));
                if (container != null)
                    bytes += JsonSerializer.SerializeToUtf8Bytes(container, MeasurementOptions).Length;
            }

            return bytes;
        }
    }
}
