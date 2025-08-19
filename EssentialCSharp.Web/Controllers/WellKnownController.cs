using System.Text.Json;
using EssentialCSharp.Web.Models.Mcp;
using Microsoft.AspNetCore.Mvc;

namespace EssentialCSharp.Web.Controllers;

[ApiController]
[Route(".well-known")] // RFC well-known base
public sealed class WellKnownController(IConfiguration configuration) : ControllerBase
{
    // RFC 9728 Protected Resource Metadata endpoint
    // GET /.well-known/oauth-protected-resource
    [HttpGet("oauth-protected-resource")]
    [Produces("application/json")]
    public IActionResult GetProtectedResourceMetadata()
    {
        var authority = configuration["McpAuth:Authority"];
        var audience = configuration["McpAuth:Audience"];

        // Compute canonical resource from request if not explicitly configured
        var host = $"{Request.Scheme}://{Request.Host}";
        var resource = configuration["McpAuth:CanonicalResource"] ?? host;

        var metadata = new
        {
            resource = resource,
            authorization_servers = new[] { authority },
            // Optional descriptive fields for clients; kept minimal
            resource_id = audience
        };
        return Ok(metadata);
    }
}
