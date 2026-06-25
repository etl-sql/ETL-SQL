using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ETL_SQL.Core;
public class DataGenerator
{
    private readonly int? _seed;
    private readonly Random _random;

    public DataGenerator(int? seed = null)
    {
        _seed = seed;
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    public IEnumerable<Dictionary<string, object?>> GenerateRows(int rowCount, List<GenerateRule> rules)
    {
        var generators = rules.Select(r => new { r.ColumnName, Generator = CreateGenerator(r.Rule) }).ToList();

        for (int i = 0; i < rowCount; i++)
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in generators)
            {
                try
                {
                    row[g.ColumnName] = g.Generator(i);
                }
                catch
                {
                    row[g.ColumnName] = null;
                }
            }
            yield return row;
        }
    }

    private Func<int, object?> CreateGenerator(string rule)
    {
        var match = Regex.Match(rule.Trim(), @"^(\w+)\((.*)\)$");
        if (!match.Success)
        {
            // Literal fallback — handles raw strings or values
            return _ => rule.Trim('\'', '\"');
        }

        string funcName = match.Groups[1].Value.ToUpperInvariant();
        string[] args = ParseArgs(match.Groups[2].Value);

        switch (funcName)
        {
            case "SEQUENCE":
                return CreateSequenceGenerator(args);
            case "RANDOM_INT":
                return CreateRandomIntGenerator(args);
            case "RANDOM_DECIMAL":
                return CreateRandomDecimalGenerator(args);
            case "RANDOM":
                return CreateRandomValueGenerator(args);
            default:
                return _ => rule; // Fallback
        }
    }

    private string[] ParseArgs(string argsContent)
    {
        if (string.IsNullOrWhiteSpace(argsContent)) return Array.Empty<string>();

        var args = new List<string>();
        bool inQuotes = false;
        int start = 0;

        for (int i = 0; i < argsContent.Length; i++)
        {
            if (argsContent[i] == '\'' || argsContent[i] == '\"') inQuotes = !inQuotes;
            if (argsContent[i] == ',' && !inQuotes)
            {
                args.Add(argsContent.Substring(start, i - start).Trim());
                start = i + 1;
            }
        }
        args.Add(argsContent.Substring(start).Trim());

        return args.ToArray();
    }

    private Func<int, object?> CreateSequenceGenerator(string[] args)
    {
        // SEQUENCE(start, step, unit)
        if (args.Length == 0) return index => index;

        string startStr = args[0].Trim('\'', '\"');
        decimal step = args.Length > 1 && decimal.TryParse(args[1], out var s) ? s : 1;
        string unit = args.Length > 2 ? args[2].ToUpperInvariant().Trim('\'', '\"') : "";

        if (DateTime.TryParse(startStr, out var startDate))
        {
            return index =>
            {
                double currentOffset = (double)(index * step);
                switch (unit)
                {
                    case "DAY": case "DAYS": return startDate.AddDays(currentOffset);
                    case "MONTH": case "MONTHS": return startDate.AddMonths((int)currentOffset);
                    case "YEAR": case "YEARS": return startDate.AddYears((int)currentOffset);
                    case "HOUR": case "HOURS": return startDate.AddHours(currentOffset);
                    case "MINUTE": case "MINUTES": return startDate.AddMinutes(currentOffset);
                    case "SECOND": case "SECONDS": return startDate.AddSeconds(currentOffset);
                    default: return startDate.AddDays(currentOffset);
                }
            };
        }

        if (decimal.TryParse(startStr, out var startNum))
        {
            return index => startNum + (index * step);
        }

        return index => index;
    }

    private Func<int, object?> CreateRandomIntGenerator(string[] args)
    {
        int min = args.Length > 0 && int.TryParse(args[0], out var low) ? low : 0;
        int max = args.Length > 1 && int.TryParse(args[1], out var high) ? high : int.MaxValue;
        return _ => _random.Next(min, max + 1);
    }

    private Func<int, object?> CreateRandomDecimalGenerator(string[] args)
    {
        double min = args.Length > 0 && double.TryParse(args[0], out var low) ? low : 0;
        double max = args.Length > 1 && double.TryParse(args[1], out var high) ? high : 100;
        return _ => (decimal)(_random.NextDouble() * (max - min) + min);
    }

    private Func<int, object?> CreateRandomValueGenerator(string[] args)
    {
        var values = args.Select(v => v.Trim('\'', '\"')).ToArray();
        if (values.Length == 0) return _ => null;
        return _ => values[_random.Next(0, values.Length)];
    }

    // Support for stress tests
    public static void Generate(int count = 1000000)
    {
        // 1. BigTable
        string bigPath = "TestData/test_stress_BigTable.csv";
        using (var sw = new System.IO.StreamWriter(bigPath))
        {
            sw.WriteLine("ID,Value,Data");
            var rnd = new Random();
            for (int i = 1; i <= count; i++)
                sw.WriteLine($"{rnd.Next(1, 1500)},Val_{i},RandomData_{rnd.Next(1000, 9999)}");
        }

        // 2. SmallTable (Expected by tests)
        string smallPath = "TestData/test_stress_SmallTable.csv";
        using (var sw = new System.IO.StreamWriter(smallPath))
        {
            sw.WriteLine("ID,Name");
            for (int i = 1; i <= 1000; i++)
                sw.WriteLine($"{i},User_{i}");
        }
    }
}
