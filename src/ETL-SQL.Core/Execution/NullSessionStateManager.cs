using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Execution;
/// <summary>
/// A do-nothing implementation of ISessionStateManager for use in stateless contexts 
/// or unit tests where full session persistence is not required.
/// </summary>
public class NullSessionStateManager : ISessionStateManager
{
    public string SessionRoot => "";

    public byte[] GetSpillKey(string sessionId)
    {
        // Fallback to a simple hash of the sessionId if no machine-key awareness is needed
        // This ensures stability within a single process run for non-persistent sessions.
        using var sha = SHA256.Create();
        return sha.ComputeHash(Encoding.UTF8.GetBytes("NULL_KEY_" + sessionId));
    }

    public Task SaveSession(string sessionId, object evaluator, string? scriptSource = null) => Task.CompletedTask;
    public Task<Core.Data.SessionState?> LoadSession(string sessionId) => Task.FromResult<Core.Data.SessionState?>(null);
    public void ClearSession(string sessionId) { }
    public IEnumerable<Core.Data.SessionSummary> GetSessions(bool includeSize = false) => System.Linq.Enumerable.Empty<Core.Data.SessionSummary>();
    public bool IsSessionInUse(string sessionId) => false;
    public void RegisterActiveSession(string sessionId) { }
    public void UnregisterActiveSession(string sessionId) { }
    public void ReapStaleSessions(System.TimeSpan maxAge) { }
    public void ReapStaleSessions() { }
}
