using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Connectors.MockDb;

namespace ETL_SQL.Benchmarks
{
    public class TpcHMockDataSeeder(double scaleFactor = 0.1) : IMockDataSeeder
    {
        private readonly double _scaleFactor = scaleFactor;

        // TPC-H standard reference data
        private static readonly (int Key, string Name, int RegionKey)[] Nations =
        [
            (0,  "ALGERIA",        0), (1,  "ARGENTINA",      1), (2,  "BRAZIL",         1),
            (3,  "CANADA",         1), (4,  "EGYPT",          4), (5,  "ETHIOPIA",        0),
            (6,  "FRANCE",         3), (7,  "GERMANY",        3), (8,  "INDIA",           2),
            (9,  "INDONESIA",      2), (10, "IRAN",           4), (11, "IRAQ",            4),
            (12, "JAPAN",          2), (13, "JORDAN",         4), (14, "KENYA",           0),
            (15, "MOROCCO",        0), (16, "MOZAMBIQUE",     0), (17, "PERU",            1),
            (18, "CHINA",          2), (19, "ROMANIA",        3), (20, "SAUDI ARABIA",    4),
            (21, "VIETNAM",        2), (22, "RUSSIA",         3), (23, "UNITED KINGDOM",  3),
            (24, "UNITED STATES",  1)
        ];

        private static readonly (int Key, string Name)[] Regions =
        [
            (0, "AFRICA"), (1, "AMERICA"), (2, "ASIA"), (3, "EUROPE"), (4, "MIDDLE EAST")
        ];

        private static readonly string[] ShipModes       = ["REG AIR", "AIR", "RAIL", "SHIP", "TRUCK", "MAIL", "FOB"];
        private static readonly string[] ShipInstructs   = ["DELIVER IN PERSON", "COLLECT COD", "NONE", "TAKE BACK RETURN"];
        private static readonly string[] OrderPriorities = ["1-URGENT", "2-HIGH", "3-MEDIUM", "4-NOT SPECIFIED", "5-LOW"];
        private static readonly string[] MktSegments     = ["BUILDING", "AUTOMOBILE", "MACHINERY", "HOUSEHOLD", "FURNITURE"];
        private static readonly string[] PartTypes       =
        [
            "PROMO ANODIZED STEEL", "PROMO BURNISHED COPPER", "PROMO POLISHED BRASS",
            "STANDARD ANODIZED BRASS", "ECONOMY POLISHED NICKEL", "SMALL BRUSHED STEEL",
            "MEDIUM POLISHED TIN", "LARGE BURNISHED ALUMINUM", "STANDARD BRUSHED COPPER"
        ];
        private static readonly string[] PartContainers = ["SM CASE", "LG BOX", "MED BAG", "JUMBO PACK", "WRAP JAR"];

