using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Extensions;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account;

public partial class ForgotPasswordModel(UserManager<EssentialCSharpWebUser> userManager, IEmailSender emailSender, ICaptchaValidationService captchaValidationService, IOptions<CaptchaOptions> optionsAccessor, ILogger<ForgotPasswordModel> logger, IOptions<SiteSettings> siteSettings) : PageModel
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

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken = default)
    {
        string? captchaToken = Request.Form[CaptchaOptions.HttpPostResponseKeyName];
        CaptchaValidationResult captchaResult = await captchaValidationService.ValidateAsync(
            captchaToken,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);
        if (!captchaResult.ShouldProceed)
        {
            ModelState.AddModelError(string.Empty, "Human verification failed. Please try again.");
            return Page();
        }

        if (ModelState.IsValid)
        {
            if (Input.Email is null)
            {
                return RedirectToPage("./ForgotPasswordConfirmation");
            }
            EssentialCSharpWebUser? user = await userManager.FindByEmailAsync(Input.Email);
            if (user is null || !(await userManager.IsEmailConfirmedAsync(user)))
            {
                // Don't reveal that the user does not exist or is not confirmed
                return RedirectToPage("./ForgotPasswordConfirmation");
            }

            // For more information on how to enable account confirmation and password reset please
            // visit https://go.microsoft.com/fwlink/?LinkID=532713
            string code = await userManager.GeneratePasswordResetTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            // Build the link from the configured site URL, not the request Host, so a forged Host header
            // cannot send the reset token to another site.
            string? callbackUrl = Url.EmailPageLink(
                siteSettings.Value,
                "/Account/ResetPassword",
                values: new { area = "Identity", code });

            if (callbackUrl is null)
            {
                return RedirectToPage("./ForgotPasswordConfirmation");
            }

            try
            {
                await emailSender.SendEmailAsync(
                    Input.Email,
                    "Reset Password",
                    $"Please reset your password by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.");
            }
            catch (EmailSendException)
            {
                // The email is only attempted when the account exists and is confirmed. Surfacing a failure here
                // would reveal that, enabling account enumeration, so log it and show the usual confirmation page.
                // Do not log the exception: its inner exception can contain the recipient address. EmailSender has logged the details.
                LogPasswordResetEmailNotSent(logger);
            }

            return RedirectToPage("./ForgotPasswordConfirmation");
        }

        return Page();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The password reset email could not be sent. The user was shown the standard confirmation page.")]
    private static partial void LogPasswordResetEmailNotSent(ILogger<ForgotPasswordModel> logger);
}
