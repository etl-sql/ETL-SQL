using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Execution;

namespace ETL_SQL.Orchestrator.Service;

public sealed class OrchestratorObjectAuthorizationService(IOrchestratorAuthorizationStore store)
    : OrchestratorObjectAuthorizer(store, allowAnonymousWhenUnbound: false);
