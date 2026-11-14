using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Core.Infrastructure;

namespace EssentialCSharp.Web.Controllers;

/// <summary>
/// Marks a rejected antiforgery token on the response with <see cref="HeaderName"/>: <c>1</c>. This is the
/// explicit, stable signal the reading client uses to tell "the token is no longer valid, stop tracking"
/// from a 400 caused by the request body (or by a proxy/WAF/server in front of the app).
/// Always-run so it also sees the result produced by the global antiforgery authorization filter, which
/// short-circuits before the action executes; ordered first because [ApiController] otherwise replaces that
/// result with a problem document before this filter can recognize it.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ReadingAntiforgeryFailureHeaderAttribute : Attribute, IAlwaysRunResultFilter, IOrderedFilter
{
    public const string HeaderName = "X-Antiforgery-Failure";

    public int Order => int.MinValue;

    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is IAntiforgeryValidationFailedResult)
        {
            context.HttpContext.Response.Headers[HeaderName] = "1";
        }
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}
