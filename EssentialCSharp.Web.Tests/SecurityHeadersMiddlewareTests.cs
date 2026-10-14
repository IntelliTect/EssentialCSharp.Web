using EssentialCSharp.Web.Middleware;
using Microsoft.AspNetCore.Http;

namespace EssentialCSharp.Web.Tests;

public class SecurityHeadersMiddlewareTests
{
    private static async Task<HttpContext> InvokeAsync(string csp)
    {
        DefaultHttpContext context = new();
        SecurityHeadersMiddleware middleware = new(
            _ => Task.CompletedTask,
            new SecurityHeadersBuilder().AddContentSecurityPolicy(csp).Build());
        await middleware.Invoke(context);
        return context;
    }

    [Test]
    public async Task Invoke_CspWithNoncePlaceholder_ReplacesPlaceholderWithRequestNonce()
    {
        HttpContext context = await InvokeAsync($"style-src 'self' 'nonce-{CspNonce.Placeholder}'");

        string? nonce = CspNonce.Get(context);
        await Assert.That(nonce).IsNotNull().And.IsNotEmpty();
        await Assert.That(context.Response.Headers.ContentSecurityPolicy.ToString())
            .IsEqualTo($"style-src 'self' 'nonce-{nonce}'");
    }

    [Test]
    public async Task Invoke_CspWithNoncePlaceholder_GeneratesUniqueNoncePerRequest()
    {
        string csp = $"style-src 'nonce-{CspNonce.Placeholder}'";
        HttpContext first = await InvokeAsync(csp);
        HttpContext second = await InvokeAsync(csp);

        await Assert.That(CspNonce.Get(first)).IsNotEqualTo(CspNonce.Get(second));
    }

    [Test]
    public async Task Invoke_CspWithoutNoncePlaceholder_DoesNotCreateNonce()
    {
        HttpContext context = await InvokeAsync("style-src 'self'");

        await Assert.That(CspNonce.Get(context)).IsNull();
    }
}
