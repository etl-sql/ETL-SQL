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
        private readonly ILogger _logger;
        private readonly IExecutionContext? _context;

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
        /// <param name="logger">The logger instance.</param>
        public SmtpDataSource(IExecutionContext context, Dictionary<string, string> options)
        {
            _context = context;
            _options = options;
            _logger = context.Logger;

            // Security Hardening: egress control
            if (_options.TryGetValue("HOST", out var host))
            {
                context.SecurityService.ValidateHost(host);
            }
        }

        /// <summary>Reading batches is not supported for SMTP.</summary>
        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            yield break; // Read not supported
        }

        /// <summary>Writes batches of data by sending each row as an email.</summary>
        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false)
        {
            if (!append) await TruncateAsync();
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
            _context?.RecordSmtpEmailSend();

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
                        if (string.IsNullOrWhiteSpace(path))
                        {
                            continue;
                        }

                        var resolvedPath = _context?.ResolvePath(path) ?? path;
                        _context?.SecurityService.ValidatePath(resolvedPath);

                        if (System.IO.File.Exists(resolvedPath))
                        {
                            using var fs = System.IO.File.OpenRead(resolvedPath);
                            var ms = new System.IO.MemoryStream();
                            await fs.CopyToAsync(ms);
                            ms.Position = 0;

                            // COMPAT_BREAK: 0.10
                            var attachment = new MimePart(MimeTypes.GetMimeType(resolvedPath))
                            {
                                Content = new MimeContent(ms),
                                ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                                ContentTransferEncoding = ContentEncoding.Base64,
                                FileName = System.IO.Path.GetFileName(resolvedPath)
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
            int port = _options.TryGetValue("PORT", out var p) && int.TryParse(p, out var pt) ? pt : 587; // Security: Default to 587 (STARTTLS) instead of 25 (plaintext)
            bool useSsl = _options.TryGetValue("USE_SSL", out var ssl) && bool.TryParse(ssl, out var s) && s;

            try
            {
                await client.ConnectAsync(host, port, useSsl);

                if (_options.TryGetValue("USERNAME", out var user) && _options.TryGetValue("PASSWORD", out var pass))
                {
                    await client.AuthenticateAsync(user, pass);
                }

                await client.SendAsync(message);
                await client.DisconnectAsync(true);
            }
            catch (Exception ex) when (ex is not ETL_SQL.Core.Common.Exceptions.ExecutionException
                                       && ex is not ETL_SQL.Services.SecurityException)
            {
                throw new ETL_SQL.Core.Common.Exceptions.ExecutionException($"SMTP connector error: {ex.Message}", ex);
            }
        }

        public Task TruncateAsync() => Task.CompletedTask;

        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult(new[] { "To", "From", "Cc", "Bcc", "Subject", "Body", "Attachments" }.AsEnumerable());

        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }

        public IDataSource WithTable(string tableName) => this;

        public async ValueTask DisposeAsync()
        {
            await Task.CompletedTask;
        }
    }
}
