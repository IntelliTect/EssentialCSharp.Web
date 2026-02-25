using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EssentialCSharp.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class McpTokenController(McpTokenService mcpTokenService) : ControllerBase
{
    [HttpPost]
    public IActionResult GenerateToken()
    {
        string? userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { Error = "User must be logged in to generate an MCP token." });
        }

        string? userName = User.Identity?.Name;
        string? email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;

        var (token, expiresAt) = mcpTokenService.GenerateToken(userId, userName, email);

        return Ok(new
        {
            Token = token,
            ExpiresAt = expiresAt,
            Usage = "Add to your MCP client config: { \"url\": \"<site-url>/mcp\", \"headers\": { \"Authorization\": \"Bearer <token>\" } }"
        });
    }
}
