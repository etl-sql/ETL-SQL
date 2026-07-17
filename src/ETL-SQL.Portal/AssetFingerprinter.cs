using System;
using System.IO;
using System.Text.RegularExpressions;

namespace ETL_SQL.Portal;

public static class AssetFingerprinter
{
    public static void Apply(string webRoot, string version)
    {
        if (string.IsNullOrEmpty(webRoot) || !Directory.Exists(webRoot)) return;

        foreach (var htmlFile in Directory.GetFiles(webRoot, "*.html", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(htmlFile);
            var original = content;

            // 1. Match href="/..." or src="/..."
            // Example: href="/css/portal.css"
            content = Regex.Replace(content,
                @"(href|src)=""/([^""]+?\.(?:js|css))(?:\?v=[^""]*)?""",
                $"$1=\"/$2?v={version}\"",
                RegexOptions.IgnoreCase);

            // 2. Match ES Module imports: from '/...'
            // Example: from '/designer/designer.js'
            content = Regex.Replace(content,
                @"(from\s+['""])(/[^'""]+?\.(?:js|css))(?:\?v=[^'""]*)?(['""])",
                $"$1$2?v={version}$3",
                RegexOptions.IgnoreCase);

            if (content != original)
            {
                File.WriteAllText(htmlFile, content, System.Text.Encoding.UTF8);
            }
        }
    }
}
