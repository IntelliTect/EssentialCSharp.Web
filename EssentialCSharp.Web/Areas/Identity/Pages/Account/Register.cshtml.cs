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

public class RegisterModel : PageModel
{
    private readonly SignInManager<EssentialCSharpWebUser> _SignInManager;
    private readonly UserManager<EssentialCSharpWebUser> _UserManager;
    private readonly IUserStore<EssentialCSharpWebUser> _UserStore;
    private readonly IUserEmailStore<EssentialCSharpWebUser> _EmailStore;
    private readonly ILogger<RegisterModel> _Logger;
    private readonly IEmailSender _EmailSender;
    private readonly ICaptchaService _CaptchaService;
    public CaptchaOptions CaptchaOptions { get; } //Set with Secret Manager.

    public RegisterModel(
        UserManager<EssentialCSharpWebUser> userManager,
        IUserStore<EssentialCSharpWebUser> userStore,
        SignInManager<EssentialCSharpWebUser> signInManager,
        ILogger<RegisterModel> logger,
        IEmailSender emailSender,
        ICaptchaService captchaService,
        IOptions<CaptchaOptions> optionsAccessor,
        IUserEmailStore<EssentialCSharpWebUser> emailStore)
    {
        _UserManager = userManager;
        _UserStore = userStore;
        _EmailStore = emailStore;
        _SignInManager = signInManager;
        _Logger = logger;
        _EmailSender = emailSender;
        _CaptchaService = captchaService;
        CaptchaOptions = optionsAccessor.Value;
    }

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
        [StringLength(Web.Services.PasswordRequirementOptions.PasswordMaximumLength, ErrorMessage = ValidationMessages.StringLengthErrorMessage, MinimumLength = Web.Services.PasswordRequirementOptions.PasswordMinimumLength)]
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
        ExternalLogins = (await _SignInManager.GetExternalAuthenticationSchemesAsync()).ToList();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");
        string? hCaptcha_response = Request.Form[CaptchaOptions.HttpPostResponseKeyName];

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (hCaptcha_response is null)
        {
            throw new InvalidOperationException($"{nameof(hCaptcha_response)} is null");
        }

        HCaptchaResult response = await _CaptchaService.VerifyAsync(hCaptcha_response) ?? throw new InvalidOperationException("HCaptcha returned a null response");

        // The JSON should also return a field "success" as true
        // https://docs.hcaptcha.com/#verify-the-user-response-server-side
        // TODO: Implement this properly!!
        response.Success = true;
        if (response.Success)
        {
            ExternalLogins = (await _SignInManager.GetExternalAuthenticationSchemesAsync()).ToList();
            if (ModelState.IsValid)
            {
                EssentialCSharpWebUser user = CreateUser();
                user.FirstName = Input.FirstName;
                user.LastName = Input.LastName;

                await _UserStore.SetUserNameAsync(user, Input.UserName, CancellationToken.None);
                await _EmailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);
                if (Input.Password is null)
                {
                    ModelState.AddModelError(string.Empty, "Error: Password null; please enter in a password");
                    return Page();
                }
                IdentityResult result = await _UserManager.CreateAsync(user, Input.Password);

                if (result.Succeeded)
                {
                    _Logger.LogInformation("User created a new account with password.");

                    string userId = await _UserManager.GetUserIdAsync(user);
                    string code = await _UserManager.GenerateEmailConfirmationTokenAsync(user);
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
                    await _EmailSender.SendEmailAsync(Input.Email, "Confirm your email",
                        $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.");

                    if (_UserManager.Options.SignIn.RequireConfirmedAccount)
                    {
                        return RedirectToPage("RegisterConfirmation", new { email = Input.Email, returnUrl = returnUrl });
                    }
                    else
                    {
                        await _SignInManager.SignInAsync(user, isPersistent: false);
                        return LocalRedirect(returnUrl);
                    }
                }
                foreach (IdentityError error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
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
                                    _Logger.LogInformation("HCaptcha returned error code: {ErrorDetails}", details.ToString());
                                    break;
                                case HCaptchaErrorDetails.BadRequest:
                                    ModelState.AddModelError(string.Empty, details.FriendlyDescription);
                                    _Logger.LogInformation("HCaptcha returned error code: {ErrorDetails}", details.ToString());
                                    break;
                                case HCaptchaErrorDetails.MissingInputSecret:
                                case HCaptchaErrorDetails.InvalidInputSecret:
                                case HCaptchaErrorDetails.NotUsingDummyPasscode:
                                case HCaptchaErrorDetails.SitekeySecretMismatch:
                                    _Logger.LogCritical("HCaptcha returned error code: {ErrorDetails}", details.ToString());
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
