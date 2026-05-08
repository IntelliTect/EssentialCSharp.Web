namespace EssentialCSharp.Web.Services;

/// <summary>
/// Thrown when a user attempts to create an MCP token but has already reached <see cref="McpApiTokenService.MaxTokensPerUser"/>.
/// Using a dedicated type (rather than base <see cref="InvalidOperationException"/>) lets callers
/// discriminate this expected business-rule failure from unexpected EF Core / runtime failures.
/// </summary>
public sealed class TokenLimitExceededException(int limit)
    : InvalidOperationException($"You have reached the maximum of {limit} active MCP tokens.");
