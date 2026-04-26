using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Models;
using ETL_SQL.ReportPortal.Services;

namespace ETL_SQL.ReportPortal.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    UserManager<PortalUser>  userManager,
    SignInManager<PortalUser> signInManager,
    TokenService             tokenService,
    AuditService             auditService,
    PortalDbContext          db,
    PortalConfig             config) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var user = await userManager.FindByNameAsync(req.Username);
        if (user is null || !user.IsActive)
            return Unauthorized(new { error = "Invalid credentials" });

        var result = await signInManager.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            await auditService.LogAsync(user.Id, "LOGIN_FAILED", "User", user.Id.ToString());
            if (result.IsLockedOut)
                return StatusCode(429, new { error = "Account locked. Try again in 15 minutes." });
            return Unauthorized(new { error = "Invalid credentials" });
        }

        var roles       = await userManager.GetRolesAsync(user);
        var jwt         = tokenService.GenerateJwt(user, roles);
        var rawRefresh  = tokenService.GenerateRefreshToken();
        var expiresAt   = DateTime.UtcNow.AddMinutes(config.Jwt.ExpiryMinutes);

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId    = user.Id,
            Token     = rawRefresh,
            ExpiresAt = DateTime.UtcNow.AddDays(config.Jwt.RefreshExpiryDays)
        });
        await db.SaveChangesAsync();
        await auditService.LogAsync(user.Id, "LOGIN", "User", user.Id.ToString());

        return Ok(new LoginResponse(jwt, rawRefresh, expiresAt));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest req)
    {
        var token = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == req.RefreshToken
                && t.RevokedAt == null
                && t.ExpiresAt > DateTime.UtcNow);

        if (token is null)
            return Unauthorized(new { error = "Invalid or expired refresh token" });

        // Rotate: revoke old, issue new
        token.RevokedAt = DateTime.UtcNow;
        var newRaw = tokenService.GenerateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId    = token.UserId,
            Token     = newRaw,
            ExpiresAt = DateTime.UtcNow.AddDays(config.Jwt.RefreshExpiryDays)
        });
        await db.SaveChangesAsync();

        var user     = token.User;
        var roles    = await userManager.GetRolesAsync(user);
        var jwt      = tokenService.GenerateJwt(user, roles);
        var expiresAt = DateTime.UtcNow.AddMinutes(config.Jwt.ExpiryMinutes);

        return Ok(new LoginResponse(jwt, newRaw, expiresAt));
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user   = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return NotFound();

        var result = await userManager.ChangePasswordAsync(user, req.CurrentPassword, req.NewPassword);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        user.MustChangePassword = false;
        await userManager.UpdateAsync(user);
        await auditService.LogAsync(userId, "PASSWORD_CHANGED", "User", userId.ToString());

        return NoContent();
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest req)
    {
        var token = await db.RefreshTokens
            .FirstOrDefaultAsync(t => t.Token == req.RefreshToken && t.RevokedAt == null);

        if (token is not null)
        {
            token.RevokedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await auditService.LogAsync(userId, "LOGOUT", "User", userId.ToString());
        return NoContent();
    }
}
