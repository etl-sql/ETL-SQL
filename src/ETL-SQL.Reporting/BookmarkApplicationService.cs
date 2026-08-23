using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ETL_SQL.Core.Reporting;

namespace ETL_SQL.Reporting
{
    /// <summary>
    /// Resolves and validates a <see cref="ResolvedReportState"/> envelope (from an author bookmark or a
    /// Portal saved view) against the current <see cref="ReportManifest"/>. Unknown or deleted references
    /// are reconciled with warnings rather than throwing, so a stale envelope never prevents the base
    /// report from opening. The result is a clean, applicable state that the atomic application operation
    /// (see <c>DashboardService.ApplyBookmarkAsync</c>) commits through the cascading-parameter engine.
    /// </summary>
    public static class BookmarkApplicationService
    {
        private static readonly HashSet<string> NumericTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "INT", "INTEGER", "BIGINT", "SMALLINT", "TINYINT", "DECIMAL", "NUMERIC",
            "FLOAT", "REAL", "DOUBLE", "MONEY", "NUMBER"
        };

        private static readonly HashSet<string> BooleanTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "BIT", "BOOL", "BOOLEAN"
        };

        /// <summary>Looks up an author bookmark's state by name (case-insensitive). Null if not found.</summary>
        public static ResolvedReportState? ResolveAuthorBookmark(ReportManifest manifest, string bookmarkName)
        {
            if (manifest.Bookmarks == null || string.IsNullOrWhiteSpace(bookmarkName)) return null;
            var bm = manifest.Bookmarks.FirstOrDefault(b =>
                string.Equals(b.Name, bookmarkName, StringComparison.OrdinalIgnoreCase));
            return bm?.State;
        }

        /// <summary>
        /// Reconciles <paramref name="requested"/> against the current manifest. Returns the applicable
        /// state plus warnings for anything dropped. <paramref name="currentScriptHash"/>, when supplied,
        /// enables report-revision drift detection for saved views.
        /// </summary>
        public static ReportStateReconciliation Reconcile(
            ReportManifest manifest,
            ResolvedReportState requested,
            string? currentScriptHash = null)
        {
            var result = new ReportStateReconciliation();
            var state = result.State;
            state.SchemaVersion = requested.SchemaVersion;
            state.ScriptHash = requested.ScriptHash;
            state.ReportRevision = requested.ReportRevision;

            // Report-revision drift (saved views): warn but continue.
            if (!string.IsNullOrEmpty(requested.ScriptHash)
                && !string.IsNullOrEmpty(currentScriptHash)
                && !string.Equals(requested.ScriptHash, currentScriptHash, StringComparison.OrdinalIgnoreCase))
            {
                result.HasDrift = true;
                result.Warnings.Add("This saved view was created against a different version of the report; "
                    + "unrecognized settings were skipped.");
            }

            var pageNames = new HashSet<string>(
                (manifest.Pages ?? new()).Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
            var objectNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in manifest.Visuals ?? new()) objectNames.Add(v.Name);
            if (manifest.Containers != null)
                foreach (var c in manifest.Containers) objectNames.Add(c.Name);

            // Active page.
            if (!string.IsNullOrEmpty(requested.ActivePage))
            {
                if (pageNames.Contains(requested.ActivePage))
                    state.ActivePage = requested.ActivePage;
                else
                    result.Warnings.Add($"Page '{requested.ActivePage}' no longer exists; navigation was skipped.");
            }

            // Parameters (typed).
            foreach (var (rawName, value) in requested.Parameters)
            {
                var name = rawName.StartsWith('@') ? rawName : "@" + rawName;
                var known = manifest.ParameterMetadata.ContainsKey(name) || manifest.Parameters.ContainsKey(name);
                if (!known)
                {
                    result.Warnings.Add($"Parameter '{name}' is no longer declared; it was skipped.");
                    continue;
                }
                if (manifest.ParameterMetadata.TryGetValue(name, out var meta))
                {
                    if (!TryCoerce(meta.Type, value, out var coerced))
                    {
                        result.Warnings.Add($"Value for '{name}' does not match its declared type '{meta.Type}'; it was skipped.");
                        continue;
                    }
                    state.Parameters[name] = coerced;
                }
                else
                {
                    state.Parameters[name] = value;
                }
            }

            // Presentation state (VISIBLE / COLLAPSED) — object must still exist.
            foreach (var (obj, on) in requested.Visible)
            {
                if (objectNames.Contains(obj)) state.Visible[obj] = on;
                else result.Warnings.Add($"Object '{obj}' referenced by VISIBLE state no longer exists; it was skipped.");
            }
            foreach (var (obj, on) in requested.Collapsed)
            {
                if (objectNames.Contains(obj)) state.Collapsed[obj] = on;
                else result.Warnings.Add($"Object '{obj}' referenced by COLLAPSED state no longer exists; it was skipped.");
            }

            return result;
        }

        /// <summary>
        /// Attempts to coerce a typed value to a declared parameter type. Returns false only when the
        /// value is clearly incompatible (e.g. a non-numeric string into a numeric column).
        /// </summary>
        public static bool TryCoerce(string declaredType, ReportStateValue value, out ReportStateValue coerced)
        {
            coerced = value;
            if (value.Kind == ReportStateValueKind.Null) return true; // null assignable to any type

            var baseType = declaredType ?? string.Empty;
            var paren = baseType.IndexOf('(');
            if (paren >= 0) baseType = baseType[..paren];
            baseType = baseType.Trim();

            if (NumericTypes.Contains(baseType))
            {
                if (value.Kind == ReportStateValueKind.Number) return true;
                if (value.Kind == ReportStateValueKind.String
                    && decimal.TryParse(value.StringValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
                {
                    coerced = ReportStateValue.FromNumber(d);
                    return true;
                }
                return false;
            }

            if (BooleanTypes.Contains(baseType))
            {
                if (value.Kind == ReportStateValueKind.Boolean) return true;
                if (value.Kind == ReportStateValueKind.String && bool.TryParse(value.StringValue, out var b))
                {
                    coerced = ReportStateValue.FromBoolean(b);
                    return true;
                }
                return false;
            }

            // String / date / other declared types accept any scalar (numbers project to their canonical text).
            return true;
        }
    }
}
