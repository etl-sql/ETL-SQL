using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Core.Execution
{
    public interface ISessionStateManager
    {
        string SessionRoot { get; }
        byte[] GetSpillKey(string sessionId);
        
        Task SaveSession(string sessionId, object evaluator, string? scriptSource = null);
        Task<SessionState?> LoadSession(string sessionId);
        void ClearSession(string sessionId);
        IEnumerable<SessionSummary> GetSessions();
        bool IsSessionInUse(string sessionId);
        void RegisterActiveSession(string sessionId);
        void UnregisterActiveSession(string sessionId);
        void ReapStaleSessions(System.TimeSpan maxAge);
        void ReapStaleSessions();
    }
}
