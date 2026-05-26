using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the SEND EMAIL statement, resolving recipients, content, and attachments, and sending via an SMTP datasource.
    /// </summary>
    public class EmailStatementHandler(ILogger logger) : IStatementHandler
    {
        private readonly ILogger _logger = logger;
        public Type SupportedStatementType => typeof(EmailStatement);


        /// <summary>Executes the SEND EMAIL statement, resolving all fields and invoking the SMTP provider.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (EmailStatement)statement;

            // 1. Resolve Connection
            string? connName = null;
            if (stmt.ConnectionName != null)
            {
                var connVal = await context.EvaluateValue(stmt.ConnectionName, new Row());
                connName = connVal?.ToString();
            }

            IDataSource? dataSource = null;
            if (!string.IsNullOrEmpty(connName))
            {
                if (!context.Connections.TryGetValue(connName!, out dataSource))
                {
                    throw new ExecutionException($"Connection '{connName}' not found.");
                }
            }
            else
            {
                // Try to find a default SMTP connection if only one exists
                var smtpConnections = context.Connections.Where(kv => kv.Key.Contains("smtp", StringComparison.OrdinalIgnoreCase) || kv.Key.Contains("email", StringComparison.OrdinalIgnoreCase)).ToList();
                if (smtpConnections.Count == 1)
                {
                    dataSource = smtpConnections[0].Value;
                }
                else if (smtpConnections.Count > 1)
                {
                    throw new ExecutionException("Multiple potential email connections found. Please specify one using AT.");
                }
                else
                {
                    throw new ExecutionException("No SMTP connection found. Create one using CREATE CONNECTION ... TYPE SMTP.");
                }
            }

            if (dataSource == null)
                throw new ExecutionException("Email destination (dataSource) not resolved.");

            var dummyRow = new Row();
            var toVal = await context.EvaluateValue(stmt.To, dummyRow);
            var fromVal = await context.EvaluateValue(stmt.From, dummyRow);
            var subjectVal = await context.EvaluateValue(stmt.Subject, dummyRow);
            var bodyVal = await context.EvaluateValue(stmt.Body, dummyRow);

            var row = new Row();
            row["To"] = toVal?.ToString() ?? "";
            row["From"] = fromVal?.ToString() ?? "";
            row["Subject"] = subjectVal?.ToString() ?? "";
            row["Body"] = bodyVal?.ToString() ?? "";

            if (stmt.Cc != null)
            {
                var ccList = new List<string>();
                foreach (var ccExpr in stmt.Cc) ccList.Add((await context.EvaluateValue(ccExpr, dummyRow))?.ToString() ?? "");
                row["Cc"] = string.Join(";", ccList);
            }

            if (stmt.Bcc != null)
            {
                var bccList = new List<string>();
                foreach (var bccExpr in stmt.Bcc) bccList.Add((await context.EvaluateValue(bccExpr, dummyRow))?.ToString() ?? "");
                row["Bcc"] = string.Join(";", bccList);
            }

            if (stmt.Attachments != null)
            {
                var attList = new List<string>();
                foreach (var attExpr in stmt.Attachments) attList.Add((await context.EvaluateValue(attExpr, dummyRow))?.ToString() ?? "");
                row["Attachments"] = string.Join(";", attList);
            }

            // 3. Send via WriteBatches
            _logger.Debug("Sending email to {To} via {ConnName}", row["To"], connName ?? "default SMTP");
            
            ValidateEmails(row["To"]?.ToString());
            if (row.Columns.TryGetValue("Cc", out var cc)) ValidateEmails(cc?.ToString());
            if (row.Columns.TryGetValue("Bcc", out var bcc)) ValidateEmails(bcc?.ToString());

            if (context.IsWhatIf)
            {
                _logger.WriteLine($"WHAT IF: Would send email to {row["To"]} with subject '{row["Subject"]}'", ConsoleColor.Yellow);
                return;
            }

            var dt = new DataTable();
            dt.SetColumns(row.Columns.Keys);
            await dt.AddRowAsync(row);
            await dataSource.WriteBatches(new[] { dt }.ToAsyncEnumerable());
        }

        private void ValidateEmails(string? emails)
        {
            if (string.IsNullOrWhiteSpace(emails)) return;
            var parts = emails.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var email in parts)
            {
                var trimmed = email.Trim();
                if (!System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
                {
                    throw new ExecutionException($"Invalid email format: '{trimmed}'");
                }
            }
        }
    }
}
