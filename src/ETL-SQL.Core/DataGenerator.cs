using System;
using System.IO;

namespace ETL_SQL
{
    public static class DataGenerator
    {
        public static void Generate(int count = 1000000)
        {
            Console.WriteLine($"Generating {count:N0} row CSV data...");
            string filePath = "TestData/test_stress_BigTable.csv";
            
            using (StreamWriter sw = new StreamWriter(filePath))
            {
                sw.WriteLine("ID,Value,Data");
                Random rnd = new Random();
                for (int i = 1; i <= count; i++)
                {
                    int smallId = rnd.Next(1, 1500);
                    sw.WriteLine($"{smallId},Val_{i},RandomData_{rnd.Next(1000, 9999)}");
                    if (i % 500000 == 0) Console.WriteLine($"... written {i} rows");
                }
            }
            Console.WriteLine("Generating SmallTable.csv (1000 rows)...");
            string dictPath = "TestData/test_stress_SmallTable.csv";
            using (StreamWriter sw = new StreamWriter(dictPath))
            {
                sw.WriteLine("ID,Name");
                for (int i = 1; i <= 1000; i++)
                {
                    sw.WriteLine($"{i},User_{i}");
                }
            }
            Console.WriteLine("Finished generating mock data.");
        }
    }
}
