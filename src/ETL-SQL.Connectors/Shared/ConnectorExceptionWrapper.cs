using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Connectors.Shared
{
    internal static class ConnectorExceptionWrapper
    {
        public static ExecutionException Wrap(string connectorName, Exception ex)
        {
            if (ex is ExecutionException executionException) return executionException;
            return new ExecutionException($"{connectorName} connector error: {ex.Message}", ex);
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
