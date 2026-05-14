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
    /// <summary>
    /// hCaptcha token obtained from the client-side invisible widget.
    /// Required when <c>CaptchaOptions.SecretKey</c> is configured; ignored otherwise.
    /// </summary>
    [StringLength(2000)]
    public string? CaptchaResponse { get; set; }
}
