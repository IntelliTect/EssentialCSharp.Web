using System.ComponentModel.DataAnnotations;
using System.Text;
using EssentialCSharp.Web.Areas.Identity.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account;

public class ResetPasswordModel : PageModel
{
    private readonly UserManager<EssentialCSharpWebUser> _UserManager;

    public ResetPasswordModel(UserManager<EssentialCSharpWebUser> userManager)
    {
        _UserManager = userManager;
    }

    private InputModel? _Input;
    [BindProperty]
    public InputModel Input
    {
        get => _Input ?? throw new InvalidOperationException();
        set => _Input = value ?? throw new ArgumentNullException(nameof(value));
    }

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string? Email { get; set; }

        [Required]
        [StringLength(Web.Services.PasswordRequirementOptions.PasswordMaximumLength, ErrorMessage = ValidationMessages.StringLengthErrorMessage, MinimumLength = Web.Services.PasswordRequirementOptions.PasswordMinimumLength)]
        [DataType(DataType.Password)]
        public string? Password { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string? ConfirmPassword { get; set; }

        [Required]
        public string? Code { get; set; }

    }

    public IActionResult OnGet(string? code = null)
    {
        if (code is null)
        {
            return BadRequest("A code must be supplied for password reset.");
        }
        else
        {
            Input = new InputModel
            {
                Code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code))
            };
            return Page();
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (Input.Email is null)
        {
            ModelState.AddModelError(string.Empty, "Error: Email is required.");
            return RedirectToPage();
        }
        EssentialCSharpWebUser? user = await _UserManager.FindByEmailAsync(Input.Email);
        if (user is null)
        {
            // Don't reveal that the user does not exist
            return RedirectToPage("./ResetPasswordConfirmation");
        }

        if (Input.Password is null)
        {
            ModelState.AddModelError(string.Empty, "Error: Password is required.");
            return RedirectToPage();
        }
        if (Input.Code is null)
        {
            ModelState.AddModelError(string.Empty, "Error: Code is required.");
            return RedirectToPage();
        }

        IdentityResult result = await _UserManager.ResetPasswordAsync(user, Input.Code, Input.Password);
        if (result.Succeeded)
        {
            return RedirectToPage("./ResetPasswordConfirmation");
        }

        foreach (IdentityError error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }
        return Page();
    }
}
