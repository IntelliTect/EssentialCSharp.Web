namespace EssentialCSharp.Web.Models;

/// <summary>
/// Model for the _HCaptchaWidget partial view.
/// </summary>
/// <param name="SiteKey">The hCaptcha site key.</param>
/// <param name="Size">"normal" for visible checkbox widget; "invisible" for challenge-only mode (free tier).</param>
/// <param name="Callback">Optional JS callback name invoked by hCaptcha after token is obtained (required for invisible mode).</param>
public record HCaptchaWidgetModel(string SiteKey, string Size = "normal", string? Callback = null);
