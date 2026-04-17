using System.Text.Json;
using EssentialCSharp.Chat.Common.Services;
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
    private readonly AIChatService _AiChatService;
    private readonly ILogger<ChatController> _Logger;

    public ChatController(ILogger<ChatController> logger, AIChatService aiChatService)
    {
        _AiChatService = aiChatService;
        _Logger = logger;
    }

    [HttpPost("message")]
    public async Task<IActionResult> SendMessage([FromBody] ChatMessageRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new { error = "Message cannot be empty." });
        }

        // Require user authentication for chat
        if (!User.Identity?.IsAuthenticated ?? true)
        {
            return Unauthorized(new { error = "User must be logged in to use chat." });
        }

        var (response, responseId) = await _AiChatService.GetChatCompletion(
            prompt: request.Message,
            systemPrompt: request.SystemPrompt,
            previousResponseId: request.PreviousResponseId,
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
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            Response.StatusCode = 400;
            await Response.WriteAsync(JsonSerializer.Serialize(new { error = "Message cannot be empty." }), cancellationToken);
            return;
        }

        // Require user authentication for chat
        if (!User.Identity?.IsAuthenticated ?? true)
        {
            Response.StatusCode = 401;
            await Response.WriteAsync(JsonSerializer.Serialize(new { error = "User must be logged in to use chat." }), cancellationToken);
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        try
        {
            await foreach (var (text, responseId) in _AiChatService.GetChatCompletionStream(
                prompt: request.Message,
                systemPrompt: request.SystemPrompt,
                previousResponseId: request.PreviousResponseId,
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
}
