using System.ComponentModel.DataAnnotations;
using EssentialCSharp.Web.Areas.Identity.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account;

public class LoginModel : PageModel
{

    private readonly UserManager<EssentialCSharpWebUser> _UserManager;
    private readonly SignInManager<EssentialCSharpWebUser> _SignInManager;
    private readonly ILogger<LoginModel> _Logger;

    public LoginModel(SignInManager<EssentialCSharpWebUser> signInManager, UserManager<EssentialCSharpWebUser> userManager, ILogger<LoginModel> logger)
    {
        _SignInManager = signInManager;
        _Logger = logger;
        _UserManager = userManager;
    }

    private InputModel? _Input;
    [BindProperty]
    public InputModel Input { 
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

        ExternalLogins = (await _SignInManager.GetExternalAuthenticationSchemesAsync()).ToList();

        ReturnUrl = returnUrl;
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");

        ExternalLogins = (await _SignInManager.GetExternalAuthenticationSchemesAsync()).ToList();

        if (ModelState.IsValid)
        {
            Microsoft.AspNetCore.Identity.SignInResult result;
            if (Input.Email is null)
            {
                return RedirectToPage(Url.Content("~/"), new { ReturnUrl = returnUrl });
            }
            EssentialCSharpWebUser? foundUser = await _UserManager.FindByEmailAsync(Input.Email);
            if (Input.Password is null)
            {
                return RedirectToPage(Url.Content("~/"), new { ReturnUrl = returnUrl });
            }
            if (foundUser is not null)
            {
                result = await _SignInManager.PasswordSignInAsync(foundUser, Input.Password, Input.RememberMe, lockoutOnFailure: true);
            }
            else
            {
                result = await _SignInManager.PasswordSignInAsync(Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: true);
            }
            if (result.Succeeded)
            {
                _Logger.LogInformation("User logged in.");
                return LocalRedirect(returnUrl);
            }
            if (result.RequiresTwoFactor)
            {
                return RedirectToPage("./LoginWith2fa", new { ReturnUrl = returnUrl, RememberMe = Input.RememberMe });
            }
            if (result.IsLockedOut)
            {
                _Logger.LogWarning("User account locked out.");
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
}
