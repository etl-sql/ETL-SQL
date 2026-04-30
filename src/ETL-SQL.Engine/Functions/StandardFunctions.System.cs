using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Functions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Functions
{
    public static partial class StandardFunctions
    {
        private static void RegisterSystemFunctions(IFunctionRegistry registry)
        {
            registry.RegisterWithHelp("APPEND_TO_LIST", AddToList, "APPEND_TO_LIST(@list, value): Adds an item to a list variable. Returns the new list.");
            registry.RegisterWithHelp("ADD_TO_LIST", AddToList, "ADD_TO_LIST(@list, value): Alias for APPEND_TO_LIST.");
            registry.RegisterWithHelp("REMOVE_FROM_LIST", RemoveFromList, "REMOVE_FROM_LIST(@list, value): Removes all occurrences of a value from a list variable.");
            registry.RegisterWithHelp("SORT_LIST", SortList, "SORT_LIST(list[, 'ASC'|'DESC']): Returns a sorted version of the list.");
            
            registry.RegisterWithHelp("CAST", (args, ctx) => args.Count >= 2 ? EvaluationUtils.CastToType(args[0], args[1]?.ToString() ?? "STRING") : args[0], "CAST(expr AS type): Converts an expression to a target data type.");
            registry.RegisterWithHelp("COUNT", Count, "COUNT(col): Returns the number of items in a collection.");
            registry.RegisterWithHelp("GENERATE_SERIES", GenerateSeries, "GENERATE_SERIES(start, stop[, step]): Generates a series of numbers.");
            registry.RegisterWithHelp("FILE_EXISTS", (args, ctx) => args.Count >= 1 && args[0] != null ? System.IO.File.Exists(ctx.ResolvePath(args[0]?.ToString() ?? "")) : false, "FILE_EXISTS(path): Returns TRUE if the file exists.");
            registry.RegisterWithHelp("DIRECTORY_EXISTS", (args, ctx) => args.Count >= 1 && args[0] != null ? System.IO.Directory.Exists(ctx.ResolvePath(args[0]?.ToString() ?? "")) : false, "DIRECTORY_EXISTS(path): Returns TRUE if the directory exists.");
            
            registry.RegisterWithHelp("HASHBYTES", HashBytes, "HASHBYTES('algo', val): Returns a cryptographic hash (MD5, SHA1, SHA256, SHA512).");
            registry.RegisterWithHelp("NEWID", (args, ctx) => NewUuidV7(), "NEWID(): Returns a new unique identifier (UUID v7).");
            registry.RegisterWithHelp("NEWSEQUENTIALID", (args, ctx) => NewUuidV7(), "NEWSEQUENTIALID(): Returns a new sequential unique identifier.");
            registry.RegisterWithHelp("CHECKSUM", Checksum, "CHECKSUM(v1, v2, ...): Returns a hash of the input values.");
            registry.RegisterWithHelp("BINARY_CHECKSUM", Checksum, "BINARY_CHECKSUM(v1, v2, ...): Returns a binary-compatible hash.");
            
            registry.RegisterWithHelp("TRY_CAST", TryCast, "TRY_CAST(expr AS type): Converts to type or returns NULL on failure.");
            
            registry.RegisterWithHelp("ERROR_NUMBER", (args, ctx) => (ctx.ActiveException ?? ctx.LastError)?.Number ?? 0, "ERROR_NUMBER(): Returns the error number of the error that caused the CATCH block to run.");
            registry.RegisterWithHelp("ERROR_MESSAGE", (args, ctx) => (ctx.ActiveException ?? ctx.LastError)?.Message, "ERROR_MESSAGE(): Returns the message text of the error that caused the CATCH block to run.");
            registry.RegisterWithHelp("ERROR_SEVERITY", (args, ctx) => (ctx.ActiveException ?? ctx.LastError)?.Severity ?? 0, "ERROR_SEVERITY(): Returns the severity of the error that caused the CATCH block to run.");
            registry.RegisterWithHelp("ERROR_STATE", (args, ctx) => (ctx.ActiveException ?? ctx.LastError)?.State ?? 0, "ERROR_STATE(): Returns the state number of the error that caused the CATCH block to run.");
            registry.RegisterWithHelp("ERROR_LINE", (args, ctx) => (ctx.ActiveException ?? ctx.LastError)?.Line ?? 0, "ERROR_LINE(): Returns the line number where the error occurred.");
            
            registry.RegisterWithHelp("ENV", (args, ctx) => {
                string? name = args.FirstOrDefault()?.ToString();
                if (string.IsNullOrEmpty(name)) return null;
                ctx.SecurityService.ValidateEnvVar(name);
                return Environment.GetEnvironmentVariable(name);
            }, "ENV('VAR_NAME'): Returns the value of a host environment variable (subject to security allow-list).");
        }

        private static object? AddToList(List<object?> args, IExecutionContext ctx)
        {
            return args.Count >= 2 && args[0] is List<object?> alp ? alp.Concat(new[] { args[1] }).ToList() : args.FirstOrDefault();
        }

        private static object? RemoveFromList(List<object?> args, IExecutionContext ctx)
        {
            return args.Count >= 2 && args[0] is List<object?> rfl ? rfl.Where(x => !EvaluationUtils.IsSoftEqual(x, args[1])).ToList() : args.FirstOrDefault();
        }

        private static object? SortList(List<object?> args, IExecutionContext ctx)
        {
            return args.Count >= 1 && args[0] is List<object?> sl ? sl.OrderBy(x => x).ToList() : args.FirstOrDefault();
        }

        private static object? Count(List<object?> args, IExecutionContext ctx)
        {
            return (args[0] is System.Collections.ICollection ic) ? (decimal)ic.Count : (args[0] is System.Collections.IEnumerable ie && args[0] is not string ? (decimal)Enumerable.Count(ie.Cast<object>()) : (args[0] == null ? 0m : 1m));
        }

        private static object? GenerateSeries(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2) return null;
            long start = Convert.ToInt64(args[0]);
            long stop = Convert.ToInt64(args[1]);
            long step = args.Count >= 3 ? Convert.ToInt64(args[2]) : 1;
            
            var list = new List<object?>();
            for (long i = start; (step > 0 ? i <= stop : i >= stop); i += step)
            {
                list.Add(i);
                if (list.Count > 1000000) break;
            }
            return list;
        }

        private static object? HashBytes(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2) throw new ExecutionException("HASHBYTES requires 2 arguments");
            string algo = args[0]?.ToString()?.ToUpperInvariant() ?? "MD5";
            byte[] data = System.Text.Encoding.UTF8.GetBytes(args[1]?.ToString() ?? "");
            using (System.Security.Cryptography.HashAlgorithm hash = algo switch
            {
                "MD5" => System.Security.Cryptography.MD5.Create(),
                "SHA1" => System.Security.Cryptography.SHA1.Create(),
                "SHA2_256" or "SHA256" => System.Security.Cryptography.SHA256.Create(),
                "SHA2_512" or "SHA512" => System.Security.Cryptography.SHA512.Create(),
                _ => throw new ExecutionException($"Unsupported hash algorithm: {algo}")
            })
            {
                return hash.ComputeHash(data);
            }
        }

        private static Guid NewUuidV7() => Guid.CreateVersion7();

        private static object? Checksum(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 1) return 0L;
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] h = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("|", args.Select(a => a?.ToString() ?? "NULL"))));
                return BitConverter.ToInt64(h, 0);
            }
        }

        private static object? TryCast(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2) return null;
            try {
                return EvaluationUtils.CastToType(args[0], args[1]?.ToString() ?? "STRING");
            } catch {
                return null;
            }
        }
    }
}
