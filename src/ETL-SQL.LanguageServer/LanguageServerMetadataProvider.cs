using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Linting;

namespace ETL_SQL.LSP
{
    /// <summary>
    /// Provides metadata (tables, columns, connections) to the Linter within the Language Server context.
    /// Bridges the <see cref="IMetadataManager"/> and <see cref="IMetadataProvider"/> interfaces.
    /// </summary>
    public class LanguageServerMetadataProvider : IMetadataProvider
    {
        private readonly IMetadataManager _metadataManager;
        private readonly string _documentUri;

        /// <summary>Initializes a new instance of the <see cref="LanguageServerMetadataProvider"/> class.</summary>
        /// <param name="metadataManager">The metadata manager to delegate to.</param>
        /// <param name="documentUri">The URI of the document being analyzed.</param>
        public LanguageServerMetadataProvider(IMetadataManager metadataManager, string documentUri)
        {
            _metadataManager = metadataManager;
            _documentUri = documentUri;
        }

        /// <summary>Asynchronously retrieves table names for the specified connection.</summary>
        public async Task<IEnumerable<string>> GetTablesAsync(string connectionName)
        {
            return await _metadataManager.GetTablesAsync(connectionName, _documentUri);
        }

        /// <summary>Asynchronously retrieves column names for the specified table and connection.</summary>
        public async Task<IEnumerable<string>> GetColumnsAsync(string connectionName, string tableName)
        {
            return await _metadataManager.GetColumnsAsync(connectionName, tableName, _documentUri);
        }

        /// <summary>Returns a collection of connection names available in the current context.</summary>
        public IEnumerable<string> GetConnections()
        {
            return _metadataManager.GetConnections(_documentUri).Select(c => c.Name);
        }
    }
}
