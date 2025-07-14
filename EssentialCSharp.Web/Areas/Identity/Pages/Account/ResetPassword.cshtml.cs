using System.ComponentModel.DataAnnotations;
using System.Text;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Models;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account;

public class ResetPasswordModel(UserManager<EssentialCSharpWebUser> userManager, ICaptchaService captchaService, IOptions<CaptchaOptions> optionsAccessor) : PageModel
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

        [Required]
        [StringLength(Web.Services.PasswordRequirementOptions.PasswordMaximumLength, ErrorMessage = ValidationMessages.StringLengthErrorMessage, MinimumLength = Web.Services.PasswordRequirementOptions.PasswordMinimumLength)]
        [DataType(DataType.Password)]
        public string? Password { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string? ConfirmPassword { get; set; }

        [Required]
        public string? Code { get; set; }

    }

    public IActionResult OnGet(string? code = null)
    {
        if (code is null)
        {
            return BadRequest("A code must be supplied for password reset.");
        }
        else
        {
            Input = new InputModel
            {
                Code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code))
            };
            return Page();
        }
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
                ModelState.AddModelError(string.Empty, "Error: Email is required.");
                return RedirectToPage();
            }
            EssentialCSharpWebUser? user = await userManager.FindByEmailAsync(Input.Email);
            if (user is null)
            {
                // Don't reveal that the user does not exist
                return RedirectToPage("./ResetPasswordConfirmation");
            }

            if (Input.Password is null)
            {
                ModelState.AddModelError(string.Empty, "Error: Password is required.");
                return RedirectToPage();
            }
            if (Input.Code is null)
            {
                ModelState.AddModelError(string.Empty, "Error: Code is required.");
                return RedirectToPage();
            }

            IdentityResult result = await userManager.ResetPasswordAsync(user, Input.Code, Input.Password);
            if (result.Succeeded)
            {
                return RedirectToPage("./ResetPasswordConfirmation");
            }

            foreach (IdentityError error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
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
