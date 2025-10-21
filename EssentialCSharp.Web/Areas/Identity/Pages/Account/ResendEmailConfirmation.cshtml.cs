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
    public CaptchaOptions CaptchaOptions { get; } = optionsAccessor.Value;
    private InputModel? _Input;
    [BindProperty]
    public InputModel Input
    {
        get => _Input!;
        set => _Input = value ?? throw new ArgumentNullException(nameof(value));
    }

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string? Email { get; set; }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        string? hCaptcha_response = Request.Form[CaptchaOptions.HttpPostResponseKeyName];

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (hCaptcha_response is null)
        {
            ModelState.AddModelError(CaptchaOptions.HttpPostResponseKeyName, HCaptchaErrorDetails.GetValue(HCaptchaErrorDetails.MissingInputResponse).FriendlyDescription);
            return Page();
        }

        HCaptchaResult? response = await captchaService.VerifyAsync(hCaptcha_response);
        if (response is null)
        {
            ModelState.AddModelError(CaptchaOptions.HttpPostResponseKeyName, "Error: HCaptcha API response unexpectedly null");
            return Page();
        }

        if (response.Success)
        {
            if (Input.Email is null)
            {
                ModelState.AddModelError(string.Empty, "Error: Email is null. Please enter in an email");
                return Page();
            }

            EssentialCSharpWebUser? user = await userManager.FindByEmailAsync(Input.Email);
            if (user is null)
            {
                return NotFound($"Unable to load user with ID '{userManager.GetUserId(User)}'.");
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
        else
        {
            switch (response.ErrorCodes?.Length)
            {
                case 0:
                    throw new InvalidOperationException("The HCaptcha determined the passcode is not valid, and does not meet the security criteria");
                case > 1:
                    throw new InvalidOperationException("HCaptcha returned error codes: " + string.Join(", ", response.ErrorCodes));
                default:
                    {
                        if (response.ErrorCodes is null)
                        {
                            throw new InvalidOperationException("HCaptcha returned error codes unexpectedly null");
                        }
                        if (HCaptchaErrorDetails.TryGetValue(response.ErrorCodes.Single(), out HCaptchaErrorDetails? details))
                        {
                            switch (details.ErrorCode)
                            {
                                case HCaptchaErrorDetails.MissingInputResponse:
                                case HCaptchaErrorDetails.InvalidInputResponse:
                                case HCaptchaErrorDetails.InvalidOrAlreadySeenResponse:
                                    ModelState.AddModelError(string.Empty, details.FriendlyDescription);
                                    break;
                                case HCaptchaErrorDetails.BadRequest:
                                    ModelState.AddModelError(string.Empty, details.FriendlyDescription);
                                    break;
                                case HCaptchaErrorDetails.MissingInputSecret:
                                case HCaptchaErrorDetails.InvalidInputSecret:
                                case HCaptchaErrorDetails.NotUsingDummyPasscode:
                                case HCaptchaErrorDetails.SitekeySecretMismatch:
                                    break;
                                default:
                                    throw new InvalidOperationException("HCaptcha returned unknown error code: " + details?.ErrorCode);
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException("HCaptcha returned unknown error code: " + response.ErrorCodes.Single());
                        }

                        break;
                    }

            }
        }

        return Page();
    }
}
