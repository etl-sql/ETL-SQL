using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Functions;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Engine.Functions
{
    public static partial class StandardFunctions
    {
        private static void RegisterMathFunctions(IFunctionRegistry registry)
        {
            registry.RegisterWithHelp("ABS", (args, ctx) => {
                if (args[0].IsNull()) return null;
                if (!decimal.TryParse(args[0]?.ToString(), out var n)) return null;
                return Math.Abs(n);
            }, "ABS(n): Returns the absolute value of a number. Returns NULL on non-numeric input.");
            
            registry.RegisterWithHelp("ROUND", Round, "ROUND(numeric, decimals): Rounds a numeric value to a specified number of decimal places.");
            registry.RegisterWithHelp("CEILING", (args, ctx) => args[0] == null ? null : (decimal.TryParse(args[0]?.ToString(), out var n) ? Math.Ceiling(n) : null), "CEILING(n): Returns the smallest integer greater than or equal to the number.");
            registry.RegisterWithHelp("FLOOR", (args, ctx) => args[0] == null ? null : (decimal.TryParse(args[0]?.ToString(), out var n) ? Math.Floor(n) : null), "FLOOR(n): Returns the largest integer less than or equal to the number.");
            
            registry.RegisterWithHelp("SQRT", (args, ctx) => {
                if (args[0] == null) return null;
                if (!double.TryParse(args[0]?.ToString(), out var d)) return null;
                if (d < 0) return null;
                return (decimal)Math.Sqrt(d);
            }, "SQRT(n): Returns the square root of a number. Returns NULL for negative inputs.");
            
            registry.RegisterWithHelp("POWER", (args, ctx) => {
                if (args.Count < 2 || args[0] == null || args[1] == null) return null;
                if (!double.TryParse(args[0]?.ToString(), out var b)) return null;
                if (!double.TryParse(args[1]?.ToString(), out var p)) return null;
                if (b == 0 && p < 0) return null;
                return (decimal)Math.Pow(b, p);
            }, "POWER(base, exp): Returns the result of a base raised to an exponent.");
            
            registry.RegisterWithHelp("MOD", (args, ctx) => args.Count >= 2 && args[0] != null && args[1] != null ? (decimal.TryParse(args[0]?.ToString(), out var n1) && decimal.TryParse(args[1]?.ToString(), out var n2) && n2 != 0 ? n1 % n2 : null) : null, "MOD(n, d): Returns the remainder of a division.");
            
            registry.RegisterWithHelp("EXP", (args, ctx) => args[0] == null ? null : (decimal)Math.Exp(Convert.ToDouble(args[0])), "EXP(n): Returns e raised to the power of n.");
            registry.RegisterWithHelp("LOG", (args, ctx) => args.Count >= 2 ? (decimal)Math.Log(Convert.ToDouble(args[0]), Convert.ToDouble(args[1])) : (args[0] == null ? null : (decimal)Math.Log(Convert.ToDouble(args[0]))), "LOG(n[, base]): Returns the logarithm of n.");
            registry.RegisterWithHelp("LOG10", (args, ctx) => args[0] == null ? null : (decimal)Math.Log10(Convert.ToDouble(args[0])), "LOG10(n): Returns the base-10 logarithm of n.");
            registry.RegisterWithHelp("RAND", (args, ctx) => (decimal)_random.NextDouble(), "RAND([seed]): Returns a random number between 0 and 1.");
            
            registry.RegisterWithHelp("SIN", (args, ctx) => args[0] == null ? null : (decimal)Math.Sin(Convert.ToDouble(args[0])), "SIN(f): Sine (input in radians).");
            registry.RegisterWithHelp("COS", (args, ctx) => args[0] == null ? null : (decimal)Math.Cos(Convert.ToDouble(args[0])), "COS(f): Cosine (input in radians).");
            registry.RegisterWithHelp("TAN", (args, ctx) => args[0] == null ? null : (decimal)Math.Tan(Convert.ToDouble(args[0])), "TAN(f): Tangent (input in radians).");
            registry.RegisterWithHelp("ASIN", (args, ctx) => args[0] == null ? null : (decimal)Math.Asin(Convert.ToDouble(args[0])), "ASIN(f): Inverse Sine (returns radians).");
            registry.RegisterWithHelp("ACOS", (args, ctx) => args[0] == null ? null : (decimal)Math.Acos(Convert.ToDouble(args[0])), "ACOS(f): Inverse Cosine (returns radians).");
            registry.RegisterWithHelp("ATAN", (args, ctx) => args[0] == null ? null : (decimal)Math.Atan(Convert.ToDouble(args[0])), "ATAN(f): Inverse Tangent (returns radians).");
            registry.RegisterWithHelp("ATAN2", (args, ctx) => args.Count >= 2 ? (decimal)Math.Atan2(Convert.ToDouble(args[0]), Convert.ToDouble(args[1])) : null, "ATAN2(y, x): Returns the angle in radians between the x-axis and (x, y).");
            registry.RegisterWithHelp("SIGN", (args, ctx) => args[0] == null ? null : (decimal)Math.Sign(Convert.ToDecimal(args[0])), "SIGN(n): Returns the sign of a number (1, -1, or 0).");
            
            registry.RegisterWithHelp("SUM", Sum, "SUM(expression): Returns the sum of values in a collection.");
            registry.RegisterWithHelp("AVG", Avg, "AVG(expression): Returns the average of values in a collection.");
            registry.RegisterWithHelp("MIN", Min, "MIN(expression): Returns the minimum value in a collection.");
            registry.RegisterWithHelp("MAX", Max, "MAX(expression): Returns the maximum value in a collection.");
            registry.RegisterWithHelp("STDDEV", StdDev, "STDDEV(expression): Returns the statistical deviation.");
            registry.RegisterWithHelp("VAR", Variance, "VAR(expression): Returns the statistical variance.");

            // Bitwise Functions
            registry.RegisterWithHelp("BITAND", (args, ctx) => {
                if (args.Count < 2 || args[0] == null || args[1] == null) return null;
                return (decimal)(Convert.ToInt64(args[0]) & Convert.ToInt64(args[1]));
            }, "BITAND(a, b): Performs a bitwise AND operation on two integers.");

            registry.RegisterWithHelp("BITOR", (args, ctx) => {
                if (args.Count < 2 || args[0] == null || args[1] == null) return null;
                return (decimal)(Convert.ToInt64(args[0]) | Convert.ToInt64(args[1]));
            }, "BITOR(a, b): Performs a bitwise OR operation on two integers.");

            registry.RegisterWithHelp("BITXOR", (args, ctx) => {
                if (args.Count < 2 || args[0] == null || args[1] == null) return null;
                return (decimal)(Convert.ToInt64(args[0]) ^ Convert.ToInt64(args[1]));
            }, "BITXOR(a, b): Performs a bitwise XOR operation on two integers.");

            registry.RegisterWithHelp("BITNOT", (args, ctx) => {
                if (args.Count < 1 || args[0] == null) return null;
                return (decimal)(~Convert.ToInt64(args[0]));
            }, "BITNOT(a): Performs a bitwise NOT operation on an integer.");

            registry.RegisterWithHelp("BITSHIFTLEFT", (args, ctx) => {
                if (args.Count < 2 || args[0] == null || args[1] == null) return null;
                return (decimal)(Convert.ToInt64(args[0]) << Convert.ToInt32(args[1]));
            }, "BITSHIFTLEFT(a, n): Performs a bitwise left shift on 'a' by 'n' bits.");

            registry.RegisterWithHelp("BITSHIFTRIGHT", (args, ctx) => {
                if (args.Count < 2 || args[0] == null || args[1] == null) return null;
                return (decimal)(Convert.ToInt64(args[0]) >> Convert.ToInt32(args[1]));
            }, "BITSHIFTRIGHT(a, n): Performs a bitwise right shift on 'a' by 'n' bits.");

            registry.RegisterWithHelp("BIT_COUNT", (args, ctx) => {
                if (args.Count < 1 || args[0] == null) return null;
                long val = Convert.ToInt64(args[0]);
                return (decimal)System.Numerics.BitOperations.PopCount((ulong)val);
            }, "BIT_COUNT(a): Returns the number of set bits (popcount) in the integer.");

            // Trigonometric / Math Constants
            registry.RegisterWithHelp("PI", (args, ctx) => (decimal)Math.PI, "PI(): Returns the value of PI.");

            registry.RegisterWithHelp("DEGREES", (args, ctx) => {
                if (args.Count < 1 || args[0] == null) return null;
                double rad = Convert.ToDouble(args[0]);
                return (decimal)(rad * (180.0 / Math.PI));
            }, "DEGREES(radians): Converts radians to degrees.");

            registry.RegisterWithHelp("RADIANS", (args, ctx) => {
                if (args.Count < 1 || args[0] == null) return null;
                double deg = Convert.ToDouble(args[0]);
                return (decimal)(deg * (Math.PI / 180.0));
            }, "RADIANS(degrees): Converts degrees to radians.");

            registry.RegisterWithHelp("COT", (args, ctx) => {
                if (args.Count < 1 || args[0] == null) return null;
                double val = Convert.ToDouble(args[0]);
                double tan = Math.Tan(val);
                if (tan == 0) return null;
                return (decimal)(1.0 / tan);
            }, "COT(n): Returns the cotangent of a number.");
        }

        private static object? Round(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 1 || args[0] == null) return null;
            if (!decimal.TryParse(args[0]?.ToString(), out var n)) return null;
            int decimals = args.Count >= 2 && int.TryParse(args[1]?.ToString(), out var d) ? d : 0;
            return Math.Round(n, decimals, MidpointRounding.AwayFromZero);
        }

        private static IEnumerable<decimal> GetNumbers(object? arg)
        {
            if (arg is IEnumerable<object?> enumerable)
                return enumerable.Where(x => x != null).Select(x => Convert.ToDecimal(x));
            if (arg != null)
                return new[] { Convert.ToDecimal(arg) };
            return Enumerable.Empty<decimal>();
        }

        private static object? Sum(List<object?> args, IExecutionContext ctx)
        {
            var nums = GetNumbers(args.FirstOrDefault());
            return nums.Any() ? nums.Sum() : (decimal)0;
        }

        private static object? Avg(List<object?> args, IExecutionContext ctx)
        {
            var nums = GetNumbers(args.FirstOrDefault());
            return nums.Any() ? nums.Average() : (decimal)0;
        }

        private static object? Min(List<object?> args, IExecutionContext ctx)
        {
            var nums = GetNumbers(args.FirstOrDefault());
            return nums.Any() ? nums.Min() : null;
        }

        private static object? Max(List<object?> args, IExecutionContext ctx)
        {
            var nums = GetNumbers(args.FirstOrDefault());
            return nums.Any() ? nums.Max() : null;
        }

        private static object? StdDev(List<object?> args, IExecutionContext ctx)
        {
            var nums = GetNumbers(args.FirstOrDefault()).ToList();
            if (nums.Count < 2) return (decimal)0;
            double avg = (double)nums.Average();
            double sum = nums.Sum(d => Math.Pow((double)d - avg, 2));
            return (decimal)Math.Sqrt(sum / (nums.Count - 1));
        }

        private static object? Variance(List<object?> args, IExecutionContext ctx)
        {
            var nums = GetNumbers(args.FirstOrDefault()).ToList();
            if (nums.Count < 2) return (decimal)0;
            double avg = (double)nums.Average();
            double sum = nums.Sum(d => Math.Pow((double)d - avg, 2));
            return (decimal)(sum / (nums.Count - 1));
        }
    }
}
