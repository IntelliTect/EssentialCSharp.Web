using EssentialCSharp.Web.Auth;
using Microsoft.AspNetCore.Http;

namespace EssentialCSharp.Web.Tests;

public class McpBearerAuthenticationTests
{
    [Test]
    public async Task TryGetRawToken_ValidMcpBearerToken_ReturnsToken()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer mcp_test-token";

        bool result = McpBearerAuthentication.TryGetRawToken(context.Request, out string? token);

        await Assert.That(result).IsTrue();
        await Assert.That(token).IsEqualTo("mcp_test-token");
    }

    [Test]
    public async Task TryGetRawToken_BearerSchemeIsCaseInsensitive_ReturnsToken()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "bEaReR mcp_test-token";

        bool result = McpBearerAuthentication.TryGetRawToken(context.Request, out string? token);

        await Assert.That(result).IsTrue();
        await Assert.That(token).IsEqualTo("mcp_test-token");
    }

    [Test]
    public async Task TryGetRawToken_TokenWhitespaceIsTrimmed_ReturnsToken()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer   mcp_test-token  ";

        bool result = McpBearerAuthentication.TryGetRawToken(context.Request, out string? token);

        await Assert.That(result).IsTrue();
        await Assert.That(token).IsEqualTo("mcp_test-token");
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("Basic mcp_test-token")]
    [Arguments("Bearer")]
    [Arguments("Bearer ")]
    [Arguments("Bearer token_without_mcp_prefix")]
    public async Task TryGetRawToken_InvalidAuthorizationHeader_ReturnsFalse(string? authorization)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = authorization;

        bool result = McpBearerAuthentication.TryGetRawToken(context.Request, out string? token);

        await Assert.That(result).IsFalse();
        await Assert.That(token).IsNull();
    }

    [Test]
    public async Task TryGetRawToken_McpPrefixComparisonIsCaseSensitive_ReturnsFalseForUppercasePrefix()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer MCP_test-token";

        bool result = McpBearerAuthentication.TryGetRawToken(context.Request, out string? token);

        await Assert.That(result).IsFalse();
        await Assert.That(token).IsNull();
    }
}
