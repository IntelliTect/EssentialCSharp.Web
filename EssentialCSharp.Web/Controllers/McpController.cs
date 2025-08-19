using System.Security.Claims;
using EssentialCSharp.Chat.Common.Models;
using EssentialCSharp.Chat.Common.Services;
using EssentialCSharp.Web.Options;
using EssentialCSharp.Web.Models.Mcp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Controllers;

[ApiController]
[Route("api/mcp")]
[EnableRateLimiting(McpConstants.RateLimitingPolicy)]
public sealed class McpController(AISearchService search, IOptions<McpAuthOptions> authOptions, IOptions<McpOptions> mcpOptions) : ControllerBase
{
    private static readonly string[] _RequiredQueryOnly = ["query"];
    private readonly McpAuthOptions _authOptions = authOptions.Value;
    private readonly McpOptions _mcpOptions = mcpOptions.Value;
    // Public metadata endpoint for discovery
    [HttpGet(".well-known")] // Convenience metadata surface (non-standard)
    [AllowAnonymous]
    public IActionResult GetWellKnown()
    {
        // Minimal MCP-style metadata; clients can use this to discover auth and tools
        var authority = HttpContext.Request.Scheme + "://login.microsoftonline.com/{tenant}";
        var audience = _authOptions.Audience;
        var baseUrl = HttpContext.Request.Scheme + "://" + HttpContext.Request.Host;
        var metadata = new
        {
            resource_id = "essentialcsharp-context",
            scopes = _mcpOptions.SupportedScopes,
            authorization = new
            {
                type = "oauth2",
        // Hints only; the actual values come from appsettings/env
        issuer = authority,
        audience = audience,
        resource_metadata = $"{baseUrl}/.well-known/oauth-protected-resource"
            },
            tools = new[]
            {
                new
                {
                    name = "get_ecsharp_context",
                    description = "Search Essential C# book corpus and return citeable chunks.",
                    input_schema = new
                    {
                        type = "object",
                        properties = new
                        {
                            query = new { type = "string" },
                            top_k = new { type = "integer", minimum = 1, maximum = 10, @default = 5 },
                            min_score = new { type = "number", minimum = 0, maximum = 1 }
                        },
                        required = _RequiredQueryOnly
                    }
                }
            }
        };
        return Ok(metadata);
    }

    // Tools list (optional separate endpoint)
    [HttpGet("tools")]
    [AllowAnonymous]
    public IActionResult GetTools()
    => Ok(new object[]
        {
            new
            {
                name = "get_ecsharp_context",
                description = "Search Essential C# book corpus and return citeable chunks.",
                input_schema = new
                {
                    type = "object",
                    properties = new
                    {
                        query = new { type = "string" },
                        top_k = new { type = "integer", minimum = 1, maximum = 10, @default = 5 },
                        min_score = new { type = "number", minimum = 0, maximum = 1 }
                    },
                    required = _RequiredQueryOnly
                }
            }
        });

    // Authorized tool invocation
    [HttpPost("tools/get_ecsharp_context")]
    [Authorize(AuthenticationSchemes = McpConstants.JwtBearerScheme, Policy = McpConstants.AuthorizationPolicy)]
    public async Task<ActionResult<McpContextResponse>> GetContext([FromBody] McpContextRequest request, CancellationToken cancellationToken)
    {
        return await ExecuteContextSearch(request, cancellationToken);
    }

    // Temporary anonymous version for testing
    [HttpPost("tools/get_ecsharp_context/test")]
    [AllowAnonymous]
    public async Task<ActionResult<McpContextResponse>> GetContextTest([FromBody] McpContextRequest request, CancellationToken cancellationToken)
    {
        if (!_mcpOptions.AllowAnonymousForTests)
        {
            return Unauthorized(new { error = "Anonymous access not enabled" });
        }
        return await ExecuteContextSearch(request, cancellationToken);
    }

    private async Task<ActionResult<McpContextResponse>> ExecuteContextSearch(McpContextRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return BadRequest(new { error = "query is required" });
        }

        int top = request.TopK is int t ? Math.Clamp(t, 1, 10) : 5;

        var results = await search.ExecuteVectorSearch(request.Query, top);
        var list = new List<McpContextItem>();

        await foreach (var r in results.WithCancellation(cancellationToken))
        {
            // score may not be present from VectorData; using null for now
            list.Add(new McpContextItem
            {
                Id = r.Record.Id,
                Heading = r.Record.Heading,
                Content = r.Record.ChunkText,
                FileName = r.Record.FileName,
                Chapter = r.Record.ChapterNumber,
                Score = null,
                Url = BuildPublicUrl(r.Record)
            });
        }

        return Ok(new McpContextResponse
        {
            Items = list.ToArray(),
            Total = list.Count
        });
    }

    private string? BuildPublicUrl(BookContentChunk chunk)
    {
        // Derive a best-effort URL using existing site routing if available
        // If you have a stable slug, replace logic here.
        if (chunk.ChapterNumber is int chapter)
        {
            return Url.Action("Index", "Home", new { key = $"chapter-{chapter:00}" }, Request.Scheme);
        }
        return null;
    }
}
