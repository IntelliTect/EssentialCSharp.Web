using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using EssentialCSharp.Web.Areas.Identity.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account.Manage;

public class EmailModel : PageModel
{
    private readonly UserManager<EssentialCSharpWebUser> _UserManager;
    private readonly SignInManager<EssentialCSharpWebUser> _SignInManager;
    private readonly IEmailSender _EmailSender;

    public EmailModel(
        UserManager<EssentialCSharpWebUser> userManager,
        SignInManager<EssentialCSharpWebUser> signInManager,
        IEmailSender emailSender)
    {
        _UserManager = userManager;
        _SignInManager = signInManager;
        _EmailSender = emailSender;
    }

    public string? Email { get; set; }

    public bool IsEmailConfirmed { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

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
        [Display(Name = "New email")]
        public string? NewEmail { get; set; }
    }

    private async Task LoadAsync(EssentialCSharpWebUser user)
    {
        string? email = await _UserManager.GetEmailAsync(user);
        Email = email;

        Input = new InputModel
        {
            NewEmail = email,
        };

        IsEmailConfirmed = await _UserManager.IsEmailConfirmedAsync(user);
    }

    public async Task<IActionResult> OnGetAsync()
    {
        EssentialCSharpWebUser? user = await _UserManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound($"Unable to load user with ID '{_UserManager.GetUserId(User)}'.");
        }

        await LoadAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostChangeEmailAsync()
    {
        EssentialCSharpWebUser? user = await _UserManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound($"Unable to load user with ID '{_UserManager.GetUserId(User)}'.");
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(user);
            return Page();
        }

        string? email = await _UserManager.GetEmailAsync(user);
        if (Input.NewEmail != email)
        {
            string userId = await _UserManager.GetUserIdAsync(user);
            if (Input.NewEmail is null)
            {
                StatusMessage = "Please enter in a new email.";
                return RedirectToPage();
            }
            string code = await _UserManager.GenerateChangeEmailTokenAsync(user, Input.NewEmail);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            string? callbackUrl = Url.Page(
                "/Account/ConfirmEmailChange",
                pageHandler: null,
                values: new { area = "Identity", userId = userId, email = Input.NewEmail, code = code },
                protocol: Request.Scheme);
            if (callbackUrl is null)
            {
                StatusMessage = "Error: callback url unexpectedly null.";
                return RedirectToPage();
            }
            await _EmailSender.SendEmailAsync(
                Input.NewEmail,
                "Confirm your email",
                $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.");

            StatusMessage = "Confirmation link to change email sent. Please check your email. If you can't find the email, please check your spam folder.";
            return RedirectToPage();
        }

        StatusMessage = "Your email is unchanged.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSendVerificationEmailAsync()
    {
        EssentialCSharpWebUser? user = await _UserManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound($"Unable to load user with ID '{_UserManager.GetUserId(User)}'.");
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(user);
            return Page();
        }

        string userId = await _UserManager.GetUserIdAsync(user);
        string? email = await _UserManager.GetEmailAsync(user);
        string code = await _UserManager.GenerateEmailConfirmationTokenAsync(user);
        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
        string? callbackUrl = Url.Page(
            "/Account/ConfirmEmail",
            pageHandler: null,
            values: new { area = "Identity", userId = userId, code = code },
            protocol: Request.Scheme);
        if (callbackUrl is null)
        {
            StatusMessage = "Error: callback url unexpectedly null.";
            return RedirectToPage();
        }
        if (email is null)
        {
            return NotFound($"Unable to load user email with ID '{_UserManager.GetUserId(User)}'.");
        }
        await _EmailSender.SendEmailAsync(
            email,
            "Confirm your email",
            $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.");

        StatusMessage = "Verification email sent. Please check your email. If you can't find the email, please check your spam folder.";
        return RedirectToPage();
    }
}
