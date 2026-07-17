namespace ETL_SQL.Portal.Models;

public record LoginRequest(string Username, string Password);

public record LoginResponse(string Token, string RefreshToken, DateTime ExpiresAt);

public record RefreshRequest(string RefreshToken);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
