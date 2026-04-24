using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Models;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account;

public class RegisterModel(
    UserManager<EssentialCSharpWebUser> userManager,
    IUserStore<EssentialCSharpWebUser> userStore,
    SignInManager<EssentialCSharpWebUser> signInManager,
    ILogger<RegisterModel> logger,
    IEmailSender emailSender,
    ICaptchaService captchaService,
    IOptions<CaptchaOptions> optionsAccessor,
    IUserEmailStore<EssentialCSharpWebUser> emailStore) : PageModel
{
    public string CaptchaSiteKey { get; } = optionsAccessor.Value.SiteKey ?? string.Empty;

    private InputModel? _Input;
    [BindProperty]
    public InputModel Input
    {
        get => _Input!;
        set => _Input = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string? ReturnUrl { get; set; }

    public IList<AuthenticationScheme>? ExternalLogins { get; set; }

    public class InputModel
    {
        [Required]
        [DataType(DataType.Text)]
        [Display(Name = "User Name")]
        public string? UserName { get; set; }

        [Required]
        [DataType(DataType.Text)]
        [Display(Name = "First Name")]
        public string? FirstName { get; set; }

        [Required]
        [DataType(DataType.Text)]
        [Display(Name = "Last Name")]
        public string? LastName { get; set; }

        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public string? Email { get; set; }

        [Required]
        [StringLength(PasswordRequirementOptions.PasswordMaximumLength, ErrorMessage = ValidationMessages.StringLengthErrorMessage, MinimumLength = PasswordRequirementOptions.PasswordMinimumLength)]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string? Password { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string? ConfirmPassword { get; set; }
    }

    public async Task OnGetAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
        ExternalLogins = (await signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");
        ExternalLogins = (await signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
        string? hCaptcha_response = Request.Form[CaptchaOptions.HttpPostResponseKeyName];

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (string.IsNullOrEmpty(hCaptcha_response))
        {
            ModelState.AddModelError(string.Empty, HCaptchaErrorDetails.GetValue(HCaptchaErrorDetails.MissingInputResponse).FriendlyDescription);
            return Page();
        }

        HCaptchaResult? response = await captchaService.VerifyAsync(hCaptcha_response, HttpContext.Connection.RemoteIpAddress?.ToString());
        if (response is null)
        {
            ModelState.AddModelError(string.Empty, "Captcha verification is temporarily unavailable. Please try again later.");
            return Page();
        }

        // The JSON should also return a field "success" as true
        // https://docs.hcaptcha.com/#verify-the-user-response-server-side
        if (response.Success)
        {
            EssentialCSharpWebUser user = CreateUser();
            user.FirstName = Input.FirstName;
            user.LastName = Input.LastName;

            await userStore.SetUserNameAsync(user, Input.UserName, CancellationToken.None);
            await emailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);
            if (Input.Password is null)
            {
                logger.LogInformation("Error: Password null; please enter in a password");
                ModelState.AddModelError(string.Empty, "Error: Password null; please enter in a password");
                return Page();
            }
            IdentityResult result = await userManager.CreateAsync(user, Input.Password);

            if (result.Succeeded)
            {
                logger.LogInformation("User created a new account with password.");

                string userId = await userManager.GetUserIdAsync(user);
                string code = await userManager.GenerateEmailConfirmationTokenAsync(user);
                code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                string? callbackUrl = Url.Page(
                    "/Account/ConfirmEmail",
                    pageHandler: null,
                    values: new { area = "Identity", userId = userId, code = code, returnUrl = returnUrl },
                    protocol: Request.Scheme);

                if (callbackUrl is null)
                {
                    ModelState.AddModelError(string.Empty, "Error: callback url unexpectedly null.");
                    return Page();
                }
                if (Input.Email is null)
                {
                    ModelState.AddModelError(string.Empty, "Error: Email may not be null.");
                    return Page();
                }
                await emailSender.SendEmailAsync(Input.Email, "Confirm your email",
                    $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.");

                if (userManager.Options.SignIn.RequireConfirmedAccount)
                {
                    return RedirectToPage("RegisterConfirmation", new { email = Input.Email, returnUrl = returnUrl });
                }
                else
                {
                    await signInManager.SignInAsync(user, isPersistent: false);
                    return LocalRedirect(returnUrl);
                }
            }
            foreach (IdentityError error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }
        else
        {
            switch (response.ErrorCodes?.Length)
            {
                case 0:
                    logger.LogError("HCaptcha determined the passcode is not valid with zero error codes");
                    ModelState.AddModelError(string.Empty, "Captcha verification failed. Please try again.");
                    break;
                case > 1:
                    logger.LogError("HCaptcha returned multiple error codes: {ErrorCodes}", string.Join(", ", response.ErrorCodes));
                    ModelState.AddModelError(string.Empty, "Captcha verification failed. Please try again.");
                    break;
                default:
                    {
                        if (response.ErrorCodes is null)
                        {
                            logger.LogError("HCaptcha returned null error codes with Success=false");
                            ModelState.AddModelError(string.Empty, "Captcha verification failed. Please try again.");
                            break;
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
                                    ModelState.AddModelError(string.Empty, "Captcha verification is temporarily unavailable. Please try again later.");
                                    break;
                                default:
                                    logger.LogError("HCaptcha returned unknown error code: {ErrorCode}", details?.ErrorCode);
                                    ModelState.AddModelError(string.Empty, "Captcha verification failed. Please try again.");
                                    break;
                            }
                        }
                        else
                        {
                            logger.LogError("HCaptcha returned unrecognized error code: {ErrorCode}", response.ErrorCodes.Single());
                            ModelState.AddModelError(string.Empty, "Captcha verification failed. Please try again.");
                        }

                        break;
                    }

            }
        }

        // If we got this far, something failed, redisplay form
        return Page();
    }

    private EssentialCSharpWebUser CreateUser()
    {
        try
        {
            return new EssentialCSharpWebUser();
        }
        catch
        {
            throw new InvalidOperationException($"Can't create an instance of '{nameof(EssentialCSharpWebUser)}'. " +
                $"Ensure that '{nameof(EssentialCSharpWebUser)}' is not an abstract class and has a parameterless constructor, or alternatively " +
                $"override the register page in /Areas/Identity/Pages/Account/Register.cshtml");
        }
    }
}
