using System.ComponentModel.DataAnnotations;

namespace EssentialCSharp.Web.Controllers;

public class ChatMessageRequest
{
    [Required]
    [StringLength(500)]
    public string Message { get; set; } = string.Empty;
    [StringLength(200)]
    public string? PreviousResponseId { get; set; }
    public bool EnableContextualSearch { get; set; } = true;
    public string? CaptchaResponse { get; set; } // hCaptcha token; validated server-side when chat captcha enforcement is wired up
}
