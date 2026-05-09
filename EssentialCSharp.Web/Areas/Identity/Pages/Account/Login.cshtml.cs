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

public partial class LoginModel(SignInManager<EssentialCSharpWebUser> signInManager, UserManager<EssentialCSharpWebUser> userManager, ILogger<LoginModel> logger, IReferralService referralService, ICaptchaService captchaService, IOptions<CaptchaOptions> optionsAccessor) : PageModel
{
    private InputModel? _Input;
    [BindProperty]
    public InputModel Input
    {
        get => _Input!;
        set => _Input = value ?? throw new ArgumentNullException(nameof(value));
    }

    public IList<AuthenticationScheme>? ExternalLogins { get; set; }

    public string? ReturnUrl { get; set; }

    public string CaptchaSiteKey { get; } = optionsAccessor.Value.SiteKey ?? string.Empty;

    [TempData]
    public string? ErrorMessage { get; set; }

    public class InputModel
    {

        [Required]
        [EmailAddress]
        public string? Email { get; set; }

        [Required]
        [MaxLength(PasswordRequirementOptions.PasswordMaximumLength)]
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

        string? captchaToken = Request.Form[CaptchaOptions.HttpPostResponseKeyName];
        HCaptchaResult? captchaResult = await captchaService.VerifyAsync(captchaToken, HttpContext.Connection.RemoteIpAddress?.ToString());
        if (captchaResult?.Success != true)
        {
            ModelState.AddModelError(string.Empty, "Human verification failed. Please try again.");
            ExternalLogins = (await signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
            return Page();
        }

        ExternalLogins = (await signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

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
                LogUserLoggedIn(logger);
                return LocalRedirect(returnUrl);
            }
            if (result.RequiresTwoFactor)
            {
                return RedirectToPage("./LoginWith2fa", new { ReturnUrl = returnUrl, RememberMe = Input.RememberMe });
            }
            if (result.IsLockedOut)
            {
                LogUserAccountLockedOut(logger);
                return RedirectToPage("./Lockout");
            }
            else
            {
                ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                return Page();
            }
        }

        // If we got this far, something failed, redisplay form
        return Page();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "User logged in.")]
    private static partial void LogUserLoggedIn(ILogger<LoginModel> logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "User account locked out.")]
    private static partial void LogUserAccountLockedOut(ILogger<LoginModel> logger);
}
