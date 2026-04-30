using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using Newtonsoft.Json.Linq;

namespace ETL_SQL.LSP
{
    /// <summary>
    /// Provides CodeActions (Quick Fixes) for the Language Server.
    /// Currently handles SEC-PLAIN-CONN by offering a command to secure connections.
    /// </summary>
    public class CodeActionProvider : ICodeActionHandler
    {
        private readonly ILogger<CodeActionProvider> _logger;

        public CodeActionProvider(ILogger<CodeActionProvider> logger)
        {
            _logger = logger;
        }

        public Task<CommandOrCodeActionContainer?> Handle(CodeActionParams request, CancellationToken cancellationToken)
        {
            var results = new List<CommandOrCodeAction>();

            foreach (var diagnostic in request.Context.Diagnostics)
            {
                // Identification by RuleName or specific Code
                if (diagnostic.Code?.String == "SEC-PLAIN-CONN" || diagnostic.Code?.String == "ConnectionEncryption")
                {
                    results.Add(new CodeAction
                    {
                        Title = "Secure connection credentials...",
                        Kind = CodeActionKind.QuickFix,
                        Diagnostics = new[] { diagnostic },
                        Command = new Command
                        {
                            Title = "Secure connection credentials...",
                            Name = "etlsql.secureConnection",
                            Arguments = new JArray { request.TextDocument.Uri.ToString() }
                        }
                    });
                }
            }

            return Task.FromResult(new CommandOrCodeActionContainer(results));
        }

        public CodeActionRegistrationOptions GetRegistrationOptions(CodeActionCapability capability, ClientCapabilities clientCapabilities)
        {
            return new CodeActionRegistrationOptions
            {
                DocumentSelector = TextDocumentSelector.ForLanguage("etlsql"),
                CodeActionKinds = new[] { CodeActionKind.QuickFix }
            };
        }
    }
}
