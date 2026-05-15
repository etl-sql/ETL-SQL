using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Connectors.MockDb;

namespace ETL_SQL.Benchmarks
{
    public class TpcHMockDataSeeder(double scaleFactor = 0.01) : IMockDataSeeder
    {
        private readonly double _scaleFactor = scaleFactor;

        public async Task SeedDataAsync(Dictionary<string, DataTable> tables, Random rng)
        {
            // Seed lineitem
            var lineitem = new DataTable();
            lineitem.SetColumns(new[] { 
                "l_orderkey", "l_partkey", "l_suppkey", "l_linenumber", "l_quantity", 
                "l_extendedprice", "l_discount", "l_tax", "l_returnflag", "l_linestatus", 
                "l_shipdate", "l_commitdate", "l_receiptdate", "l_shipinstruct", "l_shipmode", "l_comment" 
            });

            int rowCount = (int)(600000 * _scaleFactor);
            for (int i = 0; i < rowCount; i++)
            {
                await lineitem.AddRowAsync(new Row
                {
                    ["l_orderkey"] = rng.Next(1, 100000),
                    ["l_partkey"] = rng.Next(1, 20000),
                    ["l_suppkey"] = rng.Next(1, 1000),
                    ["l_linenumber"] = i % 7,
                    ["l_quantity"] = (decimal)(rng.Next(1, 50)),
                    ["l_extendedprice"] = (decimal)(rng.Next(900, 150000)),
                    ["l_discount"] = (decimal)(rng.Next(0, 11) / 100.0),
                    ["l_tax"] = (decimal)(rng.Next(0, 9) / 100.0),
                    ["l_returnflag"] = rng.Next(0, 3) switch { 0 => "R", 1 => "A", _ => "N" },
                    ["l_linestatus"] = rng.Next(0, 2) == 0 ? "F" : "O",
                    ["l_shipdate"] = new DateTime(1992, 1, 1).AddDays(rng.Next(0, 2500))
                });
            }

            tables["lineitem"] = lineitem;
        }
    }
}
