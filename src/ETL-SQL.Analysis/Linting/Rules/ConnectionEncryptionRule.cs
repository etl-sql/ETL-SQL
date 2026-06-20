using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Analysis.Linting.Rules
{
    /// <summary>
    /// Validates encryption settings in CREATE/ALTER CONNECTION statements.
    /// Ensures that if ENCRYPT=ON, a password or keyfile is provided, and that the algorithm is valid.
    /// </summary>
    public class ConnectionEncryptionRule : ILintRule
    {
        private readonly IGovernancePolicyRegistry _policies;

        public ConnectionEncryptionRule() : this(null)
        {
        }

        public ConnectionEncryptionRule(IGovernancePolicyRegistry? policies)
        {
            _policies = policies ?? GovernancePolicyRegistry.CreateDefault();
        }

        public string Name => "ConnectionEncryption";
        public string Description => "Validates that a password or SSH key is provided when ENCRYPT=ON for file connections.";

        private static readonly HashSet<string> FileConnectors = new(StringComparer.OrdinalIgnoreCase)
        {
            "FLATFILE", "EXCEL", "JSON", "XML", "PARQUET", "AVRO", "CSV"
        };

        // MD5/SHA1 are intentionally excluded: cryptographically broken, not allowed for encryption
        // key derivation (the runtime EncryptionOptions rejects them too).
        private static readonly HashSet<string> ValidAlgorithms = new(StringComparer.OrdinalIgnoreCase)
        {
            "SHA2_256", "SHA256", "SHA2_512", "SHA512"
        };

        public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            var results = new List<LintResult>();
            foreach (var statement in script.Statements)
            {
                AnalyzeStatement(statement, results);
            }
            return Task.FromResult<IEnumerable<LintResult>>(results);
        }

        private void AnalyzeStatement(Statement statement, List<LintResult> results)
        {
            if (statement is CreateConnectionStatement conn)
            {
                CheckConnection(conn, results);
            }

            if (statement is BlockStatement block)
            {
                foreach (var s in block.Statements) AnalyzeStatement(s, results);
            }
            else if (statement is IfStatement ifStmt)
            {
                AnalyzeStatement(ifStmt.IfBody, results);
                if (ifStmt.ElseIfClauses != null)
                    foreach (var ei in ifStmt.ElseIfClauses) AnalyzeStatement(ei.Body, results);
                if (ifStmt.ElseBody != null) AnalyzeStatement(ifStmt.ElseBody, results);
            }
            else if (statement is WhileStatement whileStmt)
            {
                AnalyzeStatement(whileStmt.Body, results);
            }
            else if (statement is ForStatement forStmt)
            {
                AnalyzeStatement(forStmt.Body, results);
            }
            else if (statement is ForeachStatement foreachStmt)
            {
                AnalyzeStatement(foreachStmt.Body, results);
            }
            else if (statement is TryCatchStatement tryCatch)
            {
                AnalyzeStatement(tryCatch.TryBody, results);
                AnalyzeStatement(tryCatch.CatchBody, results);
            }
        }

        private void CheckConnection(CreateConnectionStatement conn, List<LintResult> results)
        {
            CheckFileEncryption(conn, results);
            CheckPlaintextCredentials(conn, results);
        }

        private void CheckFileEncryption(CreateConnectionStatement conn, List<LintResult> results)
        {
            if (!FileConnectors.Contains(conn.ConnectionType ?? "")) return;
            if (conn.Options == null) return;

            string GetLiteral(Expression? expr) => expr is LiteralExpression lit ? lit.Value?.ToString() ?? "" : "";

            var encVal = GetLiteral(conn.Options.GetValueOrDefault("ENCRYPT"));
            bool isEncryptOn = encVal.Equals("ON", StringComparison.OrdinalIgnoreCase) || encVal.Equals("TRUE", StringComparison.OrdinalIgnoreCase);

            if (isEncryptOn)
            {
                bool hasPassword = conn.Options.ContainsKey("PASSWORD");
                bool hasKeyFile = conn.Options.ContainsKey("KEYFILE");

                if (!hasPassword && !hasKeyFile)
                {
                    results.Add(new LintResult
                    {
                        RuleName = Name,
                        Severity = LintSeverity.Error,
                        Message = $"Connection '{conn.ConnectionName}': ENCRYPT=ON requires either a PASSWORD or a KEYFILE.",
                        LineNumber = conn.Line,
                        ColumnNumber = conn.Column
                    });
                }

                if (conn.Options.TryGetValue("ALGORITHM", out var algoExpr))
                {
                    var algo = GetLiteral(algoExpr);
                    if (!string.IsNullOrEmpty(algo) && !ValidAlgorithms.Contains(algo))
                    {
                        results.Add(new LintResult
                        {
                            RuleName = Name,
                            Severity = LintSeverity.Error,
                            Message = $"Connection '{conn.ConnectionName}': Unsupported encryption algorithm '{algo}'. Supported: SHA256, SHA512.",
                            LineNumber = conn.Line,
                            ColumnNumber = conn.Column
                        });
                    }
                }
            }
        }

        private void CheckPlaintextCredentials(CreateConnectionStatement conn, List<LintResult> results)
        {
            // 1. Check Target (Connection String)
            if (conn.TargetExpression is LiteralExpression targetLit && targetLit.Value is string targetStr)
            {
                if (targetStr.Contains("Password=", StringComparison.OrdinalIgnoreCase) && !targetStr.Contains("ENC:", StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new LintResult
                    {
                        RuleName = Name,
                        Code = "SEC-PLAIN-CONN",
                        Severity = LintSeverity.Warning,
                        Message = $"Connection '{conn.ConnectionName}' contains a plaintext connection string. Use a Master Password to encrypt this for better security.",
                        LineNumber = conn.Line,
                        ColumnNumber = conn.Column,
                        PolicyDecision = PlaintextSecretDecision("connection string target")
                    });
                    return;
                }
            }

            // 2. Check Options (PASSWORD, API_KEY, APIKEY, CLIENT_SECRET, CLIENTSECRET)
            if (conn.Options != null)
            {
                var sensitiveKeys = new[] { "PASSWORD", "API_KEY", "APIKEY", "CLIENT_SECRET", "CLIENTSECRET", "SECRET_KEY", "SECRETKEY", "SASL_PASSWORD", "SASL_JAAS_CONFIG" };
                foreach (var key in sensitiveKeys)
                {
                    if (conn.Options.TryGetValue(key, out var valExpr) && valExpr is LiteralExpression valLit && valLit.Value is string valStr)
                    {
                        if (!string.IsNullOrEmpty(valStr) && !valStr.StartsWith("ENC:", StringComparison.OrdinalIgnoreCase))
                        {
                            var msg = key == "PASSWORD"
                                ? $"Connection '{conn.ConnectionName}' uses a plaintext password. Use a Master Password to encrypt this for better security."
                                : $"Connection '{conn.ConnectionName}' uses a plaintext password or credential ({key}). Use a Master Password to encrypt this for better security.";

                            results.Add(new LintResult
                            {
                                RuleName = Name,
                                Code = "SEC-PLAIN-CONN",
                                Severity = LintSeverity.Warning,
                                Message = msg,
                                LineNumber = conn.Line,
                                ColumnNumber = conn.Column,
                                PolicyDecision = PlaintextSecretDecision($"connector option {key}")
                            });
                        }
                    }
                }
            }
        }

        private GovernancePolicyDecision PlaintextSecretDecision(string action)
        {
            var policy = _policies.GetRequired("Engine:AllowPlaintextSecrets");
            return GovernancePolicyDecision.Violation(
                policy,
                action,
                "Plaintext connector secrets are forbidden by the central governance policy registry.");
        }
    }
}
