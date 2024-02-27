namespace EssentialCSharp.Web.Middleware;

/// <summary>
/// Instantiates a new <see cref="SecurityHeadersMiddleware"/>.
/// </summary>
/// <param name="next">The next middleware in the pipeline.</param>
/// <param name="policy">An instance of the <see cref="SecurityHeadersPolicy"/> which can be applied.</param>
public class SecurityHeadersMiddleware(RequestDelegate next, SecurityHeadersPolicy policy)
{
    private readonly RequestDelegate _Next = next ?? throw new ArgumentNullException(nameof(next));
    private readonly SecurityHeadersPolicy _Policy = policy ?? throw new ArgumentNullException(nameof(policy));

    public async Task Invoke(HttpContext? context)
    {
        ArgumentNullException.ThrowIfNull(context);

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
    public static IApplicationBuilder UseSecurityHeadersMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<SecurityHeadersMiddleware>(new SecurityHeadersBuilder().AddDefaultSecurePolicy().Build());
    }

    public static IApplicationBuilder UseSecurityHeadersMiddleware(this IApplicationBuilder app, SecurityHeadersBuilder builder)
    {
        SecurityHeadersPolicy policy = builder.Build();
        return app.UseMiddleware<SecurityHeadersMiddleware>(policy);
    }
}
