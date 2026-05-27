using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Connectors.Shared
{
    internal static class ConnectorExceptionWrapper
    {
        // Patterns that may reveal hostnames, credentials, or file paths in provider exception messages.
        // The full provider exception is preserved as the inner exception for Debug-level logging.
        private static readonly Regex SensitivePatterns = new Regex(
            @"(?:Server|Data\s+Source|Host|Password|Pwd|User\s+Id|Uid|Username|Database|Initial\s+Catalog|Dsn)\s*=\s*[^\s;,""'`]+" // connection string key=value
            + @"|'[^']{1,64}'@'[^']{1,128}'"           // MySQL 'user'@'host'
            + @"|""[^""]{1,64}""@""[^""]{1,128}"""     // quoted user@host
            + @"|(?:[A-Za-z]:\\|/(?:etc|home|root|usr|var|srv|opt|tmp)/)[^\s'""]*" // Windows and Unix paths
            + @"|\b(?:\d{1,3}\.){3}\d{1,3}\b"          // IPv4 addresses
            + @"|(?:[a-zA-Z0-9-]{2,63}\.){1,5}[a-zA-Z]{2,6}(?::\d{2,5})?\b", // hostnames (host.domain.tld[:port])
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static string SanitizeMessage(string message) =>
            SensitivePatterns.Replace(message, "<redacted>");

        public static ExecutionException Wrap(string connectorName, Exception ex)
        {
            if (ex is ExecutionException executionException) return executionException;
            // Sanitized message surfaces to users/logs; full provider detail is in the inner exception.
            return new ExecutionException($"{connectorName} connector error: {SanitizeMessage(ex.Message)}", ex);
        }

        public static async IAsyncEnumerable<T> WrapAsync<T>(
            IAsyncEnumerable<T> source,
            string connectorName,
            Func<Exception, bool> shouldWrap,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await using var enumerator = source.GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                bool moved;
                try
                {
                    moved = await enumerator.MoveNextAsync();
                }
                catch (Exception ex) when (shouldWrap(ex))
                {
                    throw Wrap(connectorName, ex);
                }

                if (!moved) yield break;
                yield return enumerator.Current;
            }
        }
    }
}
