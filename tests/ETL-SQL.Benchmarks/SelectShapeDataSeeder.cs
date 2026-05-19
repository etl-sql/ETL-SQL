using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Connectors.MockDb;

namespace ETL_SQL.Benchmarks
{
    /// <summary>
    /// Seeds a single <c>events</c> table used by the SELECT shape benchmarks.
    /// Columns: id INT, category VARCHAR (5 values), score INT (0–99), ts DATETIME.
    /// </summary>
    public class SelectShapeDataSeeder(int rowCount = 10_000) : IMockDataSeeder
    {
        private static readonly string[] Categories = ["Alpha", "Beta", "Gamma", "Delta", "Epsilon"];
        private static readonly DateTime BaseDate = new(2024, 1, 1);

        public async Task SeedDataAsync(Dictionary<string, DataTable> tables, Random rng)
        {
            var events = new DataTable();
            events.SetColumns(["id", "category", "score", "ts"]);

            for (int i = 1; i <= rowCount; i++)
            {
                await events.AddRowAsync(new Row
                {
                    ["id"]       = i,
                    ["category"] = Categories[i % Categories.Length],
                    ["score"]    = rng.Next(0, 100),
                    ["ts"]       = BaseDate.AddSeconds(i)
                });
            }

            tables["events"] = events;
        }
    }
}
