using System.Text.Json;
using EssentialCSharp.Chat.Common.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EssentialCSharp.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // Require authentication for all chat endpoints
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
        try
        {
            if (_AiChatService == null)
            {
                return StatusCode(503, new { error = "AI Chat service is not available. Please check the configuration and try again later." });
            }

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
        catch (OperationCanceledException)
        {
            return StatusCode(499, new { error = "Request was cancelled." });
        }
        catch (Exception ex)
        {
            _Logger.LogError(ex, "Error processing chat message: {Message}", request.Message);
            return StatusCode(500, new { error = "An error occurred while processing your message. Please try again." });
        }
    }

    [HttpPost("stream")]
    public async Task StreamMessage([FromBody] ChatMessageRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_AiChatService == null)
            {
                Response.StatusCode = 503;
                await Response.WriteAsync(JsonSerializer.Serialize(new { error = "AI Chat service is not available. Please check the configuration and try again later." }), cancellationToken);
                return;
            }

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
        catch (OperationCanceledException)
        {
            Response.StatusCode = 499;
            await Response.WriteAsync(JsonSerializer.Serialize(new { error = "Request was cancelled." }), cancellationToken);
        }
        catch (Exception ex)
        {
            _Logger.LogError(ex, "Error processing streaming chat message: {Message}", request.Message);
            Response.StatusCode = 500;
            await Response.WriteAsync(JsonSerializer.Serialize(new { error = "An error occurred while processing your message. Please try again." }), cancellationToken);
        }
    }
}
