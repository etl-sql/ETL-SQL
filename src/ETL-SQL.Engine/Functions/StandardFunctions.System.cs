using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Functions;
using ETL_SQL.Core.Governance;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Functions
{
    public static partial class StandardFunctions
    {
        /// <summary>
        /// Resolves and authorizes a script-selected local path at the canonical filesystem
        /// policy boundary (safe zones plus enterprise approved roots and policy freshness).
        /// </summary>
        private static string AuthorizeLocalPath(IExecutionContext context, string rawPath) =>
            new FileSystemPolicyAuthorizer(context.SecurityService)
                .Authorize(context, context.ResolvePath(rawPath), FileSystemAccessKind.Read, validateFileType: false)
                .CanonicalPath;

        private static void RegisterSystemFunctions(IFunctionRegistry registry)
        {
            registry.RegisterWithHelp("APPEND_TO_LIST", AddToList, "APPEND_TO_LIST(@list, value): Adds an item to a list variable. Returns the new list.");
            registry.RegisterWithHelp("ADD_TO_LIST", AddToList, "ADD_TO_LIST(@list, value): Alias for APPEND_TO_LIST.");
            registry.RegisterWithHelp("REMOVE_FROM_LIST", RemoveFromList, "REMOVE_FROM_LIST(@list, value): Removes all occurrences of a value from a list variable.");
            registry.RegisterWithHelp("SORT_LIST", SortList, "SORT_LIST(list[, 'ASC'|'DESC']): Returns a sorted version of the list.");

            registry.RegisterWithHelp("CAST", (args, ctx) => args.Count >= 2 ? EvaluationUtils.CastToType(args[0], args[1]?.ToString() ?? "STRING") : args[0], "CAST(expr AS type): Converts an expression to a target data type.");
            registry.RegisterWithHelp("COUNT", Count, "COUNT(col): Returns the number of items in a collection.");
            registry.RegisterWithHelp("GENERATE_SERIES", GenerateSeries, "GENERATE_SERIES(start, stop[, step]): Generates a series of numbers.");
            registry.RegisterWithHelp("UNNEST", Unnest, "UNNEST(list): Table-valued — expands a list/array into one 'Value' row per element. Use in FROM or CROSS APPLY.");
            registry.RegisterWithHelp("FLATTEN", Flatten, "FLATTEN(list): Like UNNEST but flattens one level of nested lists.");
            registry.RegisterWithHelp("FILE_EXISTS", (args, ctx) => args.Count >= 1 && args[0] != null ? System.IO.File.Exists(AuthorizeLocalPath(ctx, args[0]?.ToString() ?? "")) : false, "FILE_EXISTS(path): Returns TRUE if the file exists.");
            registry.RegisterWithHelp("DIRECTORY_EXISTS", (args, ctx) => args.Count >= 1 && args[0] != null ? System.IO.Directory.Exists(AuthorizeLocalPath(ctx, args[0]?.ToString() ?? "")) : false, "DIRECTORY_EXISTS(path): Returns TRUE if the directory exists.");

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

            registry.RegisterWithHelp("ENV", (args, ctx) =>
            {
                string? name = args.FirstOrDefault()?.ToString();
                if (string.IsNullOrEmpty(name)) return null;
                ctx.SecurityService.ValidateEnvVar(name);
                return Environment.GetEnvironmentVariable(name);
            }, "ENV('VAR_NAME'): Returns the value of a host environment variable (subject to security allow-list).");

            registry.RegisterWithHelp("CONNECTION_PROPERTY", ConnectionProperty, "CONNECTION_PROPERTY(conn_name, prop_name): Returns the value of a connection property, masking sensitive properties.");

            // Row-level security predicates. Read the host-injected identity; fail closed (FALSE)
            // when no identity was injected. Admins bypass by default. See Docs/Design/RowLevelSecurity.md.
            registry.RegisterWithHelp("HAS_GROUP", (args, ctx) =>
            {
                var name = args.FirstOrDefault()?.ToString();
                return ctx.ExecutionIdentity is { } id && !string.IsNullOrEmpty(name) && id.EffectiveHasGroup(name);
            }, "HAS_GROUP('name'): TRUE if the current user belongs to the group (row-level security). Admins bypass by default; FALSE when no identity is present.");

            registry.RegisterWithHelp("HAS_ROLE", (args, ctx) =>
            {
                var name = args.FirstOrDefault()?.ToString();
                return ctx.ExecutionIdentity is { } id && !string.IsNullOrEmpty(name) && id.EffectiveHasRole(name);
            }, "HAS_ROLE('name'): TRUE if the current user holds the role (row-level security). Admins bypass by default; FALSE when no identity is present.");

            registry.RegisterWithHelp("USER_GROUPS", UserGroups,
                "USER_GROUPS(): Table-valued — one 'Value' row per group the current user belongs to. Use in WHERE col IN (SELECT Value FROM USER_GROUPS()). Empty when no identity is present.");
            registry.RegisterWithHelp("USER_ROLES", UserRoles,
                "USER_ROLES(): Table-valued — one 'Value' row per role the current user holds. Empty when no identity is present.");

            registry.RegisterWithHelp("GET_JOB_STATE", async (args, ctx) =>
            {
                if (args.Count < 1) throw new ExecutionException("GET_JOB_STATE requires at least 1 argument (key)");
                string? key = args[0]?.ToString();
                if (string.IsNullOrEmpty(key)) return null;

                if (!string.IsNullOrEmpty(ctx.JobName))
                {
                    var store = ctx.ServiceProvider.GetService(typeof(Core.Data.IJobHistoryStore)) as Core.Data.IJobHistoryStore;
                    if (store != null)
                    {
                        return await store.GetJobStateAsync(ctx.JobName, key);
                    }
                }
                else
                {
                    return GetLocalJobState(ctx, key);
                }
                return null;
            }, "GET_JOB_STATE(key): Returns the saved state value for the current script/job execution context.");

            registry.RegisterWithHelp("SET_JOB_STATE", (args, ctx) =>
            {
                if (args.Count < 2) throw new ExecutionException("SET_JOB_STATE requires 2 arguments (key, value)");
                string? key = args[0]?.ToString();
                string? value = args[1]?.ToString();
                if (string.IsNullOrEmpty(key)) return null;

                ctx.PendingJobStateUpdates[key] = value ?? "";
                return value;
            }, "SET_JOB_STATE(key, value): Sets the saved state value for the current script/job context (committed only on successful execution).");
        }

        private static string? GetLocalJobState(IExecutionContext ctx, string key)
        {
            if (string.IsNullOrEmpty(ctx.CurrentScriptPath)) return null;
            try
            {
                var stateFile = System.IO.Path.ChangeExtension(ctx.CurrentScriptPath, ".etlstate");
                if (System.IO.File.Exists(stateFile))
                {
                    var text = System.IO.File.ReadAllText(stateFile);
                    var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(text);
                    if (dict != null && dict.TryGetValue(key, out var val))
                    {
                        return val;
                    }
                }
            }
            catch (Exception ex)
            {
                ctx.Logger.Warning("Failed to read local job state: " + ex.Message);
            }
            return null;
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

        /// <summary>USER_GROUPS() — table-valued; one 'Value' row per group in the injected identity.</summary>
        private static async Task<object?> UserGroups(List<object?> args, IExecutionContext ctx)
            => await IdentityValuesTable(ctx.ExecutionIdentity?.Groups);

        /// <summary>USER_ROLES() — table-valued; one 'Value' row per role in the injected identity.</summary>
        private static async Task<object?> UserRoles(List<object?> args, IExecutionContext ctx)
            => await IdentityValuesTable(ctx.ExecutionIdentity?.Roles);

        private static async Task<DataTable> IdentityValuesTable(IEnumerable<string>? values)
        {
            var dt = new DataTable();
            dt.SetColumns(new[] { "Value" });
            if (values is not null)
                foreach (var value in values)
                    await dt.AddRowAsync(new Row { ["Value"] = value });
            return dt;
        }

        /// <summary>UNNEST(list) — table-valued; one 'Value' row per list element.</summary>
        private static async Task<object?> Unnest(List<object?> args, IExecutionContext ctx)
        {
            var dt = new DataTable();
            dt.SetColumns(new[] { "Value" });
            if (args.Count > 0) await AddUnnestRows(dt, args[0], flatten: false);
            return dt;
        }

        /// <summary>FLATTEN(list) — like UNNEST but flattens one level of nested lists.</summary>
        private static async Task<object?> Flatten(List<object?> args, IExecutionContext ctx)
        {
            var dt = new DataTable();
            dt.SetColumns(new[] { "Value" });
            if (args.Count > 0) await AddUnnestRows(dt, args[0], flatten: true);
            return dt;
        }

        private static async System.Threading.Tasks.Task AddUnnestRows(DataTable dt, object? value, bool flatten)
        {
            if (value == null) return;
            if (value is DataTable inner)
            {
                foreach (var r in inner.Rows) await dt.AddRowAsync(new Row { ["Value"] = r[0] });
                return;
            }
            if (value is System.Collections.IEnumerable en && value is not string)
            {
                foreach (var item in en)
                {
                    if (flatten && item is System.Collections.IEnumerable nested && item is not string)
                        foreach (var sub in nested) await dt.AddRowAsync(new Row { ["Value"] = sub });
                    else
                        await dt.AddRowAsync(new Row { ["Value"] = item });
                }
                return;
            }
            await dt.AddRowAsync(new Row { ["Value"] = value }); // scalar → single row
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
            try
            {
                return EvaluationUtils.CastToType(args[0], args[1]?.ToString() ?? "STRING");
            }
            catch
            {
                return null;
            }
        }

        private static object? ConnectionProperty(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2 || args[0] == null || args[1] == null) return null;
            string connName = args[0]!.ToString()!;
            string propName = args[1]!.ToString()!;

            if (!ctx.Connections.TryGetValue(connName, out var ds))
            {
                return null;
            }

            if (propName.Equals("PATH", StringComparison.OrdinalIgnoreCase))
            {
                return ds.Path;
            }
            if (propName.Equals("TYPE", StringComparison.OrdinalIgnoreCase) ||
                propName.Equals("CONNECTOR", StringComparison.OrdinalIgnoreCase) ||
                propName.Equals("CONNECTOR_TYPE", StringComparison.OrdinalIgnoreCase))
            {
                return ds.ConnectorType;
            }

            if (ds.Options != null)
            {
                var match = ds.Options.FirstOrDefault(kv => kv.Key.Equals(propName, StringComparison.OrdinalIgnoreCase));
                if (match.Key != null)
                {
                    bool isSensitive = match.Key.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase) ||
                                       match.Key.Contains("CONNECTIONSTRING", StringComparison.OrdinalIgnoreCase) ||
                                       match.Key.Contains("SECRET", StringComparison.OrdinalIgnoreCase) ||
                                       match.Key.Contains("APIKEY", StringComparison.OrdinalIgnoreCase) ||
                                       match.Key.Contains("API_KEY", StringComparison.OrdinalIgnoreCase) ||
                                       match.Key.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) ||
                                       match.Key.Contains("CREDENTIAL", StringComparison.OrdinalIgnoreCase) ||
                                       match.Key.Contains("PRIVATEKEY", StringComparison.OrdinalIgnoreCase) ||
                                       match.Key.Contains("KEYFILE", StringComparison.OrdinalIgnoreCase) ||
                                       match.Value.StartsWith("ENC:", StringComparison.OrdinalIgnoreCase);

                    if (isSensitive)
                    {
                        return "********";
                    }
                    return match.Value;
                }
            }

            return null;
        }
    }
}
