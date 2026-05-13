using System.Threading;
using System.Threading.Tasks;
using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using OmniSharp.Extensions.JsonRpc;

namespace ETL_SQL.LSP
{
    [Method("etlsql/updateNotebookContext", Direction.ClientToServer)]
    public record UpdateNotebookContextParams : IRequest
    {
        public string Uri { get; init; } = "";
        public string Prefix { get; init; } = "";
    }

    public class UpdateNotebookContextHandler : IJsonRpcNotificationHandler<UpdateNotebookContextParams>
    {
        private readonly DocumentStateStore _store;

        public UpdateNotebookContextHandler(DocumentStateStore store)
        {
            _store = store;
        }

        public Task<Unit> Handle(UpdateNotebookContextParams request, CancellationToken cancellationToken)
        {
            _store.SetNotebookPrefix(request.Uri, request.Prefix ?? "");
            return Unit.Task;
        }
    }
}
