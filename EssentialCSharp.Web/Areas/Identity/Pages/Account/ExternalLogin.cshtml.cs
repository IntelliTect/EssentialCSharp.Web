using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using EssentialCSharp.Web.Areas.Identity.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class ExternalLoginModel : PageModel
{
    private readonly SignInManager<EssentialCSharpWebUser> _SignInManager;
    private readonly UserManager<EssentialCSharpWebUser> _UserManager;
    private readonly IUserStore<EssentialCSharpWebUser> _UserStore;
    private readonly IUserEmailStore<EssentialCSharpWebUser> _EmailStore;
    private readonly IEmailSender _EmailSender;
    private readonly ILogger<ExternalLoginModel> _Logger;

    public ExternalLoginModel(
        SignInManager<EssentialCSharpWebUser> signInManager,
        UserManager<EssentialCSharpWebUser> userManager,
        IUserStore<EssentialCSharpWebUser> userStore,
        ILogger<ExternalLoginModel> logger,
        IEmailSender emailSender)
    {
        _SignInManager = signInManager;
        _UserManager = userManager;
        _UserStore = userStore;
        _EmailStore = GetEmailStore();
        _Logger = logger;
        _EmailSender = emailSender;
    }

    private InputModel? _Input;
    [BindProperty]
    public InputModel Input
    {
        get => _Input!;
        set => _Input = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string? ProviderDisplayName { get; set; }

    public string? ReturnUrl { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string? Email { get; set; }
    }

    public IActionResult OnGet() => RedirectToPage("./Login");

    public IActionResult OnPost(string provider, string? returnUrl = null)
    {
        // Request a redirect to the external login provider.
        string redirectUrl = Url.Page("./ExternalLogin", pageHandler: "Callback", values: new { returnUrl }) ?? "/";
        Microsoft.AspNetCore.Authentication.AuthenticationProperties properties = _SignInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
        return new ChallengeResult(provider, properties);
    }

    public async Task<IActionResult> OnGetCallbackAsync(string? returnUrl = null, string? remoteError = null)
    {
        returnUrl ??= Url.Content("~/");
        if (remoteError is not null)
        {
            ErrorMessage = $"Error from external provider: {remoteError}";
            return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
        }
        ExternalLoginInfo? info = await _SignInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            ErrorMessage = "Error loading external login information.";
            return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
        }

        // Sign in the user with this external login provider if the user already has a login.
        Microsoft.AspNetCore.Identity.SignInResult result = await _SignInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);
        if (result.Succeeded)
        {
            _Logger.LogInformation("{Name} logged in with {LoginProvider} provider.", info.Principal.Identity?.Name, info.LoginProvider);
            return LocalRedirect(returnUrl);
        }
        if (result.IsLockedOut)
        {
            return RedirectToPage("./Lockout");
        }
        else
        {
            // If the user does not have an account, then ask the user to create an account.
            ReturnUrl = returnUrl;
            ProviderDisplayName = info.ProviderDisplayName;
            if (info.Principal.HasClaim(c => c.Type == ClaimTypes.Email))
            {
                Input = new()
                {
                    Email = info.Principal.FindFirstValue(ClaimTypes.Email)
                };
            }
            return Page();
        }
    }

    public async Task<IActionResult> OnPostConfirmationAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");
        // Get the information about the user from the external login provider
        ExternalLoginInfo? info = await _SignInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            ErrorMessage = "Error loading external login information during confirmation.";
            return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
        }

        if (ModelState.IsValid)
        {
            if (Input.Email is null)
            {
                ErrorMessage = "Error: Email may not be null.";
                return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
            }
            EssentialCSharpWebUser user = CreateUser();

            EssentialCSharpWebUser? existingUser = await _UserManager.FindByEmailAsync(Input.Email).ConfigureAwait(false);
            if (existingUser is null)
            {
                await _UserStore.SetUserNameAsync(user, Input.Email, CancellationToken.None);
                await _EmailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);
                IdentityResult result = await _UserManager.CreateAsync(user);
                if (result.Succeeded)
                {
                    result = await _UserManager.AddLoginAsync(user, info);
                    if (result.Succeeded)
                    {
                        _Logger.LogInformation("User created an account using {Name} provider.", info.LoginProvider);
                        return await SendConfirmationEmail(returnUrl, info, user);
                    }
                }
                foreach (IdentityError error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }
            else
            {
                if (!existingUser.EmailConfirmed)
                {
                    await SendConfirmationEmail(returnUrl, info, existingUser);
                }
            }
        }

        ProviderDisplayName = info.ProviderDisplayName;
        ReturnUrl = returnUrl;
        ModelState.AddModelError(string.Empty, "Please check confirmation email to complete registration.");
        return Page();
    }

    private async Task<IActionResult> SendConfirmationEmail(string returnUrl, ExternalLoginInfo info, EssentialCSharpWebUser user)
    {
        if (Input.Email is null)
        {
            ErrorMessage = "Error: Email may not be null.";
            return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
        }
        string userId = await _UserManager.GetUserIdAsync(user);
        string code = await _UserManager.GenerateEmailConfirmationTokenAsync(user);
        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
        string? callbackUrl = Url.Page(
            "/Account/ConfirmEmail",
            pageHandler: null,
            values: new { area = "Identity", userId = userId, code = code },
            protocol: Request.Scheme);

        if (callbackUrl is null)
        {
            ErrorMessage = "Error: callback url unexpectedly null.";
            return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
        }

        await _EmailSender.SendEmailAsync(Input.Email, "Confirm your email",
            $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.");

        // If account confirmation is required, we need to show the link if we don't have a real email sender
        if (_UserManager.Options.SignIn.RequireConfirmedAccount)
        {
            return RedirectToPage("./RegisterConfirmation", new { Email = Input.Email });
        }

        await _SignInManager.SignInAsync(user, isPersistent: false, info.LoginProvider);
        return LocalRedirect(returnUrl);
    }

    private EssentialCSharpWebUser CreateUser()
    {
        try
        {
            return Activator.CreateInstance<EssentialCSharpWebUser>();
        }
        catch (MissingMethodException innerException)
        {
            throw new InvalidOperationException($"Can't create an instance of '{nameof(EssentialCSharpWebUser)}'. " +
                $"Ensure that '{nameof(EssentialCSharpWebUser)}' is not an abstract class and has a parameterless constructor, or alternatively " +
                $"override the external login page in /Areas/Identity/Pages/Account/ExternalLogin.cshtml", innerException);
        }
    }

    private IUserEmailStore<EssentialCSharpWebUser> GetEmailStore()
    {
        if (!_UserManager.SupportsUserEmail)
        {
            throw new NotSupportedException("The default UI requires a user store with email support.");
        }
        return (IUserEmailStore<EssentialCSharpWebUser>)_UserStore;
    }
}
