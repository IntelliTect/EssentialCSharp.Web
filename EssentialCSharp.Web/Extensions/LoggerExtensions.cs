using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace EssentialCSharp.Web.Extensions;

internal static class LoggerExtensions
{
    private static readonly Action<ILogger, JsonResult, Exception?> _HomeControllerSuccessfulCaptchaResponse;

    static LoggerExtensions()
    {
        _HomeControllerSuccessfulCaptchaResponse = LoggerMessage.Define<JsonResult>(
            logLevel: LogLevel.Debug,
            eventId: 1,
            formatString: "Successful captcha with response of: '{JsonResult}'");
    }

    public static void HomeControllerSuccessfulCaptchaResponse(
        this ILogger logger, JsonResult jsonResult)
    {
        _HomeControllerSuccessfulCaptchaResponse(logger, jsonResult, null);
    }
}
