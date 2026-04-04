using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ETL_SQL.Common
{
    public static class AsyncExtensions
    {
        public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> items)
        {
            var results = new List<T>();
            await foreach (var item in items)
            {
                results.Add(item);
            }
            return results;
        }
    }
}
