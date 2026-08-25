namespace ETL_SQL.Portal.Models;

public sealed record WorkloadIdentityTokenRequest(
    string SubjectToken,
    string Audience,
    string Resource,
    string Operation,
    string? ApprovalToken = null);

public sealed record WorkloadIdentityTokenResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    string BindingId);

public sealed record CreateWorkloadApprovalRequest(string BindingId, string Resource, string Operation);
public sealed record WorkloadApprovalResponse(string ApprovalToken, int ExpiresIn, string BindingId);
