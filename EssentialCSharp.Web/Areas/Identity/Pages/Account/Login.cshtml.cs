using System.ComponentModel.DataAnnotations;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Models;
using EssentialCSharp.Web.Services;
using EssentialCSharp.Web.Services.Referrals;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account;

public class LoginModel(SignInManager<EssentialCSharpWebUser> signInManager, UserManager<EssentialCSharpWebUser> userManager, ILogger<LoginModel> logger, IReferralService referralService, ICaptchaService captchaService, IOptions<CaptchaOptions> optionsAccessor) : PageModel
{
    public CaptchaOptions CaptchaOptions { get; } = optionsAccessor.Value;
    private InputModel? _Input;
    [BindProperty]
    public InputModel Input
    {
        get => _Input!;
        set => _Input = value ?? throw new ArgumentNullException(nameof(value));
    }

    public IList<AuthenticationScheme>? ExternalLogins { get; set; }

    public string? ReturnUrl { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public class InputModel
    {

        [Required]
        [EmailAddress]
        public string? Email { get; set; }

        [Required]
        [DataType(DataType.Password)]
        public string? Password { get; set; }

        [Display(Name = "Remember me?")]
        public bool RememberMe { get; set; }
    }

    public async Task OnGetAsync(string? returnUrl = null)
    {
        if (!string.IsNullOrEmpty(ErrorMessage))
        {
            ModelState.AddModelError(string.Empty, ErrorMessage);
        }

        returnUrl ??= Url.Content("~/");

        // Clear the existing external cookie to ensure a clean login process
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        ExternalLogins = (await signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

        ReturnUrl = returnUrl;
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");
        string? hCaptcha_response = Request.Form[CaptchaOptions.HttpPostResponseKeyName];

        ExternalLogins = (await signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

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
            if (ModelState.IsValid)
            {
                Microsoft.AspNetCore.Identity.SignInResult result;
                if (Input.Email is null)
                {
                    return RedirectToPage(Url.Content("~/"), new { ReturnUrl = returnUrl });
                }
                EssentialCSharpWebUser? foundUser = await userManager.FindByEmailAsync(Input.Email);
                if (Input.Password is null)
                {
                    return RedirectToPage(Url.Content("~/"), new { ReturnUrl = returnUrl });
                }
                if (foundUser is not null)
                {
                    result = await signInManager.PasswordSignInAsync(foundUser, Input.Password, Input.RememberMe, lockoutOnFailure: true);
                    // Call the referral service to get the referral ID and set it onto the user claim
                    _ = await referralService.EnsureReferralIdAsync(foundUser);
                }
                else
                {
                    result = await signInManager.PasswordSignInAsync(Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: true);
                }
                if (result.Succeeded)
                {
                    logger.LogInformation("User logged in.");
                    return LocalRedirect(returnUrl);
                }
                if (result.RequiresTwoFactor)
                {
                    return RedirectToPage("./LoginWith2fa", new { ReturnUrl = returnUrl, RememberMe = Input.RememberMe });
                }
                if (result.IsLockedOut)
                {
                    logger.LogWarning("User account locked out.");
                    return RedirectToPage("./Lockout");
                }
                else
                {
                    ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                    return Page();
                }
            }
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
                                    logger.LogInformation("HCaptcha returned error code: {ErrorDetails}", details.ToString());
                                    break;
                                case HCaptchaErrorDetails.BadRequest:
                                    ModelState.AddModelError(string.Empty, details.FriendlyDescription);
                                    logger.LogInformation("HCaptcha returned error code: {ErrorDetails}", details.ToString());
                                    break;
                                case HCaptchaErrorDetails.MissingInputSecret:
                                case HCaptchaErrorDetails.InvalidInputSecret:
                                case HCaptchaErrorDetails.NotUsingDummyPasscode:
                                case HCaptchaErrorDetails.SitekeySecretMismatch:
                                    logger.LogCritical("HCaptcha returned error code: {ErrorDetails}", details.ToString());
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

        // If we got this far, something failed, redisplay form
        return Page();
    }
}
