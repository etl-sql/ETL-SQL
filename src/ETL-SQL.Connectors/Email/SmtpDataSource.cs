using ETL_SQL.Common;
using ETL_SQL.Data;
using MailKit.Net.Smtp;
using MimeKit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ETL_SQL.Connectors.Email
{
    /// <summary>
    /// Data source implementation for sending emails via SMTP.
    /// </summary>
    public class SmtpDataSource : IDataSource
    {
        private readonly Dictionary<string, string> _options;

        /// <summary>Gets the SMTP host from the connection options.</summary>
        public string Path => _options.TryGetValue("HOST", out var h) ? h : "localhost";

        /// <summary>The options used to create this data source.</summary>
        public Dictionary<string, string>? Options => _options;
        /// <summary>The type name of the connector (SMTP).</summary>
        public string ConnectorType => "SMTP";

        /// <summary>
        /// Initializes a new instance of the <see cref="SmtpDataSource"/> class.
        /// </summary>
        /// <param name="options">SMTP configuration options (HOST, PORT, USERNAME, etc.).</param>
        public SmtpDataSource(Dictionary<string, string> options)
        {
            _options = options;
        }

        /// <summary>Reading batches is not supported for SMTP.</summary>
        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            yield break; // Read not supported
        }

        /// <summary>Writes batches of data by sending each row as an email.</summary>
        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches)
        {
            await foreach (var batch in batches)
            {
                foreach (var row in batch.Rows)
                {
                    await SendEmail(row);
                }
            }
        }

        /// <summary>Sends a single email based on the data in the provided row.</summary>
        /// <param name="row">Row containing email fields (To, Cc, Subject, Body, etc.).</param>
        public async Task SendEmail(Row row)
        {
            var message = new MimeMessage();
            
            string from = (row.Columns.TryGetValue("From", out var f) ? f?.ToString() : (_options.TryGetValue("DEFAULT_FROM", out var df) ? df : "etl-sql@localhost")) ?? "etl-sql@localhost";
            message.From.Add(MailboxAddress.Parse(from));

            if (row.Columns.TryGetValue("To", out var to))
            {
                var addrList = to?.ToString()?.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
                foreach (var addr in addrList.Select(a => a.Trim()))
                    if (!string.IsNullOrEmpty(addr)) message.To.Add(MailboxAddress.Parse(addr));
            }

            if (row.Columns.TryGetValue("Cc", out var cc))
            {
                var addrList = cc?.ToString()?.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
                foreach (var addr in addrList.Select(a => a.Trim()))
                    if (!string.IsNullOrEmpty(addr)) message.Cc.Add(MailboxAddress.Parse(addr));
            }

            if (row.Columns.TryGetValue("Bcc", out var bcc))
            {
                var addrList = bcc?.ToString()?.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
                foreach (var addr in addrList.Select(a => a.Trim()))
                    if (!string.IsNullOrEmpty(addr)) message.Bcc.Add(MailboxAddress.Parse(addr));
            }

            message.Subject = (row.Columns.TryGetValue("Subject", out var sub) ? sub?.ToString() : "") ?? "";
            
            var body = new TextPart("plain")
            {
                Text = (row.Columns.TryGetValue("Body", out var b) ? b?.ToString() : "") ?? ""
            };

            // Handle Attachments
            if (row.Columns.TryGetValue("Attachments", out var att) && att != null)
            {
                var multipart = new Multipart("mixed");
                multipart.Add(body);

                var paths = att.ToString()?.Split(',', ';').Select(p => p.Trim());
                if (paths != null)
                {
                    foreach (var path in paths)
                    {
                        if (System.IO.File.Exists(path))
                        {
                            var attachment = new MimePart()
                            {
                                Content = new MimeContent(System.IO.File.OpenRead(path)),
                                ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                                ContentTransferEncoding = ContentEncoding.Base64,
                                FileName = System.IO.Path.GetFileName(path)
                            };
                            multipart.Add(attachment);
                        }
                    }
                }
                message.Body = multipart;
            }
            else
            {
                message.Body = body;
            }

            using var client = new SmtpClient();
            string host = _options.TryGetValue("HOST", out var h) ? h : "localhost";
            int port = _options.TryGetValue("PORT", out var p) && int.TryParse(p, out var pt) ? pt : 25;
            bool useSsl = _options.TryGetValue("USE_SSL", out var ssl) && bool.TryParse(ssl, out var s) && s;

            await client.ConnectAsync(host, port, useSsl);

            if (_options.TryGetValue("USERNAME", out var user) && _options.TryGetValue("PASSWORD", out var pass))
            {
                await client.AuthenticateAsync(user, pass);
            }

            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }

        /// <summary>Trunction is not applicable for SMTP.</summary>
        public Task TruncateAsync() => Task.CompletedTask;

        /// <summary>Returns the virtual column names recognized for email sending.</summary>
        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult(new[] { "To", "Cc", "Bcc", "Subject", "Body", "Attachments" }.AsEnumerable());

        /// <summary>Captures a snapshot (no-op for SMTP).</summary>
        public object? Snapshot() => null;

        /// <summary>Restores from a snapshot (no-op for SMTP).</summary>
        public void Restore(object? snapshot) { }

        /// <summary>Returns this instance as a typed table.</summary>
        public IDataSource WithTable(string tableName) => this;

        public async ValueTask DisposeAsync()
        {
            await Task.CompletedTask;
        }
    }
}
