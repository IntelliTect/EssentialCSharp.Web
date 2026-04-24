using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Models;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class ResendEmailConfirmationModel(UserManager<EssentialCSharpWebUser> userManager, IEmailSender emailSender, ICaptchaService captchaService, IOptions<CaptchaOptions> optionsAccessor) : PageModel
{
    private InputModel? _Input;
    [BindProperty]
    public InputModel Input
    {
        get => _Input!;
        set => _Input = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string CaptchaSiteKey { get; } = optionsAccessor.Value.SiteKey ?? string.Empty;

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string? Email { get; set; }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        string? captchaToken = Request.Form[CaptchaOptions.HttpPostResponseKeyName];
        HCaptchaResult? captchaResult = await captchaService.VerifyAsync(captchaToken, HttpContext.Connection.RemoteIpAddress?.ToString());
        if (captchaResult?.Success != true)
        {
            ModelState.AddModelError(string.Empty, "Human verification failed. Please try again.");
            return Page();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (Input.Email is null)
        {
            ModelState.AddModelError(string.Empty, "Error: Email is null. Please enter in an email");
            return Page();
        }

        EssentialCSharpWebUser? user = await userManager.FindByEmailAsync(Input.Email);
        if (user is null)
        {
            // Don't reveal that the user does not exist — return the same success message
            ModelState.AddModelError(string.Empty, "Verification email sent. Please check your email. If you can't find the email, please check your spam folder.");
            return Page();
        }

        string userId = await userManager.GetUserIdAsync(user);
        string code = await userManager.GenerateEmailConfirmationTokenAsync(user);
        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
        string? callbackUrl = Url.Page(
            "/Account/ConfirmEmail",
            pageHandler: null,
            values: new { userId = userId, code = code },
            protocol: Request.Scheme);

        if (callbackUrl is null)
        {
            ModelState.AddModelError(string.Empty, "Error: callback url unexpectedly null.");
            return Page();
        }
        await emailSender.SendEmailAsync(
            Input.Email,
            "Confirm your email",
            $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.");

        ModelState.AddModelError(string.Empty, "Verification email sent. Please check your email. If you can't find the email, please check your spam folder.");
        return Page();
    }
}
