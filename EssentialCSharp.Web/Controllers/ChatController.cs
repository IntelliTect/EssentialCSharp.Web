using System.Text.Json;
using EssentialCSharp.Chat.Common.Services;
using Microsoft.AspNetCore.Mvc;

namespace EssentialCSharp.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
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

            // TODO: Add user authentication check here when implementing auth
            // if (!User.Identity.IsAuthenticated)
            // {
            //     return Unauthorized(new { error = "User must be logged in to use chat." });
            // }

            // TODO: Add captcha verification here when implementing captcha
            // if (!await _captchaService.VerifyAsync(request.CaptchaResponse))
            // {
            //     return BadRequest(new { error = "Captcha verification failed." });
            // }

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

            // TODO: Add user authentication check here when implementing auth
            // TODO: Add captcha verification here when implementing captcha

            Response.ContentType = "text/event-stream";
            Response.Headers["Cache-Control"] = "no-cache";
            Response.Headers["Connection"] = "keep-alive";

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

public class ChatMessageRequest
{
    public string Message { get; set; } = string.Empty;
    public string? SystemPrompt { get; set; }
    public string? PreviousResponseId { get; set; }
    public bool EnableContextualSearch { get; set; } = true;
    public string? CaptchaResponse { get; set; } // For future captcha implementation
}

public class ChatMessageResponse
{
    public string Response { get; set; } = string.Empty;
    public string ResponseId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
