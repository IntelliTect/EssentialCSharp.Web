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
public partial class ChatController : ControllerBase
{
    private readonly AIChatService _AiChatService;
    private readonly ResponseIdValidationService _ResponseIdValidationService;
    private readonly ILogger<ChatController> _Logger;

    public ChatController(ILogger<ChatController> logger, AIChatService aiChatService, ResponseIdValidationService responseIdValidationService)
    {
        _AiChatService = aiChatService;
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

        var previousResponseId = string.IsNullOrWhiteSpace(request.PreviousResponseId)
            ? null
            : request.PreviousResponseId.Trim();

        if (!_ResponseIdValidationService.ValidateResponseId(userId, previousResponseId))
            return BadRequest(new { error = "Invalid conversation context." });

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
            string? finalResponseId = null;

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
                    finalResponseId = responseId;
                    var eventData = JsonSerializer.Serialize(new { type = "responseId", data = responseId });
                    await Response.WriteAsync($"data: {eventData}\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                }
            }

            if (finalResponseId is not null)
            {
                _ResponseIdValidationService.RecordResponseId(userId, finalResponseId);
            }

            await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || HttpContext.RequestAborted.IsCancellationRequested)
        {
            LogChatStreamCancelled(_Logger, User.Identity?.Name);
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
            catch { /* client already disconnected */ }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Chat stream cancelled for user {User}")]
    private static partial void LogChatStreamCancelled(ILogger<ChatController> logger, string? user);

    [LoggerMessage(Level = LogLevel.Error, Message = "Chat streaming error before response started for user {User}")]
    private static partial void LogChatStreamErrorBeforeResponseStarted(ILogger<ChatController> logger, Exception exception, string? user);

    [LoggerMessage(Level = LogLevel.Error, Message = "Chat streaming error mid-stream for user {User}")]
    private static partial void LogChatStreamErrorMidStream(ILogger<ChatController> logger, Exception exception, string? user);
}
