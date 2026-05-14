using System.Security.Claims;
using System.Text.Json;
using EssentialCSharp.Chat.Common.Services;
using EssentialCSharp.Web.Models;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[EnableRateLimiting("ChatEndpoint")]
[IgnoreAntiforgeryToken]
public partial class ChatController : ControllerBase
{
    private readonly AIChatService _AiChatService;
    private readonly ResponseIdValidationService _ResponseIdValidationService;
    private readonly ICaptchaService _CaptchaService;
    private readonly CaptchaOptions _CaptchaOptions;
    private readonly ILogger<ChatController> _Logger;

    public ChatController(ILogger<ChatController> logger, AIChatService aiChatService,
        ResponseIdValidationService responseIdValidationService,
        ICaptchaService captchaService, IOptions<CaptchaOptions> captchaOptions)
    {
        _AiChatService = aiChatService;
        _ResponseIdValidationService = responseIdValidationService;
        _CaptchaService = captchaService;
        _CaptchaOptions = captchaOptions.Value;
        _Logger = logger;
    }

    /// <summary>
    /// Validates the hCaptcha token when captcha is configured.
    /// Returns <c>true</c> when captcha is not configured (dev mode) or when the token is valid.
    /// Fails open on hCaptcha service outages (null result) to avoid blocking legitimate users —
    /// this is intentional given the existing [Authorize] + rate-limiting backstop.
    /// </summary>
    private async Task<bool> IsCaptchaValidAsync(string? token, string? remoteIp, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_CaptchaOptions.SecretKey))
            return true; // captcha not configured — skip validation

        if (string.IsNullOrWhiteSpace(token))
            return false; // token required when captcha is configured — reject without an outbound call

        HCaptchaResult? result = await _CaptchaService.VerifyAsync(token, remoteIp, ct);
        if (result is null)
        {
            LogCaptchaServiceUnavailable(_Logger); // hCaptcha unreachable — fail open (intentional)
            return true;
        }

        if (!result.Success)
        {
            LogCaptchaValidationFailed(_Logger, string.Join(',', result.ErrorCodes ?? []));
        }
        return result.Success;
    }

    [HttpPost("message")]
    public async Task<IActionResult> SendMessage([FromBody] ChatMessageRequest request, CancellationToken cancellationToken = default)
    {
        request.Message = request.Message.Trim();
        if (string.IsNullOrEmpty(request.Message))
            return BadRequest(new { error = "Message cannot be empty." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        if (!await IsCaptchaValidAsync(request.CaptchaResponse, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken))
            return StatusCode(403, new { error = "Human verification required.", errorCode = "captcha_failed" });

        var previousResponseId = string.IsNullOrWhiteSpace(request.PreviousResponseId)
            ? null
            : request.PreviousResponseId.Trim();

        if (!_ResponseIdValidationService.ValidateResponseId(userId, previousResponseId))
            return BadRequest(new { error = "Invalid conversation context." });

        try
        {
            var (response, responseId) = await _AiChatService.GetChatCompletion(
                prompt: request.Message,
                previousResponseId: previousResponseId,
                enableContextualSearch: request.EnableContextualSearch,
                endUserId: userId,
                cancellationToken: cancellationToken);

            _ResponseIdValidationService.RecordResponseId(userId, responseId);

            return Ok(new ChatMessageResponse
            {
                Response = response,
                ResponseId = responseId,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (ConversationContextLimitExceededException)
        {
            return BadRequest(new { error = "This conversation has grown too long. Please start a new one.", errorCode = "context_limit_exceeded" });
        }
    }

    [HttpPost("stream")]
    public async Task StreamMessage([FromBody] ChatMessageRequest request, CancellationToken cancellationToken = default)
    {
        request.Message = request.Message.Trim();
        if (string.IsNullOrEmpty(request.Message))
        {
            Response.StatusCode = 400;
            await Response.WriteAsJsonAsync(new { error = "Message cannot be empty." }, CancellationToken.None);
            return;
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            Response.StatusCode = 401;
            await Response.WriteAsJsonAsync(new { error = "Unauthorized." }, CancellationToken.None);
            return;
        }

        if (!await IsCaptchaValidAsync(request.CaptchaResponse, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken))
        {
            Response.StatusCode = 403;
            await Response.WriteAsJsonAsync(new { error = "Human verification required.", errorCode = "captcha_failed" }, CancellationToken.None);
            return;
        }

        var previousResponseId = string.IsNullOrWhiteSpace(request.PreviousResponseId)
            ? null
            : request.PreviousResponseId.Trim();

        if (!_ResponseIdValidationService.ValidateResponseId(userId, previousResponseId))
        {
            Response.StatusCode = 400;
            await Response.WriteAsJsonAsync(new { error = "Invalid conversation context." }, CancellationToken.None);
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        try
        {
            await foreach (var (text, responseId) in _AiChatService.GetChatCompletionStream(
                prompt: request.Message,
                previousResponseId: previousResponseId,
                enableContextualSearch: request.EnableContextualSearch,
                endUserId: userId,
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
                    // Record ownership for every responseId emitted — one per API call leg
                    // (initial request + each tool-call continuation). This ensures all leg IDs
                    // are bound to the authenticated user before being forwarded to the client.
                    _ResponseIdValidationService.RecordResponseId(userId, responseId);
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
            LogChatStreamCancelled(_Logger, User.Identity?.Name);
        }
        catch (ConversationContextLimitExceededException) when (!Response.HasStarted)
        {
            Response.StatusCode = 400;
            Response.ContentType = "application/json";
            await Response.WriteAsJsonAsync(new { error = "This conversation has grown too long. Please start a new one.", errorCode = "context_limit_exceeded" }, CancellationToken.None);
        }
        catch (ConversationContextLimitExceededException ex)
        {
            LogChatStreamErrorMidStream(_Logger, ex, User.Identity?.Name);
            try
            {
                await Response.WriteAsync("data: {\"type\":\"error\",\"message\":\"This conversation has grown too long. Please start a new one.\",\"errorCode\":\"context_limit_exceeded\"}\n\n", CancellationToken.None);
                await Response.Body.FlushAsync(CancellationToken.None);
            }
            catch (Exception)
            {
                // Best-effort write to an already-streaming response. Kestrel can throw
                // IOException (connection reset), OperationCanceledException, or
                // ObjectDisposedException on abrupt client disconnect — swallow all to
                // avoid masking the original exception.
            }
        }
        catch (Exception ex) when (!Response.HasStarted)
        {
            LogChatStreamErrorBeforeResponseStarted(_Logger, ex, User.Identity?.Name);
            Response.StatusCode = 500;
            Response.ContentType = "application/json";
            await Response.WriteAsJsonAsync(new { error = "Chat service unavailable" }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogChatStreamErrorMidStream(_Logger, ex, User.Identity?.Name);
            try
            {
                await Response.WriteAsync("data: {\"type\":\"error\",\"message\":\"Stream interrupted\"}\n\n", CancellationToken.None);
                await Response.Body.FlushAsync(CancellationToken.None);
            }
            catch (Exception)
            {
                // Best-effort write to an already-streaming response. Kestrel can throw
                // IOException (connection reset), OperationCanceledException, or
                // ObjectDisposedException on abrupt client disconnect — swallow all to
                // avoid masking the original exception.
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "hCaptcha service unavailable during chat request — failing open")]
    private static partial void LogCaptchaServiceUnavailable(ILogger<ChatController> logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "hCaptcha validation failed for chat request — error codes: {ErrorCodes}")]
    private static partial void LogCaptchaValidationFailed(ILogger<ChatController> logger, string errorCodes);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Chat stream cancelled for user {User}")]
    private static partial void LogChatStreamCancelled(ILogger<ChatController> logger, string? user);

    [LoggerMessage(Level = LogLevel.Error, Message = "Chat streaming error before response started for user {User}")]
    private static partial void LogChatStreamErrorBeforeResponseStarted(ILogger<ChatController> logger, Exception exception, string? user);

    [LoggerMessage(Level = LogLevel.Error, Message = "Chat streaming error mid-stream for user {User}")]
    private static partial void LogChatStreamErrorMidStream(ILogger<ChatController> logger, Exception exception, string? user);
}