        public async Task SeedDataAsync(Dictionary<string, DataTable> tables, Random rng)
        {
            // Proportional row counts — lineitem base is 600k at SF=1 (1/10 of real TPC-H).
            int numOrders    = Math.Max(10,  (int)(150000 * _scaleFactor));
            int numCustomers = Math.Max(10,  (int)(15000  * _scaleFactor));
            int numParts     = Math.Max(100, (int)(20000  * _scaleFactor));
            int numSuppliers = Math.Max(5,   (int)(1000   * _scaleFactor));
            int numLineItems = Math.Max(1,   (int)(600000 * _scaleFactor));

            // region — 5 fixed rows
            var region = new DataTable();
            region.SetColumns(["r_regionkey", "r_name", "r_comment"]);
            foreach (var (key, name) in Regions)
                await region.AddRowAsync(new Row { ["r_regionkey"] = key, ["r_name"] = name, ["r_comment"] = $"{name} region" });
            tables["region"] = region;

            // nation — 25 fixed rows
            var nation = new DataTable();
            nation.SetColumns(["n_nationkey", "n_name", "n_regionkey", "n_comment"]);
            foreach (var (key, name, regionKey) in Nations)
                await nation.AddRowAsync(new Row { ["n_nationkey"] = key, ["n_name"] = name, ["n_regionkey"] = regionKey, ["n_comment"] = $"{name} nation" });
            tables["nation"] = nation;

            // customer
            var customer = new DataTable();
            customer.SetColumns(["c_custkey", "c_name", "c_address", "c_nationkey", "c_phone", "c_acctbal", "c_mktsegment", "c_comment"]);
            for (int i = 1; i <= numCustomers; i++)
                await customer.AddRowAsync(new Row
                {
                    ["c_custkey"]    = i,
                    ["c_name"]       = $"Customer#{i:D9}",
                    ["c_address"]    = $"Addr_{i}",
                    ["c_nationkey"]  = rng.Next(0, 25),
                    ["c_phone"]      = $"555-{i:D6}",
                    ["c_acctbal"]    = (decimal)(rng.Next(-100000, 999900)) / 100m,
                    ["c_mktsegment"] = MktSegments[rng.Next(MktSegments.Length)],
                    ["c_comment"]    = "comment"
                });
            tables["customer"] = customer;

            // supplier
            var supplier = new DataTable();
            supplier.SetColumns(["s_suppkey", "s_name", "s_address", "s_nationkey", "s_phone", "s_acctbal", "s_comment"]);
            for (int i = 1; i <= numSuppliers; i++)
                await supplier.AddRowAsync(new Row
                {
                    ["s_suppkey"]   = i,
                    ["s_name"]      = $"Supplier#{i:D9}",
                    ["s_address"]   = $"SAddr_{i}",
                    ["s_nationkey"] = rng.Next(0, 25),
                    ["s_phone"]     = $"555-9{i:D5}",
                    ["s_acctbal"]   = (decimal)(rng.Next(0, 999900)) / 100m,
                    ["s_comment"]   = "comment"
                });
            tables["supplier"] = supplier;

            // part — ~1/3 have PROMO types so Q14 returns a non-zero ratio
            var part = new DataTable();
            part.SetColumns(["p_partkey", "p_name", "p_mfgr", "p_brand", "p_type", "p_size", "p_container", "p_retailprice", "p_comment"]);
            for (int i = 1; i <= numParts; i++)
                await part.AddRowAsync(new Row
                {
                    ["p_partkey"]    = i,
                    ["p_name"]       = $"part_{i}",
                    ["p_mfgr"]       = $"Manufacturer#{rng.Next(1, 6)}",
                    ["p_brand"]      = $"Brand#{rng.Next(1, 50)}",
                    ["p_type"]       = PartTypes[rng.Next(PartTypes.Length)],
                    ["p_size"]       = rng.Next(1, 51),
                    ["p_container"]  = PartContainers[rng.Next(PartContainers.Length)],
                    ["p_retailprice"] = (decimal)(90000 + i % 20086) / 100m,
                    ["p_comment"]    = "comment"
                });
            tables["part"] = part;

            // orders
            var orders = new DataTable();
            orders.SetColumns(["o_orderkey", "o_custkey", "o_orderstatus", "o_totalprice", "o_orderdate", "o_orderpriority", "o_clerk", "o_shippriority", "o_comment"]);
            for (int i = 1; i <= numOrders; i++)
                await orders.AddRowAsync(new Row
                {
                    ["o_orderkey"]     = i,
                    ["o_custkey"]      = rng.Next(1, numCustomers + 1),
                    ["o_orderstatus"]  = rng.Next(3) switch { 0 => "F", 1 => "P", _ => "O" },
                    ["o_totalprice"]   = (decimal)rng.Next(100000, 50000000) / 100m,
                    ["o_orderdate"]    = new DateTime(1992, 1, 1).AddDays(rng.Next(0, 2557)),
                    ["o_orderpriority"] = OrderPriorities[rng.Next(OrderPriorities.Length)],
                    ["o_clerk"]        = $"Clerk#{rng.Next(1, 1001):D9}",
                    ["o_shippriority"] = 0,
                    ["o_comment"]      = "comment"
                });
            tables["orders"] = orders;

            // lineitem — key ranges use actual seeded table sizes for join coherence
            var lineitem = new DataTable();
            lineitem.SetColumns([
                "l_orderkey", "l_partkey", "l_suppkey", "l_linenumber", "l_quantity",
                "l_extendedprice", "l_discount", "l_tax", "l_returnflag", "l_linestatus",
                "l_shipdate", "l_commitdate", "l_receiptdate", "l_shipinstruct", "l_shipmode", "l_comment"
            ]);
            for (int i = 0; i < numLineItems; i++)
            {
                // Q12 requires l_shipdate < l_commitdate < l_receiptdate
                var shipDate    = new DateTime(1992, 1, 1).AddDays(rng.Next(0, 2500));
                var commitDate  = shipDate.AddDays(rng.Next(1, 31));
                var receiptDate = commitDate.AddDays(rng.Next(1, 31));
                await lineitem.AddRowAsync(new Row
                {
                    ["l_orderkey"]      = rng.Next(1, numOrders + 1),
                    ["l_partkey"]       = rng.Next(1, numParts + 1),
                    ["l_suppkey"]       = rng.Next(1, numSuppliers + 1),
                    ["l_linenumber"]    = i % 7,
                    ["l_quantity"]      = (decimal)rng.Next(1, 50),
                    ["l_extendedprice"] = (decimal)rng.Next(900, 150000),
                    ["l_discount"]      = (decimal)(rng.Next(0, 11) / 100.0),
                    ["l_tax"]           = (decimal)(rng.Next(0, 9) / 100.0),
                    ["l_returnflag"]    = rng.Next(3) switch { 0 => "R", 1 => "A", _ => "N" },
                    ["l_linestatus"]    = rng.Next(2) == 0 ? "F" : "O",
                    ["l_shipdate"]      = shipDate,
                    ["l_commitdate"]    = commitDate,
                    ["l_receiptdate"]   = receiptDate,
                    ["l_shipinstruct"]  = ShipInstructs[rng.Next(ShipInstructs.Length)],
                    ["l_shipmode"]      = ShipModes[rng.Next(ShipModes.Length)],
                    ["l_comment"]       = "comment"
                });
            }
            tables["lineitem"] = lineitem;
        }
    }
}
