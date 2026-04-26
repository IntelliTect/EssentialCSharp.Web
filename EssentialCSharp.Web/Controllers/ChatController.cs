using System.Text.Json;
using EssentialCSharp.Chat;
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
    private const string AIUnavailableErrorCode = "ai_unavailable";

    private readonly AIConfigurationState _AiConfiguration;
    private readonly IAIChatService _AiChatService;
    private readonly ICaptchaService _CaptchaService;
    private readonly ILogger<ChatController> _Logger;

    public ChatController(
        ILogger<ChatController> logger,
        AIConfigurationState aiConfiguration,
        IAIChatService aiChatService,
        ICaptchaService captchaService)
    {
        _AiConfiguration = aiConfiguration;
        _AiChatService = aiChatService;
        _CaptchaService = captchaService;
        _Logger = logger;
    }

    [HttpPost("message")]
    public async Task<IActionResult> SendMessage([FromBody] ChatMessageRequest request, CancellationToken cancellationToken = default)
    {
        if (!_AiConfiguration.IsAvailable)
        {
            return CreateAIUnavailableResult();
        }

        var (captchaOk, captchaError) = await VerifyCaptchaAsync(request.CaptchaToken, cancellationToken);
        if (!captchaOk) return captchaError!;

        request.Message = request.Message.Trim();
        if (string.IsNullOrEmpty(request.Message))
            return BadRequest(new { error = "Message cannot be empty." });

        var previousResponseId = string.IsNullOrWhiteSpace(request.PreviousResponseId)
            ? null
            : request.PreviousResponseId.Trim();

        try
        {
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
        catch (AIChatUnavailableException ex)
        {
            _Logger.LogInformation(ex, "Chat unavailable for user {User}", User.Identity?.Name);
            return CreateAIUnavailableResult(ex.Message);
        }
    }

    [HttpPost("stream")]
    public async Task StreamMessage([FromBody] ChatMessageRequest request, CancellationToken cancellationToken = default)
    {
        if (!_AiConfiguration.IsAvailable)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await Response.WriteAsJsonAsync(CreateAIUnavailablePayload(), CancellationToken.None);
            return;
        }

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
        catch (AIChatUnavailableException ex)
        {
            _Logger.LogInformation(ex, "Chat unavailable for user {User}", User.Identity?.Name);
            if (!Response.HasStarted)
            {
                Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                Response.ContentType = "application/json";
                await Response.WriteAsJsonAsync(CreateAIUnavailablePayload(ex.Message), CancellationToken.None);
                return;
            }

            try
            {
                var eventData = JsonSerializer.Serialize(new
                {
                    type = "error",
                    errorCode = AIUnavailableErrorCode,
                    message = ex.Message
                });
                await Response.WriteAsync($"data: {eventData}\n\n", CancellationToken.None);
                await Response.Body.FlushAsync(CancellationToken.None);
            }
            catch
            {
                // The client may already be gone; there's nothing else to do.
            }
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
    /// Verifies the hCaptcha token and denies chat access when verification cannot be completed.
    /// </summary>
    private async Task<(bool Success, IActionResult? Error)> VerifyCaptchaAsync(
        string? captchaToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(captchaToken))
            return (false, CreateCaptchaRequiredResult());

        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _CaptchaService.VerifyAsync(captchaToken, remoteIp, cancellationToken);

        if (result is null)
        {
            _Logger.LogWarning("hCaptcha service unavailable for user {User} — denying request", User.Identity?.Name);
            return (false, CreateCaptchaUnavailableResult());
        }

        if (!result.Success)
            return (false, CreateCaptchaFailedResult());

        return (true, null);
    }

    private ObjectResult CreateCaptchaRequiredResult() =>
        StatusCode(StatusCodes.Status403Forbidden,
            new { error = "Captcha verification required.", errorCode = "captcha_required", retryable = true });

    private ObjectResult CreateCaptchaFailedResult() =>
        StatusCode(StatusCodes.Status403Forbidden,
            new { error = "Captcha verification failed.", errorCode = "captcha_failed", retryable = true });

    private ObjectResult CreateCaptchaUnavailableResult() =>
        StatusCode(StatusCodes.Status503ServiceUnavailable,
            new
            {
                error = "Captcha verification is temporarily unavailable. Please try again later.",
                errorCode = "captcha_unavailable",
                retryable = true
            });

    private static object CreateAIUnavailablePayload(string? message = null) =>
        new
        {
            error = string.IsNullOrWhiteSpace(message) ? AIConfigurationState.DevelopmentUnavailableMessage : message,
            errorCode = AIUnavailableErrorCode,
            retryable = false
        };

    private ObjectResult CreateAIUnavailableResult(string? message = null) =>
        StatusCode(StatusCodes.Status503ServiceUnavailable, CreateAIUnavailablePayload(message));

}
