using System;
using System.IO;
using System.Text.RegularExpressions;

namespace ETL_SQL.Core.Data
{
    public sealed record BundleUri(string BundleName, int? Version, string Path)
    {
        private static readonly Regex UriRegex = new(
            @"^orch://(?<bundle>[^/@]+)(?:@(?<version>\d+))?/(?<path>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool TryParse(string value, out BundleUri? uri)
        {
            uri = null;
            var match = UriRegex.Match(value);
            if (!match.Success) return false;

            var version = match.Groups["version"].Success
                ? int.Parse(match.Groups["version"].Value)
                : (int?)null;
            uri = new BundleUri(
                match.Groups["bundle"].Value,
                version,
                NormalizePath(match.Groups["path"].Value));
            return true;
        }

        public string ToPinnedString(int version) => $"orch://{BundleName}@{version}/{NormalizePath(Path)}";

        public static string NormalizePath(string path)
            => path.Replace('\\', '/').TrimStart('/');

        public static string CombineRelative(BundleUri current, string relativePath)
        {
            if (TryParse(relativePath, out _)) return relativePath;
            var parent = System.IO.Path.GetDirectoryName(current.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
            var combined = string.IsNullOrWhiteSpace(parent)
                ? relativePath
                : System.IO.Path.Combine(parent, relativePath);
            var normalized = NormalizePath(combined);
            return current.Version.HasValue
                ? $"orch://{current.BundleName}@{current.Version.Value}/{normalized}"
                : $"orch://{current.BundleName}/{normalized}";
        }
    }
}
