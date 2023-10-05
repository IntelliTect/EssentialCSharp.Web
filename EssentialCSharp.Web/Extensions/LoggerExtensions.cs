using Microsoft.AspNetCore.Mvc;

namespace EssentialCSharp.Web.Extensions;

internal static partial class LoggerExtensions
{
    [LoggerMessage(Level = LogLevel.Debug, EventId = 1, Message = "Successful captcha with response of: '{JsonResult}'")]
    public static partial void HomeControllerSuccessfulCaptchaResponse(
           this ILogger logger, JsonResult jsonResult);
}
