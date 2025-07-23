namespace EssentialCSharp.Web.Controllers;

public class ChatMessageRequest
{
    public string Message { get; set; } = string.Empty;
    public string? SystemPrompt { get; set; }
    public string? PreviousResponseId { get; set; }
    public bool EnableContextualSearch { get; set; } = true;
    public string? CaptchaResponse { get; set; } // For future captcha implementation
}
