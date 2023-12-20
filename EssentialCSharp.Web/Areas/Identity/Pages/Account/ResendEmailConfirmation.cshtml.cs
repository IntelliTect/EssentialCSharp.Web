using System.ComponentModel.DataAnnotations;
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
public class ResendEmailConfirmationModel : PageModel
{
    private readonly UserManager<EssentialCSharpWebUser> _UserManager;
    private readonly IEmailSender _EmailSender;

    public ResendEmailConfirmationModel(UserManager<EssentialCSharpWebUser> userManager, IEmailSender emailSender)
    {
        _UserManager = userManager;
        _EmailSender = emailSender;
    }

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
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (Input.Email is null)
        {
            ModelState.AddModelError(string.Empty, "Error: Email is null. Please enter in an email");
            return Page();
        }

        EssentialCSharpWebUser? user = await _UserManager.FindByEmailAsync(Input.Email);
        if (user is null)
        {
            return NotFound($"Unable to load user with ID '{_UserManager.GetUserId(User)}'.");
        }

        string userId = await _UserManager.GetUserIdAsync(user);
        string code = await _UserManager.GenerateEmailConfirmationTokenAsync(user);
        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
        string? callbackUrl = Url.Page(
            "/Account/ConfirmEmail",
            pageHandler: null,
            values: new { userId = userId, code = code },
            protocol: Request.Scheme);

        if (callbackUrl is null)
        {
            ModelState.AddModelError(string.Empty, "Error: callback url unexpectedly null.");
            return Page();
        }
        await _EmailSender.SendEmailAsync(
            Input.Email,
            "Confirm your email",
            $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.");

        ModelState.AddModelError(string.Empty, "Verification email sent. Please check your email. If you can't find the email, please check your spam folder.");
        return Page();
    }
}
