using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Connectors.Shared
{
    public static partial class ConnectorExceptionWrapper
    {
        // Patterns that may reveal hostnames, credentials, or file paths in provider exception messages.
        [GeneratedRegex(
            @"(?:Server|Data\s+Source|Host|Password|Pwd|User\s+Id|Uid|Username|Database|Initial\s+Catalog|Dsn)\s*=\s*[^\s;,""'`]+" // connection string key=value
            + @"|'[^']{1,64}'@'[^']{1,128}'"           // MySQL 'user'@'host'
            + @"|""[^""]{1,64}""@""[^""]{1,128}"""     // quoted user@host
            + @"|(?:[A-Za-z]:\\|/(?:etc|home|root|usr|var|srv|opt|tmp)/)[^\s'""]*" // Windows and Unix paths
            + @"|\b(?:\d{1,3}\.){3}\d{1,3}\b"          // IPv4 addresses
            + @"|(?:[a-zA-Z0-9-]{2,63}\.){1,5}[a-zA-Z]{2,6}(?::\d{2,5})?\b", // hostnames (host.domain.tld[:port])
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
        private static partial Regex SensitivePatterns();

        private static string SanitizeMessage(string message) =>
            SensitivePatterns().Replace(message, "<redacted>");

        public static ExecutionException Wrap(string connectorName, Exception ex)
        {
            if (ex is ExecutionException executionException) return executionException;
            // COMPAT_BREAK: 0.20.0 — raw provider details must not escape through InnerException/ToString.
            return new ExecutionException($"{connectorName} connector error: {SanitizeMessage(ex.Message)}");
        }

        /// <summary>
        /// Runs an async provider operation, converting any matching provider exception into a
        /// sanitized <see cref="ExecutionException"/> at the connector boundary. Centralizes the
        /// catch-when(shouldWrap) -> Wrap pattern so providers don't each hand-roll it.
        /// </summary>
        public static async Task<T> RunAsync<T>(
            string connectorName,
            Func<Exception, bool> shouldWrap,
            Func<Task<T>> operation)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (shouldWrap(ex))
            {
                throw Wrap(connectorName, ex);
            }
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
