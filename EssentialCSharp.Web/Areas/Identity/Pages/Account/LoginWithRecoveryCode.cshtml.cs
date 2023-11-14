using System.ComponentModel.DataAnnotations;
using EssentialCSharp.Web.Areas.Identity.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace EssentialCSharp.Web.Areas.Identity.Pages.Account;

public class LoginWithRecoveryCodeModel : PageModel
{

    private readonly SignInManager<EssentialCSharpWebUser> _SignInManager;
    private readonly UserManager<EssentialCSharpWebUser> _UserManager;
    private readonly ILogger<LoginWithRecoveryCodeModel> _Logger;

    public LoginWithRecoveryCodeModel(
        SignInManager<EssentialCSharpWebUser> signInManager,
        UserManager<EssentialCSharpWebUser> userManager,
        ILogger<LoginWithRecoveryCodeModel> logger)
    {
        _SignInManager = signInManager;
        _UserManager = userManager;
        _Logger = logger;
    }

    private InputModel? _Input;
    [BindProperty]
    public InputModel Input
    {
        get => _Input!;
        set => _Input = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string? ReturnUrl { get; set; }

    public class InputModel
    {

        [BindProperty]
        [Required]
        [DataType(DataType.Text)]
        [Display(Name = "Recovery Code")]
        public string? RecoveryCode { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        // Ensure the user has gone through the username & password screen first
        EssentialCSharpWebUser user = await _SignInManager.GetTwoFactorAuthenticationUserAsync() ?? throw new InvalidOperationException($"Unable to load two-factor authentication user.");
        ReturnUrl = returnUrl;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        EssentialCSharpWebUser user = await _SignInManager.GetTwoFactorAuthenticationUserAsync() ?? throw new InvalidOperationException($"Unable to load two-factor authentication user.");
        if (Input.RecoveryCode is null)
        {
            return Page();
        }
        string recoveryCode = Input.RecoveryCode.Replace(" ", string.Empty);

        Microsoft.AspNetCore.Identity.SignInResult result = await _SignInManager.TwoFactorRecoveryCodeSignInAsync(recoveryCode);

        string userId = await _UserManager.GetUserIdAsync(user);

        if (result.Succeeded)
        {
            _Logger.LogInformation("User with ID '{UserId}' logged in with a recovery code.", user.Id);
            return LocalRedirect(returnUrl ?? Url.Content("~/"));
        }
        if (result.IsLockedOut)
        {
            _Logger.LogWarning("User account locked out.");
            return RedirectToPage("./Lockout");
        }
        else
        {
            _Logger.LogWarning("Invalid recovery code entered for user with ID '{UserId}' ", user.Id);
            ModelState.AddModelError(string.Empty, "Invalid recovery code entered.");
            return Page();
        }
    }
}
