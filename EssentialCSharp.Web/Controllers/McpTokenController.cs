using System.Security.Claims;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EssentialCSharp.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class McpTokenController(McpApiTokenService tokenService) : ControllerBase
{
    public record CreateTokenRequest(string? Name, DateOnly? ExpiresOn = null);

    [HttpPost]
    public async Task<IActionResult> CreateToken(
        [FromBody] CreateTokenRequest? request,
        CancellationToken cancellationToken)
    {
        string? userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { Error = "User must be logged in to generate an MCP token." });

        string name = string.IsNullOrWhiteSpace(request?.Name) ? "default" : request.Name.Trim();
        if (name.Length > 256)
            return BadRequest(new { Error = "Token name must be 256 characters or fewer." });

        DateTime? expiresAt = null;
        DateOnly maxExpiresOn = McpApiTokenService.GetDefaultExpiryDate();
        if (request?.ExpiresOn is DateOnly expiresOn)
        {
            if (expiresOn < DateOnly.FromDateTime(DateTime.UtcNow))
                return BadRequest(new { Error = "ExpiresOn must be today or in the future." });
            if (expiresOn > maxExpiresOn)
                return BadRequest(new { Error = McpApiTokenService.MaxExpiryValidationMessage });
            // Convert date-only boundary to end-of-day UTC instant before persisting
            expiresAt = expiresOn.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
        }

        var (rawToken, entity) = await tokenService.CreateTokenAsync(
            userId, name, expiresAt, cancellationToken);

        return Ok(new
        {
            TokenId = entity.Id,
            Token = rawToken,
            Name = entity.Name,
            ExpiresAt = entity.ExpiresAt,
            CreatedAt = entity.CreatedAt,
            Usage = "Add to your MCP client config: { \"url\": \"<site-url>/mcp\", \"headers\": { \"Authorization\": \"Bearer <token>\" } }"
        });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> RevokeToken(Guid id, CancellationToken cancellationToken)
    {
        string? userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        bool revoked = await tokenService.RevokeTokenAsync(id, userId, cancellationToken);
        if (!revoked)
            return NotFound(new { Error = "Token not found or already revoked." });

        return NoContent();
    }
}
