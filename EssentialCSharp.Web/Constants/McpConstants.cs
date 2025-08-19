using System.ComponentModel.DataAnnotations;

namespace EssentialCSharp.Web.Options;

/// <summary>
/// MCP authentication configuration options
/// </summary>
public class McpAuthOptions
{
    public const string SectionName = "McpAuth";

    [Required]
    public string Authority { get; set; } = string.Empty;

    [Required] 
    public string Audience { get; set; } = "api://essentialcsharp-mcp";

    public bool RequireHttpsMetadata { get; set; } = true;
}

/// <summary>
/// MCP server configuration options
/// </summary>
public class McpOptions
{
    public const string SectionName = "Mcp";

    public bool EnableSdk { get; set; } = true;
    public bool AllowAnonymousForTests { get; set; }
    public bool EnableDetailedLogging { get; set; }
    
    public string[] SupportedScopes { get; set; } = 
    [
        "mcp:tools",
        "read:ecsharp_context"
    ];

    // Rate limiting configuration
    public int RateLimitPermits { get; set; } = 30;
    public int RateLimitWindowMinutes { get; set; } = 1;
}

/// <summary>
/// Constants for MCP-related identifiers that don't change
/// </summary>
public static class McpConstants
{
    /// <summary>
    /// Rate limiting policy name for MCP endpoints
    /// </summary>
    public const string RateLimitingPolicy = "McpEndpoint";

    /// <summary>
    /// Authorization policy name for MCP scope validation
    /// </summary>
    public const string AuthorizationPolicy = "McpScopePolicy";

    /// <summary>
    /// JWT Bearer authentication scheme name for MCP
    /// </summary>
    public const string JwtBearerScheme = "McpJwtBearer";
}
