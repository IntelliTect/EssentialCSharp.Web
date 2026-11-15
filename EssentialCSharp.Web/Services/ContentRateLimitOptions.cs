namespace EssentialCSharp.Web.Services;

public class ContentRateLimitOptions
{
    public const string SectionName = "ContentRateLimiting";

    /// <summary>
    /// Shared secret the link checker sends in <see cref="ContentRateLimiterPolicy.LinkCheckerKeyHeader"/>
    /// to bypass the content rate limiter. Leave empty to disable the exemption.
    /// </summary>
    public string? LinkCheckerKey { get; set; }
}
