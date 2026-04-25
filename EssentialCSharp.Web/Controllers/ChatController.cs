using System.Text.Json;
using EssentialCSharp.Chat.Common.Services;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EssentialCSharp.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[EnableRateLimiting("ChatEndpoint")]
public class ChatController : ControllerBase
{
    private readonly IAIChatService _AiChatService;
    private readonly ICaptchaService _CaptchaService;
    private readonly ILogger<ChatController> _Logger;

    public ChatController(ILogger<ChatController> logger, IAIChatService aiChatService, ICaptchaService captchaService)
    {
        _AiChatService = aiChatService;
        _CaptchaService = captchaService;
        _Logger = logger;
    }

    [HttpPost("message")]
    public async Task<IActionResult> SendMessage([FromBody] ChatMessageRequest request, CancellationToken cancellationToken = default)
    {
        var (captchaOk, captchaError) = await VerifyCaptchaAsync(request.CaptchaToken, cancellationToken);
        if (!captchaOk) return captchaError!;

        request.Message = request.Message.Trim();
        if (string.IsNullOrEmpty(request.Message))
            return BadRequest(new { error = "Message cannot be empty." });

        var previousResponseId = string.IsNullOrWhiteSpace(request.PreviousResponseId)
            ? null
            : request.PreviousResponseId.Trim();

        var (response, responseId) = await _AiChatService.GetChatCompletion(
            prompt: request.Message,
            previousResponseId: previousResponseId,
            enableContextualSearch: request.EnableContextualSearch,
            cancellationToken: cancellationToken);

        return Ok(new ChatMessageResponse
        {
            Response = response,
            ResponseId = responseId,
            Timestamp = DateTime.UtcNow
        });
    }

    [HttpPost("stream")]
    public async Task StreamMessage([FromBody] ChatMessageRequest request, CancellationToken cancellationToken = default)
    {
        // Captcha and input validation must happen before SSE headers are set,
        // so we can still return a proper HTTP status code on failure.
        var (captchaOk, captchaError) = await VerifyCaptchaAsync(request.CaptchaToken, cancellationToken);
        if (!captchaOk)
        {
            Response.StatusCode = captchaError is ObjectResult obj ? obj.StatusCode ?? 403 : 403;
            await Response.WriteAsJsonAsync(
                captchaError is ObjectResult { Value: not null } r ? r.Value : new { error = "Captcha verification failed." },
                CancellationToken.None);
            return;
        }

        request.Message = request.Message.Trim();
        if (string.IsNullOrEmpty(request.Message))
        {
            Response.StatusCode = 400;
            await Response.WriteAsJsonAsync(new { error = "Message cannot be empty." }, CancellationToken.None);
            return;
        }

        var previousResponseId = string.IsNullOrWhiteSpace(request.PreviousResponseId)
            ? null
            : request.PreviousResponseId.Trim();

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        try
        {
            await foreach (var (text, responseId) in _AiChatService.GetChatCompletionStream(
                prompt: request.Message,
                previousResponseId: previousResponseId,
                enableContextualSearch: request.EnableContextualSearch,
                cancellationToken: cancellationToken))
            {
                if (!string.IsNullOrEmpty(text))
                {
                    var eventData = JsonSerializer.Serialize(new { type = "text", data = text });
                    await Response.WriteAsync($"data: {eventData}\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                }

                if (!string.IsNullOrEmpty(responseId))
                {
                    var eventData = JsonSerializer.Serialize(new { type = "responseId", data = responseId });
                    await Response.WriteAsync($"data: {eventData}\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                }
            }

            await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || HttpContext.RequestAborted.IsCancellationRequested)
        {
            _Logger.LogDebug("Chat stream cancelled for user {User}", User.Identity?.Name);
        }
        catch (Exception ex) when (!Response.HasStarted)
        {
            _Logger.LogError(ex, "Chat streaming error before response started for user {User}", User.Identity?.Name);
            Response.StatusCode = 500;
            Response.ContentType = "application/json";
            await Response.WriteAsJsonAsync(new { error = "Chat service unavailable" }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _Logger.LogError(ex, "Chat streaming error mid-stream for user {User}", User.Identity?.Name);
            try
            {
                await Response.WriteAsync("data: {\"type\":\"error\",\"message\":\"Stream interrupted\"}\n\n", CancellationToken.None);
                await Response.Body.FlushAsync(CancellationToken.None);
            }
            catch { /* client already disconnected */ }
        }
    }

    /// <summary>
    /// Verifies the hCaptcha token. Fails-open on service outage (returns success with warning)
    /// since the endpoint is already protected by [Authorize] and rate limiting.
    /// </summary>
    private async Task<(bool Success, IActionResult? Error)> VerifyCaptchaAsync(
        string? captchaToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(captchaToken))
            return (false, StatusCode(StatusCodes.Status403Forbidden,
                new { error = "Captcha verification required.", errorCode = "captcha_required", retryable = true }));

        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _CaptchaService.VerifyAsync(captchaToken, remoteIp, cancellationToken);

        if (result is null)
        {
            // hCaptcha service is unreachable — fail-open since [Authorize] + rate limiting still protect the endpoint.
            _Logger.LogWarning("hCaptcha service unavailable for user {User} — allowing request", User.Identity?.Name);
            return (true, null);
        }

        if (!result.Success)
            return (false, StatusCode(StatusCodes.Status403Forbidden,
                new { error = "Captcha verification failed.", errorCode = "captcha_failed", retryable = true }));

        return (true, null);
    }
}
