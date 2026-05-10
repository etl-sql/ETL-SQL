using System;
using System.IO;

namespace ETL_SQL.TestData
{
    class GenerateStressData
    {
        static void Main(string[] args)
        {
            int bigCount = 1000000;
            if (args.Length > 0 && int.TryParse(args[0], out var count)) bigCount = count;

            Console.WriteLine($"Generating SmallTable.csv (1000 rows)...");
            using (var sw = new StreamWriter("TestData/test_stress_SmallTable.csv"))
            {
                sw.WriteLine("ID,Name");
                for (int i = 1; i <= 1000; i++)
                {
                    sw.WriteLine($"{i},User_{i}");
                }
            }

            Console.WriteLine($"Generating BigTable.csv ({bigCount} rows)...");
            var rand = new Random(42);
            using (var sw = new StreamWriter("TestData/test_stress_BigTable.csv"))
            {
                sw.WriteLine("ID,Value,Data");
                for (int i = 1; i <= bigCount; i++)
                {
                    int smallId = rand.Next(1, 1500); // Some will match, some won't
                    sw.WriteLine($"{smallId},Val_{i},RandomData_{rand.Next(1000, 9999)}");
                    
                    if (i % 500000 == 0) Console.WriteLine($"... written {i} rows");
                }
            }
            Console.WriteLine("Done.");
        }
    }
}
