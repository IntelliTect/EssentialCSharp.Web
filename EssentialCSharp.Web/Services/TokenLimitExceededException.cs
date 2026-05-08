namespace EssentialCSharp.Web.Services;

/// <summary>
/// Thrown when a user attempts to create an MCP token but has already reached <see cref="McpApiTokenService.MaxTokensPerUser"/>.
/// Using a dedicated type (rather than base <see cref="InvalidOperationException"/>) lets callers
/// discriminate this expected business-rule failure from unexpected EF Core / runtime failures.
/// </summary>
/// <remarks>
/// This type inherits <see cref="InvalidOperationException"/> for convenience. Be aware that any
/// <c>catch (InvalidOperationException)</c> guard added in the call chain will silently capture
/// this exception. Prefer catching <see cref="TokenLimitExceededException"/> directly.
/// </remarks>
public sealed class TokenLimitExceededException(int limit)
    : InvalidOperationException($"You have reached the maximum of {limit} active MCP tokens.");
