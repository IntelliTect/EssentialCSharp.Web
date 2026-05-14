using System.IO;
using System.Security.Claims;
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
[IgnoreAntiforgeryToken]
public partial class ChatController : ControllerBase
{
    private readonly AIChatService _AIChatService;
    private readonly ResponseIdValidationService _ResponseIdValidationService;
    private readonly ILogger<ChatController> _Logger;

    public ChatController(ILogger<ChatController> logger, AIChatService aiChatService, ResponseIdValidationService responseIdValidationService)
    {
        _AIChatService = aiChatService;
        _ResponseIdValidationService = responseIdValidationService;
        _Logger = logger;
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

        var previousResponseId = string.IsNullOrWhiteSpace(request.PreviousResponseId)
            ? null
            : request.PreviousResponseId.Trim();

        if (!_ResponseIdValidationService.ValidateResponseId(userId, previousResponseId))
            return BadRequest(new { error = "Invalid conversation context." });

        try
        {
            var (response, responseId) = await _AIChatService.GetChatCompletion(
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
            await Response.WriteAsJsonAsync(new { error = "Message cannot be empty." }, cancellationToken);
            return;
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            Response.StatusCode = 401;
            await Response.WriteAsJsonAsync(new { error = "Unauthorized." }, cancellationToken);
            return;
        }

        var previousResponseId = string.IsNullOrWhiteSpace(request.PreviousResponseId)
            ? null
            : request.PreviousResponseId.Trim();

        if (!_ResponseIdValidationService.ValidateResponseId(userId, previousResponseId))
        {
            Response.StatusCode = 400;
            await Response.WriteAsJsonAsync(new { error = "Invalid conversation context." }, cancellationToken);
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        try
        {
            await foreach (var (text, responseId) in _AIChatService.GetChatCompletionStream(
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
        catch (OperationCanceledException) when (Response.HasStarted || cancellationToken.IsCancellationRequested || HttpContext.RequestAborted.IsCancellationRequested)
        {
            LogChatStreamCancelled(_Logger, User.Identity?.Name);
        }
        catch (ConversationContextLimitExceededException ex)
        {
            if (!Response.HasStarted)
            {
                if (cancellationToken.IsCancellationRequested || HttpContext.RequestAborted.IsCancellationRequested)
                    return;

                Response.StatusCode = 400;
                Response.ContentType = "application/json";
                try
                {
                    await Response.WriteAsJsonAsync(new { error = "This conversation has grown too long. Please start a new one.", errorCode = "context_limit_exceeded" }, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || HttpContext.RequestAborted.IsCancellationRequested)
                {
                    // Best-effort write during an aborted request — no response body can be delivered.
                }
                catch (IOException) when (HttpContext.RequestAborted.IsCancellationRequested)
                {
                    // Expected client disconnect while attempting a best-effort error response write.
                }
                catch (ObjectDisposedException) when (HttpContext.RequestAborted.IsCancellationRequested)
                {
                    // Response stream can already be disposed after an abrupt client disconnect.
                }
            }
            else
            {
                LogChatStreamErrorMidStream(_Logger, ex, User.Identity?.Name);
                try
                {
                    await Response.WriteAsync("data: {\"type\":\"error\",\"message\":\"This conversation has grown too long. Please start a new one.\",\"errorCode\":\"context_limit_exceeded\"}\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                }
                catch (Exception writeException) when (writeException is IOException or OperationCanceledException or ObjectDisposedException)
                {
                    // Best-effort write to an already-streaming response. Kestrel can throw
                    // IOException (connection reset), OperationCanceledException, or
                    // ObjectDisposedException on abrupt client disconnect — swallow expected
                    // transport/disconnect exceptions to avoid masking the original exception.
                }
            }
        }
        catch (Exception ex) when (!Response.HasStarted)
        {
            LogChatStreamErrorBeforeResponseStarted(_Logger, ex, User.Identity?.Name);
            if (cancellationToken.IsCancellationRequested || HttpContext.RequestAborted.IsCancellationRequested)
                return;

            Response.StatusCode = 500;
            Response.ContentType = "application/json";
            try
            {
                await Response.WriteAsJsonAsync(new { error = "Chat service unavailable" }, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || HttpContext.RequestAborted.IsCancellationRequested)
            {
                // Best-effort write during an aborted request — no response body can be delivered.
            }
            catch (IOException) when (HttpContext.RequestAborted.IsCancellationRequested)
            {
                // Expected client disconnect while attempting a best-effort error response write.
            }
            catch (ObjectDisposedException) when (HttpContext.RequestAborted.IsCancellationRequested)
            {
                // Response stream can already be disposed after an abrupt client disconnect.
            }
        }
        catch (Exception ex)
        {
            LogChatStreamErrorMidStream(_Logger, ex, User.Identity?.Name);
            try
            {
                await Response.WriteAsync("data: {\"type\":\"error\",\"message\":\"Stream interrupted\"}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
            catch (Exception writeException) when (writeException is IOException or OperationCanceledException or ObjectDisposedException)
            {
                // Best-effort write to an already-streaming response. Kestrel can throw
                // IOException (connection reset), OperationCanceledException, or
                // ObjectDisposedException on abrupt client disconnect — swallow expected
                // transport/disconnect exceptions to avoid masking the original exception.
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Chat stream cancelled for user {User}")]
    private static partial void LogChatStreamCancelled(ILogger<ChatController> logger, string? user);

    [LoggerMessage(Level = LogLevel.Error, Message = "Chat streaming error before response started for user {User}")]
    private static partial void LogChatStreamErrorBeforeResponseStarted(ILogger<ChatController> logger, Exception exception, string? user);

    [LoggerMessage(Level = LogLevel.Error, Message = "Chat streaming error mid-stream for user {User}")]
    private static partial void LogChatStreamErrorMidStream(ILogger<ChatController> logger, Exception exception, string? user);
}
