using System.ComponentModel.DataAnnotations;
using EssentialCSharp.Web.Areas.Identity.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace EssentialCSharp.Web.Areas.Identity.Pages.Account;

public partial class LoginWithRecoveryCodeModel(
    SignInManager<EssentialCSharpWebUser> signInManager,
    UserManager<EssentialCSharpWebUser> userManager,
    ILogger<LoginWithRecoveryCodeModel> logger) : PageModel
{
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
        EssentialCSharpWebUser user = await signInManager.GetTwoFactorAuthenticationUserAsync() ?? throw new InvalidOperationException($"Unable to load two-factor authentication user.");
        ReturnUrl = returnUrl;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        EssentialCSharpWebUser user = await signInManager.GetTwoFactorAuthenticationUserAsync() ?? throw new InvalidOperationException($"Unable to load two-factor authentication user.");
        if (Input.RecoveryCode is null)
        {
            return Page();
        }
        string recoveryCode = Input.RecoveryCode.Replace(" ", string.Empty);

        Microsoft.AspNetCore.Identity.SignInResult result = await signInManager.TwoFactorRecoveryCodeSignInAsync(recoveryCode);

        string userId = await userManager.GetUserIdAsync(user);

        if (result.Succeeded)
        {
            LogUserLoggedInWithRecoveryCode(logger, user.Id);
            return LocalRedirect(returnUrl ?? Url.Content("~/"));
        }
        if (result.IsLockedOut)
        {
            LogUserAccountLockedOutRecovery(logger);
            return RedirectToPage("./Lockout");
        }
        else
        {
            LogInvalidRecoveryCode(logger, user.Id);
            ModelState.AddModelError(string.Empty, "Invalid recovery code entered.");
            return Page();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "User with ID '{UserId}' logged in with a recovery code.")]
    private static partial void LogUserLoggedInWithRecoveryCode(ILogger<LoginWithRecoveryCodeModel> logger, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "User account locked out.")]
    private static partial void LogUserAccountLockedOutRecovery(ILogger<LoginWithRecoveryCodeModel> logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid recovery code entered for user with ID '{UserId}' ")]
    private static partial void LogInvalidRecoveryCode(ILogger<LoginWithRecoveryCodeModel> logger, string userId);
}
