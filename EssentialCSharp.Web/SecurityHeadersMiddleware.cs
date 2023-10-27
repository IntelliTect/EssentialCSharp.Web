using EssentialCSharp.Web.Middleware;

namespace EssentialCSharp.Web;

// You may need to install the Microsoft.AspNetCore.Http.Abstractions package into your project
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _Next;
    private readonly SecurityHeadersPolicy _Policy;

    /// <summary>
    /// Instantiates a new <see cref="SecurityHeadersMiddleware"/>.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="policy">An instance of the <see cref="SecurityHeadersPolicy"/> which can be applied.</param>
    public SecurityHeadersMiddleware(RequestDelegate next, SecurityHeadersPolicy policy)
    {
        _Next = next ?? throw new ArgumentNullException(nameof(next));
        _Policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    public async Task Invoke(HttpContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        HttpResponse response = context.Response ?? throw new InvalidOperationException(nameof(context.Response));
        IHeaderDictionary headers = response.Headers;

        foreach (KeyValuePair<string, string> headerValuePair in _Policy.SetHeaders)
        {
            headers[headerValuePair.Key] = headerValuePair.Value;
        }

        foreach (string header in _Policy.RemoveHeaders)
        {
            headers.Remove(header);
        }

        await _Next(context);
    }
}

// Extension method used to add the middleware to the HTTP request pipeline.
public static class MiddlewareExtensions
{
    public static IApplicationBuilder UseMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<SecurityHeadersMiddleware>();
    }

    public static IApplicationBuilder UseSecurityHeadersMiddleware(this IApplicationBuilder app, SecurityHeadersBuilder builder)
    {
        SecurityHeadersPolicy policy = builder.Build();
        return app.UseMiddleware<SecurityHeadersMiddleware>(policy);
    }
}
