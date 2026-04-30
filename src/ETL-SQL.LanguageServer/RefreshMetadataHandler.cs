using System.Threading;
using System.Threading.Tasks;
using MediatR;
using OmniSharp.Extensions.JsonRpc;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using ETL_SQL.Core;

namespace ETL_SQL.LSP
{
    /// <summary>Params for metadata refresh.</summary>
    public class RefreshMetadataParams : IRequest 
    { 
        public string Uri { get; set; } = string.Empty; 
    }

    /// <summary>Notification handler for metadata refresh.</summary>
    [Method("etlsql/refreshMetadata", Direction.ClientToServer)]
    public interface IRefreshMetadataHandler : IJsonRpcNotificationHandler<RefreshMetadataParams> { }

    public class RefreshMetadataHandler(ILogger<RefreshMetadataHandler> logger, DocumentStateStore store, TextDocumentHandler textDocumentHandler, IMetadataManager metadata) : IRefreshMetadataHandler
    {
        public async Task<Unit> Handle(RefreshMetadataParams request, CancellationToken cancellationToken)
        {
            logger.LogInformation("LSP: RefreshMetadata requested for {Uri}", request.Uri);
            // Clear the table/column cache for this document so new tables (created during execution)
            // are discovered on the next sidebar expand rather than returning stale empty results.
            metadata.ClearCacheForUri(request.Uri);
            var text = store.GetDocumentText(request.Uri);
            if (text != null)
            {
                await textDocumentHandler.AnalyzeAsync(request.Uri, text);
            }
            return Unit.Value;
        }
    }
}
